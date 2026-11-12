using System.Collections.ObjectModel;
using ArcadeGridz.App.Models;
using ArcadeGridz.App.Runtimes;
using ArcadeGridz.App.Services;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;
using WinRT.Interop;

namespace ArcadeGridz.App;

public sealed partial class MainWindow : Window
{
    enum LauncherFocus { Categories, Games, Scores, Storyboards, Options, Exit, GameGrid, Play, SignIn }
    enum StartupFocus { Email, Google, Guest, Terms, Privacy }
    const string TermsUrl = "https://griddim.com/terms/";
    const string PrivacyUrl = "https://griddim.com/privacy/";
    readonly CatalogService catalogs = new();
    readonly LeaderboardService leaderboards = new();
    readonly SettingsService settings = SettingsService.Load();
    readonly UpdateService updates = new();
    readonly DesktopAuthService desktopAuth = new();
    readonly DailyRewardsService daily = new();
    DailyState? pendingDaily;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? launchWatchdog;
    readonly ObservableCollection<GameEntry> visible = [];
    readonly List<GameEntry> all = [];
    readonly List<string> categories = [];
    readonly NativeGameRuntime native = new();
    GamepadService? pads;
    WebGameRuntime? web;
    IGameRuntime? active;
    int categoryIndex;
    // storyboards (interactive films) live in their own section, not in ALL GAMES or the genre list
    const string StoryboardCategory = "STORYBOARD";
    bool storyMode;
    static bool IsStoryboard(GameEntry g) => g.Category.Equals(StoryboardCategory, StringComparison.OrdinalIgnoreCase);
    bool gameOpen;
    UpdateInfo? availableUpdate;
    LauncherFocus launcherFocus = LauncherFocus.GameGrid;
    int panelFocus;
    int optionsTab; // 0 GAMES, 1 DISPLAY, 2 SYSTEM, 3 UPDATES, 4 ABOUT
    const int OptionsTabCount = 5;
    NetworkAccount? account;
    StartupFocus startupFocus = StartupFocus.Email;
    bool startupOpen = true;
    bool consoleMode;
    bool initializingSettings;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? clockTimer;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? splashTimer;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? revealFailsafe;
    bool cloaked, revealed;
    bool signInPanelOpen, signInFromWelcome;
    static readonly System.Diagnostics.Stopwatch SinceLaunch = System.Diagnostics.Stopwatch.StartNew();
    static readonly TimeSpan MinimumSplash = TimeSpan.FromMilliseconds(1200);
    CancellationTokenSource? heroLoad;
    // live catalog: re-checked after a game, when the window regains focus and every few minutes (ETag, so cheap)
    enum CatalogRefresh { Changed, Same, Offline, Failed }
    static readonly TimeSpan CatalogCheckThrottle = TimeSpan.FromSeconds(20);
    DateTime lastCatalogCheck = DateTime.MinValue;
    bool catalogBusy;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? catalogTimer;

    public MainWindow()
    {
        StartupLog.Write("window", "Constructor entered");
        try
        {
            InitializeComponent();
            StartupLog.Write("window", "InitializeComponent completed (WebView2 deferred)");
            // Load fullscreen but cloaked (invisible) behind the overlay splash; Reveal() uncloaks it once the
            // launcher or sign-in panel is ready, so there is never a full-screen loading screen.
            SetCloaked(true);
            revealFailsafe = DispatcherQueue.CreateTimer();
            // a cold WebView profile can take up to 12s to answer the saved-session check; don't hold the splash
            // that long: show the sign-in card, and a late "already signed in" answer still skips to the launcher
            revealFailsafe.Interval = TimeSpan.FromSeconds(8);
            revealFailsafe.IsRepeating = false;
            revealFailsafe.Tick += (_, _) =>
            {
                StartupLog.Write("window", "Reveal failsafe fired; showing sign-in while the session check finishes");
                if (startupOpen) ShowWelcome();
                Reveal();
            };
            revealFailsafe.Start();
            GameGrid.ItemsSource = visible;
            // mouse wheel scrolls the game grid (handled ourselves so nothing in the tile template can swallow it)
            Root.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Root_PointerWheelChanged), true);
            Root.SizeChanged += (_, _) => FitLauncher();
            native.Exited += (_, _) => DispatcherQueue.TryEnqueue(ReturnToLauncher);
            Activated += OnActivated;
            Root.Loaded += OnLoaded;
        }
        catch (Exception ex)
        {
            StartupLog.Exception("MainWindow constructor", ex, true);
            throw;
        }
    }

    WebGameRuntime EnsureWebRuntime()
    {
        if (web is not null) return web;
        StartupLog.Write("webview", "Creating WebView2 control on demand");
        var view = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        WebViewHost.Children.Insert(0, view);
        web = new WebGameRuntime(view, settings.DisableGpu, settings.Quality);
        web.SetConsoleMode(consoleMode);
        web.Error += error => DispatcherQueue.TryEnqueue(() => OnPageError(error));
        web.Navigating += url => DispatcherQueue.TryEnqueue(() => OnGameNavigating(url));
        web.Exited += (_, _) => DispatcherQueue.TryEnqueue(ReturnToLauncher);
        web.AccountChanged += a => DispatcherQueue.TryEnqueue(() => SetAccount(a));
        web.Status += status => DispatcherQueue.TryEnqueue(() =>
        {
            GameStatus.Text = status.ToUpperInvariant();
            if (status.Equals("started", StringComparison.OrdinalIgnoreCase))
            {
                GameCover.Visibility = Visibility.Collapsed;
                StopStoryboard("CoverPulse");
                launchWatchdog?.Stop();
            }
            else if (status.Equals("auth", StringComparison.OrdinalIgnoreCase))
            {
                NetworkState.Text = "SIGNED IN • GRID ONLINE";
                StartupLog.Write("auth", "Desktop sign-in completed");
                ReturnToLauncher();
                CheckDailyRewards(force: true);
            }
        });
        return web;
    }

    void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState != WindowActivationState.Deactivated) { QueueCatalogCheck(); CheckDailyRewards(); }
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(id);
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ArcadeGridz.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
                StartupLog.Write("window", "ArcadeGridz icon applied");
            }
            // the sign-in card floats as its own small window; everything else is fullscreen (or windowed if chosen)
            if (!floating && !dragging && !presenterApplied) ApplyMainPresenter();
        }
        catch (Exception ex)
        {
            StartupLog.Exception("Fullscreen presenter fallback", ex);
        }
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        StartupLog.Write("window", "Root loaded");
        try
        {
            try
            {
                pads = new GamepadService(DispatcherQueue);
                pads.Pressed += PadPressed;
                StartupLog.Write("gamepad", "Polling initialized");
            }
            catch (Exception ex)
            {
                StartupLog.Exception("Gamepad initialization", ex);
            }
            initializingSettings = true;
            ShowAllToggle.IsOn = settings.ShowAll;
            StartWithWindowsToggle.IsOn = WindowsStartupService.IsEnabled;
            ScanlinesToggle.IsOn = !settings.HideScanlines;
            GpuToggle.IsOn = !settings.DisableGpu;
            DisplayModeCombo.SelectedIndex = settings.Windowed ? 1 : 0;
            QualityCombo.SelectedIndex = 0; // HD isn't available yet
            initializingSettings = false;
            ApplyScanlines();
            clockTimer = DispatcherQueue.CreateTimer();
            clockTimer.Interval = TimeSpan.FromSeconds(1);
            clockTimer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("h:mm tt");
            ClockText.Text = DateTime.Now.ToString("h:mm tt");
            clockTimer.Start();
            await RefreshCatalog();
            catalogTimer = DispatcherQueue.CreateTimer();
            catalogTimer.Interval = TimeSpan.FromMinutes(5);
            catalogTimer.Tick += (_, _) => QueueCatalogCheck();
            catalogTimer.Start();
            var updateCheck = StartupUpdateCheckAsync();
            var hasPersistedSession = await EnsureWebRuntime().HasPersistedSessionAsync();
            var startupUpdate = await updateCheck;
            // keep the logo up for at least ~1.2s so it reads as a splash, not a flicker
            var wait = MinimumSplash - SinceLaunch.Elapsed;
            splashTimer = DispatcherQueue.CreateTimer();
            splashTimer.Interval = wait > TimeSpan.FromMilliseconds(1) ? wait : TimeSpan.FromMilliseconds(1);
            splashTimer.IsRepeating = false;
            splashTimer.Tick += (_, _) =>
            {
                // the user already chose (guest / email sign-in) on the card the failsafe showed: leave it alone
                if (revealed && !startupOpen)
                {
                    if (hasPersistedSession && !signInPanelOpen) NetworkState.Text = "SIGNED IN • GRID ONLINE";
                }
                else
                {
                    void Continue()
                    {
                        if (hasPersistedSession)
                        {
                            NetworkState.Text = "SIGNED IN • GRID ONLINE";
                            if (floating) SetFloating(false);
                            EnterLauncher(false);
                        }
                        else ShowWelcome();
                    }
                    // a newer app is out: offer it before the sign-in screen (it may change sign-in too)
                    if (startupUpdate is not null && !signInPanelOpen) ShowUpdatePrompt(startupUpdate, Continue);
                    else Continue();
                }
                Reveal();
            };
            splashTimer.Start();
            StartupLog.Write("window", hasPersistedSession
                ? "Launcher ready; valid session will bypass welcome"
                : "Launcher ready; splash active");
        }
        catch (Exception ex)
        {
            StartupLog.Exception("MainWindow.OnLoaded", ex);
            Reveal();
            ShowMessage("STARTUP ERROR", $"The launcher could not finish loading. Details were saved to {StartupLog.LogFile}");
        }
    }

    void QueueCatalogCheck(bool force = false)
    {
        if (startupOpen || gameOpen || catalogBusy) return;
        if (!force && DateTime.UtcNow - lastCatalogCheck < CatalogCheckThrottle) return;
        _ = RefreshCatalog(true);
    }

    // quiet = background check: no rebuild when nothing changed (304), keeps the player's category and
    // selected game, and never replaces the screen with an error panel.
    async Task<CatalogRefresh> RefreshCatalog(bool quiet = false)
    {
        if (catalogBusy) return CatalogRefresh.Same;
        catalogBusy = true;
        try
        {
            var result = await catalogs.LoadAsync();
            lastCatalogCheck = DateTime.UtcNow;
            if (quiet && !result.Changed)
            {
                if (!result.Offline && NetworkState.Text.StartsWith("OFFLINE", StringComparison.OrdinalIgnoreCase))
                    NetworkState.Text = "GRID ONLINE • " + result.Catalog.Version;
                return result.Offline ? CatalogRefresh.Offline : CatalogRefresh.Same;
            }
            var keepCategory = categories.Count > 0 ? categories[Math.Clamp(categoryIndex, 0, categories.Count - 1)] : "ALL GAMES";
            var keepGame = (GameGrid.SelectedItem as GameEntry)?.Id;
            all.Clear();
            // Keep the complete live catalog. The options toggle controls visibility;
            // Playable controls launch authorization and is checked again at launch.
            all.AddRange(result.Catalog.Games);
            if (!quiet) NetworkState.Text = result.Offline ? "OFFLINE CACHE" : "GRID ONLINE • " + result.Catalog.Version;
            StartupLog.Write("catalog", $"Loaded version={result.Catalog.Version}; games={result.Catalog.Games.Count}; offline={result.Offline}; quiet={quiet}");
            BuildCategories();
            var ci = categories.IndexOf(keepCategory);
            categoryIndex = ci >= 0 ? ci : 0;
            ApplyFilter();
            if (keepGame is not null)
            {
                var gi = visible.ToList().FindIndex(g => g.Id == keepGame);
                if (gi > 0) GameGrid.SelectedIndex = gi;
            }
            return result.Offline ? CatalogRefresh.Offline : CatalogRefresh.Changed;
        }
        catch (Exception ex)
        {
            StartupLog.Exception(quiet ? "Background catalog check" : "Catalog load", ex);
            if (!quiet)
            {
                NetworkState.Text = "CATALOG ERROR";
                PanelTitle.Text = "NO GRID SIGNAL";
                PanelBody.Text = ex.Message;
                Panel.Visibility = Visibility.Visible;
            }
            return CatalogRefresh.Failed;
        }
        finally { catalogBusy = false; }
    }

    async void RefreshCatalog_Click(object sender, RoutedEventArgs e)
    {
        RefreshCatalogButton.IsEnabled = false;
        UpdateStatus.Visibility = Visibility.Visible;
        UpdateStatus.Text = "CHECKING THE GRID FOR NEW GAMES...";
        var outcome = await RefreshCatalog(true);
        var ready = all.Count(g => settings.ShowAll || (g.Potato && g.Playable));
        UpdateStatus.Text = outcome switch
        {
            CatalogRefresh.Changed => $"GAME LIST UPDATED • {ready} GAMES READY.",
            CatalogRefresh.Same => $"GAME LIST IS UP TO DATE • {ready} GAMES READY.",
            CatalogRefresh.Offline => "COULDN'T REACH ARCADEGRIDZ. SHOWING THE SAVED GAME LIST.",
            _ => "GAME LIST REFRESH FAILED. SEE THE STARTUP LOG FOR DETAILS."
        };
        RefreshCatalogButton.IsEnabled = true;
        PadFocus(RefreshCatalogButton);
    }

    void BuildCategories()
    {
        categories.Clear();
        categories.Add("ALL GAMES");
        categories.AddRange(all.Where(g => (settings.ShowAll || (g.Potato && g.Playable)) && !IsStoryboard(g)).Select(g => g.Category.ToUpperInvariant()).Where(x => x.Length > 0).Distinct().Order());
        categoryIndex = Math.Min(categoryIndex, categories.Count - 1);
    }

    void ApplyFilter()
    {
        var cat = storyMode ? "STORYBOARDS" : categories.Count == 0 ? "ALL GAMES" : categories[categoryIndex];
        CategoryLabel.Text = cat;
        visible.Clear();
        foreach (var g in all.Where(g => (settings.ShowAll || (g.Potato && g.Playable)) &&
                 (storyMode ? IsStoryboard(g) : !IsStoryboard(g) && (cat == "ALL GAMES" || g.Category.Equals(cat, StringComparison.OrdinalIgnoreCase)))))
            visible.Add(g);
        if (storyMode) StoryboardsButton.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgMagenta"];
        else StoryboardsButton.ClearValue(Control.BorderBrushProperty);
        if (visible.Count > 0) GameGrid.ScrollIntoView(visible[0]);
        if (visible.Count > 0) { GameGrid.SelectedIndex = 0; _ = ShowHero(visible[0]); }
        else
        {
            GameGrid.SelectedIndex = -1;
            HeroName.Text = "NO GAMES";
            HeroMeta.Text = "";
            HeroStatusChip.Visibility = Visibility.Collapsed;
            ScoreEmpty.Visibility = Visibility.Collapsed;
            HeroDescription.Text = "No catalog entries match this filter.";
            HeroArt.Source = null;
            PlayButton.IsEnabled = false;
            PlayLabel.Text = "UNAVAILABLE";
            ScoreList.ItemsSource = null;
        }
    }

    async Task ShowHero(GameEntry g)
    {
        heroLoad?.Cancel();
        heroLoad?.Dispose();
        heroLoad = new CancellationTokenSource();
        var cancellationToken = heroLoad.Token;

        // Update the selection immediately. Network score loading is debounced so
        // holding a direction never starts one request per repeated selection.
        HeroName.Text = g.Name;
        HeroMeta.Text = $"{g.Genre.ToUpperInvariant()}  ·  {g.Runtime.Type.ToUpperInvariant()}";
        HeroStatus.Text = g.Playable ? g.Status.ToUpperInvariant() : "LOCKED";
        var statusBrush = g.Playable ? UiTheme.StatusBrush(g.Status) : UiTheme.TagBrush("CHEATER");
        HeroStatus.Foreground = statusBrush;
        HeroStatusChip.BorderBrush = statusBrush;
        HeroStatusChip.Visibility = Visibility.Visible;
        HeroDescription.Text = g.Description;
        PlayButton.IsEnabled = g.Playable;
        PlayLabel.Text = g.Playable ? "PLAY" : "UNAVAILABLE";
        ScoreList.ItemsSource = null;
        ScoreEmpty.Visibility = Visibility.Collapsed;
        try { HeroArt.Source = new BitmapImage(new Uri(g.Art)); } catch { HeroArt.Source = null; }

        try
        {
            await Task.Delay(120, cancellationToken);
            var scores = await leaderboards.TopAsync(g.Id);
            cancellationToken.ThrowIfCancellationRequested();
            ScoreList.ItemsSource = scores.Select((s, i) => new ScoreRow($"#{i + 1}", s.Name, s.Score.ToString("N0"), s.Tag)).ToList();
            ScoreEmpty.Visibility = scores.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
    }

    void GameGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (GameGrid.SelectedItem is GameEntry g) _ = ShowHero(g); }
    async void GameGrid_ItemClick(object sender, ItemClickEventArgs e) { if (e.ClickedItem is GameEntry g) await Launch(g); }
    async void Play_Click(object sender, RoutedEventArgs e) { if (GameGrid.SelectedItem is GameEntry g) await Launch(g); }

    async Task Launch(GameEntry g)
    {
        try
        {
            if (!g.Playable)
            {
                StartupLog.Write("runtime", $"Blocked non-playable catalog entry id={g.Id}");
                ShowMessage("NOT PLAYABLE", $"{g.Name} is listed in the catalog but is not available in the Windows launcher.");
                return;
            }
            if (g.Runtime.Type.Equals("web", StringComparison.OrdinalIgnoreCase))
            {
                var webRuntime = EnsureWebRuntime();
                active = webRuntime.CanRun(g) ? webRuntime : null;
            }
            else active = native.CanRun(g) ? native : null;

            if (active is null) { ShowMessage("RUNTIME ERROR", "This game's runtime manifest is incomplete."); return; }
            StartupLog.Write("runtime", $"Launching id={g.Id}; type={g.Runtime.Type}");
            gameOpen = true;
            GameCoverTitle.Text = g.Name;
            try { GameCoverArt.Source = new BitmapImage(new Uri(g.Art)); } catch { GameCoverArt.Source = null; }
            GameCover.Visibility = Visibility.Visible;
            BeginStoryboard("CoverPulse");
            GameLayer.Visibility = active == web ? Visibility.Visible : Visibility.Collapsed;
            Launcher.Visibility = Visibility.Collapsed;
            Panel.Visibility = Visibility.Collapsed;
            StartLaunchWatchdog(g);
            await active.LaunchAsync(g);
        }
        catch (Exception ex)
        {
            StartupLog.Exception($"Launch {g.Id}", ex);
            ShowMessage("LAUNCH FAILED", $"{ex.Message}\n\nDetails: {StartupLog.LogFile}");
            ReturnToLauncher();
        }
    }

    async Task ExitGame()
    {
        if (!gameOpen) return;
        var runtime = active;
        active = null;
        gameOpen = false;
        if (runtime is not null) await runtime.ExitAsync();
        ReturnToLauncher();
    }

    void ReturnToLauncher()
    {
        gameOpen = false;
        active = null;
        launchWatchdog?.Stop();
        GameLayer.Visibility = Visibility.Collapsed;
        SetSignInPanel(false);
        StopStoryboard("CoverPulse");
        GameCover.Visibility = Visibility.Visible;
        Panel.Visibility = Visibility.Collapsed;
        if (startupOpen && WelcomePanel.Visibility == Visibility.Visible)
        {
            // back on the floating sign-in card (e.g. cancelled email sign-in)
            Launcher.Visibility = Visibility.Collapsed;
            FloatWelcome();
            SetStartupFocus(startupFocus);
        }
        else
        {
            if (floating) SetFloating(false);
            Launcher.Visibility = Visibility.Visible;
            SetLauncherFocus(LauncherFocus.GameGrid);
        }
        QueueCatalogCheck();
        if (!TryShowDaily()) CheckDailyRewards();
    }

    async void PadPressed(PadAction action)
    {
        // GamepadService is driven by a DispatcherQueueTimer, so this already runs
        // on the UI thread. Handling it directly avoids a second queued UI hop.
        if (DailyLayer.Visibility == Visibility.Visible)
        {
            if (action == PadAction.A) DailyPrimary();
            else if (action == PadAction.B) CloseDaily();
            return;
        }
        if (updatePromptOpen) { HandleUpdatePromptAction(action); return; }
        if (startupOpen && WelcomePanel.Visibility == Visibility.Visible)
        {
            HandleStartupAction(action);
            return;
        }
        if (signInPanelOpen && action is PadAction.B or PadAction.ExitCombo) { await CancelSignIn(); return; }
        if (action == PadAction.ExitCombo && gameOpen)
        {
            // web games show the site's SYSTEM MENU (resume / reload / exit ...); EXIT GAME there tells us via "exited"
            if (web is not null && ReferenceEquals(active, web)) web.OpenSystemMenu(); else await ExitGame();
            return;
        }
        if (gameOpen) return;
        if (Panel.Visibility == Visibility.Visible) { await HandlePanelAction(action); return; }
        if (action == PadAction.LB) ChangeCategory(-1);
        else if (action == PadAction.RB) ChangeCategory(1);
        else if (action == PadAction.B) ReturnToLauncher();
        else await HandleLauncherAction(action);
    }

    void ShowWelcome()
    {
        // centered sign-in card over the (dimmed, inert) launcher, like Steam / Battle.net
        if (startupOpen && WelcomePanel.Visibility == Visibility.Visible) return;
        startupOpen = true;
        Launcher.Visibility = Visibility.Collapsed;
        StartupLayer.Visibility = Visibility.Visible;
        WelcomePanel.Visibility = Visibility.Visible;
        FloatWelcome();
        BeginStoryboard("InsertCoinBlink");
        SetStartupFocus(StartupFocus.Email);
        StartupLog.Write("window", "Welcome screen ready");
    }

    void EnterLauncher(bool guest)
    {
        startupOpen = false;
        StopStoryboard("InsertCoinBlink");
        StartupLayer.Visibility = Visibility.Collapsed;
        WelcomePanel.Visibility = Visibility.Collapsed;
        if (floating) SetFloating(false);
        FitLauncher();
        Launcher.Visibility = Visibility.Visible;
        if (guest)
            NetworkState.Text = NetworkState.Text.StartsWith("OFFLINE", StringComparison.OrdinalIgnoreCase)
                ? "GUEST • OFFLINE CACHE"
                : "GUEST • " + NetworkState.Text;
        SetLauncherFocus(LauncherFocus.GameGrid);
        StartupLog.Write("window", guest ? "Entered launcher as guest" : "Entered launcher signed in");
        TryShowDaily();
    }

    void HandleStartupAction(PadAction action)
    {
        if (action == PadAction.B) { CloseWelcome(); return; }
        if (action is PadAction.Up or PadAction.Left)
            SetStartupFocus((StartupFocus)Math.Max(0, (int)startupFocus - 1));
        else if (action is PadAction.Down or PadAction.Right)
            SetStartupFocus((StartupFocus)Math.Min((int)StartupFocus.Privacy, (int)startupFocus + 1));
        else if (action == PadAction.A)
        {
            if (startupFocus == StartupFocus.Email) StartupEmail_Click(this, new RoutedEventArgs());
            else if (startupFocus == StartupFocus.Google) StartupGoogle_Click(this, new RoutedEventArgs());
            else if (startupFocus == StartupFocus.Terms) OpenExternal(TermsUrl);
            else if (startupFocus == StartupFocus.Privacy) OpenExternal(PrivacyUrl);
            else StartupGuest_Click(this, new RoutedEventArgs());
        }
    }

    void SetStartupFocus(StartupFocus target)
    {
        startupFocus = target;
        Control control = target switch
        {
            StartupFocus.Google => StartupGoogleButton,
            StartupFocus.Guest => StartupGuestButton,
            StartupFocus.Terms => TermsButton,
            StartupFocus.Privacy => PrivacyButton,
            _ => StartupEmailButton
        };
        PadFocus(control);
    }

    async void StartupEmail_Click(object sender, RoutedEventArgs e)
    {
        startupOpen = false;
        StartupLayer.Visibility = Visibility.Collapsed;
        await BeginSignInAsync(fromWelcome: true);
    }

    async void StartupGoogle_Click(object sender, RoutedEventArgs e)
    {
        StartupGoogleButton.IsEnabled = false;
        try
        {
            var result = await desktopAuth.SignInWithGoogleAsync(message =>
                DispatcherQueue.TryEnqueue(() => StartupStatus.Text = message));
            StartupStatus.Text = $"WELCOME, {result.AccountName.ToUpperInvariant()}. FINISHING SIGN-IN...";
            startupOpen = false;
            StartupLayer.Visibility = Visibility.Collapsed;
            var webRuntime = EnsureWebRuntime();
            await webRuntime.ImportDesktopSessionAsync(result.Token);
        }
        catch (OperationCanceledException)
        {
            StartupStatus.Text = "GOOGLE SIGN-IN TIMED OUT. TRY AGAIN OR CONTINUE AS GUEST.";
            StartupLog.Write("auth", "System-browser sign-in timed out");
        }
        catch (Exception ex)
        {
            StartupStatus.Text = "GOOGLE SIGN-IN FAILED: " + ex.Message.ToUpperInvariant();
            StartupLog.Exception("System-browser Google sign-in", ex);
            startupOpen = true;
            StartupLayer.Visibility = Visibility.Visible;
            WelcomePanel.Visibility = Visibility.Visible;
        }
        finally
        {
            StartupGoogleButton.IsEnabled = true;
            if (startupOpen) SetStartupFocus(StartupFocus.Google);
        }
    }

    void StartupGuest_Click(object sender, RoutedEventArgs e) => EnterLauncher(true);

    async Task HandleLauncherAction(PadAction action)
    {
        if (action == PadAction.A)
        {
            switch (launcherFocus)
            {
                case LauncherFocus.Categories: ChangeCategory(1); break;
                case LauncherFocus.Games: ShowSection(false); break;
                case LauncherFocus.Storyboards: ShowSection(true); break;
                case LauncherFocus.Scores: Scores_Click(this, new RoutedEventArgs()); break;
                case LauncherFocus.Options: Options_Click(this, new RoutedEventArgs()); break;
                case LauncherFocus.Exit: Close(); break;
                case LauncherFocus.SignIn:
                    if (account is not null) Account_Click(this, new RoutedEventArgs());
                    else SignIn_Click(this, new RoutedEventArgs());
                    break;
                case LauncherFocus.GameGrid:
                case LauncherFocus.Play:
                    if (GameGrid.SelectedItem is GameEntry game) await Launch(game);
                    break;
            }
            return;
        }

        if (launcherFocus == LauncherFocus.GameGrid &&
            action is PadAction.Left or PadAction.Right or PadAction.Up or PadAction.Down)
        {
            MoveGameSelection(action);
            return;
        }

        if (launcherFocus == LauncherFocus.Play)
        {
            if (action == PadAction.Up || action == PadAction.Left) SetLauncherFocus(LauncherFocus.GameGrid);
            else if (action == PadAction.Down) SetLauncherFocus(consoleMode ? LauncherFocus.Options : LauncherFocus.Exit);
            return;
        }

        if (launcherFocus == LauncherFocus.Categories)
        {
            if (action == PadAction.Left) ChangeCategory(-1);
            else if (action == PadAction.Right) ChangeCategory(1);
            else if (action == PadAction.Down) SetLauncherFocus(LauncherFocus.Games);
            else if (action == PadAction.Up) SetLauncherFocus(LauncherFocus.SignIn);
            return;
        }

        if (launcherFocus == LauncherFocus.SignIn)
        {
            if (action == PadAction.Down) SetLauncherFocus(consoleMode ? LauncherFocus.Options : LauncherFocus.Exit);
            else if (action == PadAction.Left) SetLauncherFocus(LauncherFocus.Categories);
            return;
        }

        var menu = consoleMode
            ? new[] { LauncherFocus.Games, LauncherFocus.Scores, LauncherFocus.Storyboards, LauncherFocus.Options }
            : new[] { LauncherFocus.Games, LauncherFocus.Scores, LauncherFocus.Storyboards, LauncherFocus.Options, LauncherFocus.Exit };
        var menuIndex = Array.IndexOf(menu, launcherFocus);
        if (menuIndex >= 0)
        {
            if (action == PadAction.Left) SetLauncherFocus(menu[Math.Max(0, menuIndex - 1)]);
            else if (action == PadAction.Right) SetLauncherFocus(menu[Math.Min(menu.Length - 1, menuIndex + 1)]);
            else if (action == PadAction.Up) SetLauncherFocus(launcherFocus == LauncherFocus.Exit ? LauncherFocus.SignIn : LauncherFocus.Categories);
            else if (action == PadAction.Down) SetLauncherFocus(launcherFocus == LauncherFocus.Exit ? LauncherFocus.Play : LauncherFocus.GameGrid);
        }
    }

    int GridColumns()
    {
        // Tiles use an ItemsWrapGrid with a fixed 224px item width (max 4 columns).
        var usable = GameGrid.ActualWidth - GameGrid.Padding.Left - GameGrid.Padding.Right;
        return Math.Clamp((int)(usable / 224), 1, 4);
    }

    void MoveGameSelection(PadAction action)
    {
        if (visible.Count == 0)
        {
            if (action == PadAction.Up) SetLauncherFocus(LauncherFocus.Games);
            else if (action is PadAction.Down or PadAction.Right) SetLauncherFocus(LauncherFocus.Play);
            return;
        }
        var cols = GridColumns();
        var i = Math.Max(0, GameGrid.SelectedIndex);
        var col = i % cols;
        switch (action)
        {
            case PadAction.Up:
                if (i - cols < 0) { SetLauncherFocus(LauncherFocus.Games); return; }
                i -= cols; break;
            case PadAction.Down:
                if (i / cols == (visible.Count - 1) / cols) { SetLauncherFocus(LauncherFocus.Play); return; }
                i = Math.Min(i + cols, visible.Count - 1); break;
            case PadAction.Left:
                if (col == 0) return;
                i -= 1; break;
            case PadAction.Right:
                if (col == cols - 1 || i == visible.Count - 1) { SetLauncherFocus(LauncherFocus.Play); return; }
                i += 1; break;
        }
        GameGrid.SelectedIndex = Math.Clamp(i, 0, visible.Count - 1);
        GameGrid.ScrollIntoView(GameGrid.SelectedItem);
    }

    void SetLauncherFocus(LauncherFocus target)
    {
        if (consoleMode && target == LauncherFocus.Exit)
            target = LauncherFocus.Options;
        if (launcherFocus != target) StartupLog.Write("nav", $"launcher focus {launcherFocus} -> {target}");
        launcherFocus = target;
        UpdateTileHighlight();
        Control control = target switch
        {
            LauncherFocus.Categories => NextCategoryButton,
            LauncherFocus.Games => GamesButton,
            LauncherFocus.Scores => ScoresButton,
            LauncherFocus.Storyboards => StoryboardsButton,
            LauncherFocus.Options => OptionsButton,
            LauncherFocus.Exit => ExitButton,
            LauncherFocus.Play => PlayButton,
            LauncherFocus.SignIn => account is not null ? AccountButton : SignInButton,
            _ => GameGrid
        };
        PadFocus(control);
    }

    Button[] OptionsTabButtons => [OptionsTabGames, OptionsTabDisplay, OptionsTabSystem, OptionsTabUpdates, OptionsTabAbout];

    List<Control> PanelControls()
    {
        var controls = new List<Control>();
        if (OptionControls.Visibility == Visibility.Visible)
        {
            controls.Add(OptionsTabButtons[optionsTab]); // the tab row is one stop; left/right changes tab
            switch (optionsTab)
            {
                case 0:
                    controls.Add(ShowAllToggle);
                    controls.Add(RefreshCatalogButton);
                    break;
                case 1:
                    controls.Add(ScreenCombo);
                    controls.Add(DisplayModeCombo);
                    controls.Add(QualityCombo);
                    controls.Add(ScanlinesToggle);
                    controls.Add(GpuToggle);
                    break;
                case 2:
                    controls.Add(StartWithWindowsToggle);
                    if (ConsoleModeButton.IsEnabled) controls.Add(ConsoleModeButton);
                    break;
                case 3:
                    if (CheckUpdateButton.IsEnabled) controls.Add(CheckUpdateButton);
                    if (InstallUpdateButton.IsEnabled) controls.Add(InstallUpdateButton);
                    break;
                case 4:
                    controls.Add(OptionsTermsButton);
                    controls.Add(OptionsPrivacyButton);
                    break;
            }
        }
        else if (AccountControls.Visibility == Visibility.Visible)
        {
            if (DailyButton.IsEnabled) controls.Add(DailyButton);
            if (SignOutButton.IsEnabled) controls.Add(SignOutButton);
        }
        else if (UpdateActions.Visibility == Visibility.Visible)
        {
            controls.Add(CheckUpdateButton);
            if (InstallUpdateButton.IsEnabled) controls.Add(InstallUpdateButton);
        }
        controls.Add(BackButton);
        return controls;
    }

    async Task HandlePanelAction(PadAction action)
    {
        if (action == PadAction.B) { ReturnToLauncher(); return; }
        var options = OptionControls.Visibility == Visibility.Visible;
        var controls = PanelControls();
        var current = controls[Math.Clamp(panelFocus, 0, controls.Count - 1)];
        var onTabs = options && current is Button b && OptionsTabButtons.Contains(b);
        if (options && (action is PadAction.LB or PadAction.RB || (onTabs && action is PadAction.Left or PadAction.Right)))
        {
            var delta = action is PadAction.LB or PadAction.Left ? -1 : 1;
            SelectOptionsTab((optionsTab + delta + OptionsTabCount) % OptionsTabCount, keepFocusOnTabs: onTabs || action is PadAction.Left or PadAction.Right);
            return;
        }
        // dropdowns: left/right flips through the choices (or up/down while the list is open); A opens / closes the list
        if (current is ComboBox combo)
        {
            if (action is PadAction.Left or PadAction.Right || (combo.IsDropDownOpen && action is PadAction.Up or PadAction.Down))
            {
                StepCombo(combo, action is PadAction.Right or PadAction.Down ? 1 : -1);
                return;
            }
            if (action == PadAction.A) { combo.IsDropDownOpen = !combo.IsDropDownOpen; return; }
        }
        if (action is PadAction.Up || (!options && action is PadAction.Left)) panelFocus = Math.Max(0, panelFocus - 1);
        else if (action is PadAction.Down || (!options && action is PadAction.Right)) panelFocus = Math.Min(controls.Count - 1, panelFocus + 1);
        else if (action == PadAction.A)
        {
            var control = current;
            if (control == BackButton) { ReturnToLauncher(); return; }
            if (onTabs) { panelFocus = Math.Min(1, controls.Count - 1); }
            else if (control == ShowAllToggle) ShowAllToggle.IsOn = !ShowAllToggle.IsOn;
            else if (control == StartWithWindowsToggle) StartWithWindowsToggle.IsOn = !StartWithWindowsToggle.IsOn;
            else if (control == ScanlinesToggle) ScanlinesToggle.IsOn = !ScanlinesToggle.IsOn;
            else if (control == GpuToggle) GpuToggle.IsOn = !GpuToggle.IsOn;
            else if (control == DailyButton) { Daily_Click(this, new RoutedEventArgs()); return; }
            else if (control == RefreshCatalogButton) { if (RefreshCatalogButton.IsEnabled) RefreshCatalog_Click(this, new RoutedEventArgs()); return; }
            else if (control == ConsoleModeButton) ConsoleMode_Click(this, new RoutedEventArgs());
            else if (control == CheckUpdateButton) CheckUpdate_Click(this, new RoutedEventArgs());
            else if (control == InstallUpdateButton) InstallUpdate_Click(this, new RoutedEventArgs());
            else if (control == OptionsTermsButton) OpenExternal(TermsUrl);
            else if (control == OptionsPrivacyButton) OpenExternal(PrivacyUrl);
            else if (control == SignOutButton) { SignOut_Click(this, new RoutedEventArgs()); return; }
            controls = PanelControls();
            panelFocus = Math.Min(panelFocus, controls.Count - 1);
        }
        FocusPanelControl(controls[Math.Clamp(panelFocus, 0, controls.Count - 1)]);
        await Task.CompletedTask;
    }

    void FocusPanelControl(Control control)
    {
        control.Focus(FocusState.Keyboard);
        control.StartBringIntoView();
        // ToggleSwitch has no visible focus state in this theme: light up its row instead
        var cyan = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgCyan"];
        var line = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgLine"];
        ShowAllRow.BorderBrush = control == ShowAllToggle ? cyan : line;
        StartWithWindowsRow.BorderBrush = control == StartWithWindowsToggle ? cyan : line;
        ScreenRow.BorderBrush = control == ScreenCombo ? cyan : line;
        DisplayModeRow.BorderBrush = control == DisplayModeCombo ? cyan : line;
        QualityRow.BorderBrush = control == QualityCombo ? cyan : line;
        ScanlinesRow.BorderBrush = control == ScanlinesToggle ? cyan : line;
        GpuRow.BorderBrush = control == GpuToggle ? cyan : line;
    }

    void SelectOptionsTab(int tab, bool keepFocusOnTabs = true)
    {
        optionsTab = Math.Clamp(tab, 0, OptionsTabCount - 1);
        if (optionsTab == 1) RefreshScreens();
        OptionsGamesTab.Visibility = optionsTab == 0 ? Visibility.Visible : Visibility.Collapsed;
        OptionsDisplayTab.Visibility = optionsTab == 1 ? Visibility.Visible : Visibility.Collapsed;
        OptionsSystemTab.Visibility = optionsTab == 2 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCard.Visibility = optionsTab == 3 ? Visibility.Visible : Visibility.Collapsed;
        OptionsAboutTab.Visibility = optionsTab == 4 ? Visibility.Visible : Visibility.Collapsed;
        OptionsLegal.Visibility = optionsTab == 4 ? Visibility.Visible : Visibility.Collapsed;
        var pink = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgMagenta"];
        var selectedFill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x44, 0xFF, 0x3A, 0xA7));
        var buttons = OptionsTabButtons;
        for (int i = 0; i < buttons.Length; i++)
        {
            if (i == optionsTab) { buttons[i].Background = selectedFill; buttons[i].BorderBrush = pink; }
            else { buttons[i].ClearValue(Control.BackgroundProperty); buttons[i].ClearValue(Control.BorderBrushProperty); }
        }
        var controls = PanelControls();
        panelFocus = keepFocusOnTabs ? 0 : Math.Min(1, controls.Count - 1);
        FocusPanelControl(controls[panelFocus]);
    }

    void OptionsTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var tab)) SelectOptionsTab(tab);
    }

    void ChangeCategory(int delta)
    {
        if (categories.Count == 0) return;
        if (storyMode) { storyMode = false; ApplyFilter(); return; } // LB/RB from STORYBOARDS goes back to the games
        categoryIndex = (categoryIndex + delta + categories.Count) % categories.Count;
        ApplyFilter();
    }

    void PreviousCategory_Click(object sender, RoutedEventArgs e) => ChangeCategory(-1);
    void NextCategory_Click(object sender, RoutedEventArgs e) => ChangeCategory(1);

    void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (consoleMode) { e.Handled = true; return; }
        if (updatePromptOpen)
        {
            // Enter/Space activate the focused button natively; Esc = not now, arrows move between the two
            if (e.Key == VirtualKey.Escape) { UpdateLater_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (e.Key is VirtualKey.Up or VirtualKey.Left) { UpdateNowButton.Focus(FocusState.Keyboard); e.Handled = true; }
            else if (e.Key is VirtualKey.Down or VirtualKey.Right) { UpdateLaterButton.Focus(FocusState.Keyboard); e.Handled = true; }
            return;
        }
        if (DailyLayer.Visibility == Visibility.Visible)
        {
            if (e.Key is VirtualKey.Enter or VirtualKey.Space) { DailyPrimary(); e.Handled = true; }
            else if (e.Key == VirtualKey.Escape) { CloseDaily(); e.Handled = true; }
            return;
        }
        // Ctrl+Shift+Left/Right: hop the arcade to the next screen (handy for the TV)
        if (!gameOpen && e.Key is VirtualKey.Left or VirtualKey.Right &&
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) &&
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            MoveToNextScreen(e.Key == VirtualKey.Right ? 1 : -1);
            e.Handled = true;
            return;
        }
        // While a game runs every key belongs to the game: Escape/back opens the game's own pause menu there.
        // Games are left only through their EXIT GAME / QUIT menu items (or the SELECT+START combo).
        if (gameOpen) return;
        if (e.Key == VirtualKey.Escape)
        {
            if (startupOpen && WelcomePanel.Visibility == Visibility.Visible) CloseWelcome();
            else ReturnToLauncher();
            e.Handled = true;
        }
        else if (!gameOpen)
        {
            var action = e.Key switch
            {
                VirtualKey.Up => PadAction.Up,
                VirtualKey.Down => PadAction.Down,
                VirtualKey.Left => PadAction.Left,
                VirtualKey.Right => PadAction.Right,
                VirtualKey.Enter or VirtualKey.Space => PadAction.A,
                VirtualKey.PageUp or VirtualKey.Q => PadAction.LB,
                VirtualKey.PageDown or VirtualKey.E => PadAction.RB,
                _ => (PadAction?)null
            };
            if (action is not null)
            {
                e.Handled = true;
                if (startupOpen && WelcomePanel.Visibility == Visibility.Visible)
                    HandleStartupAction(action.Value);
                else
                    _ = Panel.Visibility == Visibility.Visible
                        ? HandlePanelAction(action.Value)
                        : HandleLauncherAction(action.Value);
            }
        }
    }

    async void SignIn_Click(object sender, RoutedEventArgs e) => await BeginSignInAsync();

    async Task BeginSignInAsync(bool fromWelcome = false)
    {
        try
        {
            var webRuntime = EnsureWebRuntime();
            active = webRuntime;
            gameOpen = true;
            signInFromWelcome = fromWelcome;
            GameCover.Visibility = Visibility.Collapsed;
            // the Network sign-in page shows in a centered card; the launcher stays visible (dimmed) behind it
            SetSignInPanel(true);
            GameLayer.Visibility = Visibility.Visible;
            Launcher.Visibility = floating ? Visibility.Collapsed : Visibility.Visible;
            Panel.Visibility = Visibility.Collapsed;
            await webRuntime.NavigateAsync("https://arcadegridz.com/?host=winapp&mode=signin");
        }
        catch (Exception ex)
        {
            StartupLog.Exception("Sign in", ex);
            startupOpen = false;
            ShowMessage("SIGN-IN FAILED", $"{ex.Message}\n\nDetails: {StartupLog.LogFile}");
            ReturnToLauncher();
        }
    }

    void Scores_Click(object sender, RoutedEventArgs e) => ShowMessage("NETWORK SCORES", "Choose a game to see its live top five. A full cross-game score browser is planned after v0.1.");
    void Options_Click(object sender, RoutedEventArgs e)
    {
        PanelTitle.Text = "OPTIONS";
        PanelTitle.FontSize = 36;
        PanelBody.Text = "LB / RB or click a tab to switch sections.";
        PanelBody.Visibility = Visibility.Visible;
        OptionControls.Visibility = Visibility.Visible;
        AccountControls.Visibility = Visibility.Collapsed;
        initializingSettings = true;
        StartWithWindowsToggle.IsOn = WindowsStartupService.IsEnabled;
        initializingSettings = false;
        VersionText.Text = $"INSTALLED VERSION  {UpdateService.InstalledVersion}";
        AboutVersionText.Text = $"ARCADEGRIDZ FOR WINDOWS  ·  V{UpdateService.InstalledVersion}";
        VersionText.Visibility = Visibility.Visible;
        UpdateStatus.Text = "Updates are optional. ArcadeGridz never installs one without confirmation.";
        UpdateStatus.Visibility = Visibility.Visible;
        UpdateActions.Visibility = Visibility.Visible;
        UpdateProgress.Visibility = Visibility.Collapsed;
        InstallUpdateButton.IsEnabled = availableUpdate is not null;
        Panel.Visibility = Visibility.Visible;
        Launcher.Visibility = Visibility.Collapsed;
        SelectOptionsTab(optionsTab, keepFocusOnTabs: true);
    }
    void Games_Click(object sender, RoutedEventArgs e) => ShowSection(false);

    void Storyboards_Click(object sender, RoutedEventArgs e) => ShowSection(true);

    void ShowSection(bool storyboards)
    {
        var changed = storyMode != storyboards;
        storyMode = storyboards;
        ReturnToLauncher();
        if (changed) ApplyFilter();
        SetLauncherFocus(LauncherFocus.GameGrid);
    }
    void Back_Click(object sender, RoutedEventArgs e) => ReturnToLauncher();
    void Close_Click(object sender, RoutedEventArgs e) => Close();
    void ShowMessage(string title, string body)
    {
        PanelTitle.Text = title;
        PanelTitle.FontSize = 46;
        PanelBody.Text = body;
        PanelBody.Visibility = Visibility.Visible;
        OptionControls.Visibility = Visibility.Collapsed;
        AccountControls.Visibility = Visibility.Collapsed;
        VersionText.Visibility = Visibility.Collapsed;
        UpdateStatus.Visibility = Visibility.Collapsed;
        UpdateActions.Visibility = Visibility.Collapsed;
        UpdateCard.Visibility = Visibility.Collapsed;
        OptionsLegal.Visibility = Visibility.Collapsed;
        UpdateProgress.Visibility = Visibility.Collapsed;
        Panel.Visibility = Visibility.Visible;
        Launcher.Visibility = Visibility.Collapsed;
        panelFocus = 0;
        PadFocus(BackButton);
    }

    async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        UpdateProgress.IsIndeterminate = true;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateStatus.Text = "CHECKING OFFICIAL ARCADEGRIDZ RELEASES...";
        try
        {
            var result = await updates.CheckAsync();
            availableUpdate = result.Update;
            UpdateStatus.Text = result.Status;
            InstallUpdateButton.IsEnabled = availableUpdate is not null;
            StartupLog.Write("update", $"Check completed; installed={result.InstalledVersion}; available={result.Update?.Version}");
        }
        catch (Exception ex)
        {
            availableUpdate = null;
            UpdateStatus.Text = "UPDATE CHECK FAILED: " + ex.Message;
            StartupLog.Exception("Update check", ex);
        }
        finally
        {
            UpdateProgress.IsIndeterminate = false;
            UpdateProgress.Visibility = Visibility.Collapsed;
            CheckUpdateButton.IsEnabled = true;
        }
    }

    async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (availableUpdate is null) return;
        CheckUpdateButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        UpdateProgress.IsIndeterminate = false;
        UpdateProgress.Value = 0;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateStatus.Text = $"DOWNLOADING ARCADEGRIDZ {availableUpdate.Version}...";
        try
        {
            var progress = new Progress<double>(value =>
            {
                UpdateProgress.Value = value * 100;
                UpdateStatus.Text = $"DOWNLOADING ARCADEGRIDZ {availableUpdate.Version}  {value:P0}";
            });
            var installer = await updates.DownloadAsync(availableUpdate, progress);
            UpdateStatus.Text = "DOWNLOAD VERIFIED. STARTING INSTALLER...";
            StartupLog.Write("update", $"Verified installer for {availableUpdate.Version}: {installer}");
            UpdateService.InstallAndRestart(installer);
            Close();
        }
        catch (Exception ex)
        {
            UpdateStatus.Text = "UPDATE FAILED: " + ex.Message;
            StartupLog.Exception("Update install", ex);
            CheckUpdateButton.IsEnabled = true;
            InstallUpdateButton.IsEnabled = true;
            UpdateProgress.Visibility = Visibility.Collapsed;
        }
    }
    void ShowAllToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (initializingSettings) return;
        settings.ShowAll = ShowAllToggle.IsOn;
        settings.Save();
        BuildCategories();
        ApplyFilter();
    }

    void StartWithWindowsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (initializingSettings) return;
        if (WindowsStartupService.SetEnabled(StartWithWindowsToggle.IsOn))
        {
            UpdateStatus.Text = StartWithWindowsToggle.IsOn
                ? "ARCADEGRIDZ WILL START WHEN YOU SIGN IN TO WINDOWS."
                : "WINDOWS STARTUP DISABLED.";
        }
        else
        {
            initializingSettings = true;
            StartWithWindowsToggle.IsOn = WindowsStartupService.IsEnabled;
            initializingSettings = false;
            UpdateStatus.Text = "WINDOWS STARTUP COULD NOT BE CHANGED. SEE THE STARTUP LOG FOR DETAILS.";
        }
    }

    void ConsoleMode_Click(object sender, RoutedEventArgs e)
    {
        if (consoleMode) return;
        consoleMode = true;
        ExitButton.Visibility = Visibility.Collapsed;
        ConsoleModeButton.Content = "CONSOLE MODE ACTIVE • RESTART APP TO RESTORE";
        ConsoleModeButton.IsEnabled = false;
        ConsoleModeStatus.Text = "Controller-only mode is active. Keyboard and WebView pointer input are disabled; Exit ArcadeGridz stays hidden until restart.";
        web?.SetConsoleMode(true);
        StartupLog.Write("settings", "Console mode enabled for this session");
        PadFocus(BackButton);
    }

    void Terms_Click(object sender, RoutedEventArgs e) => OpenExternal(TermsUrl);
    void Privacy_Click(object sender, RoutedEventArgs e) => OpenExternal(PrivacyUrl);

    async void OpenExternal(string url)
    {
        // Legal pages open in the system browser; the launcher stays native.
        try
        {
            var opened = await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
            StartupLog.Write("window", $"Opened external page {url}; ok={opened}");
            if (!opened && startupOpen) StartupStatus.Text = "COULD NOT OPEN YOUR BROWSER. VISIT " + url.ToUpperInvariant();
        }
        catch (Exception ex) { StartupLog.Exception("Open external page", ex); }
    }

    void BeginStoryboard(string key)
    {
        try { if (Root.Resources[key] is Microsoft.UI.Xaml.Media.Animation.Storyboard sb) sb.Begin(); }
        catch (Exception ex) { StartupLog.Exception($"Storyboard {key}", ex); }
    }

    void StopStoryboard(string key)
    {
        try { if (Root.Resources[key] is Microsoft.UI.Xaml.Media.Animation.Storyboard sb) sb.Stop(); }
        catch { }
    }

    void SetSignInPanel(bool open)
    {
        signInPanelOpen = open;
        SignInChrome.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        GameLayer.Background = open
            ? floating
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgCardSolid"]
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xCC, 0x05, 0x03, 0x12))
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black);
        if (open)
        {
            var h = Root.ActualHeight > 0 ? Root.ActualHeight : 900;
            // card = 84px header + page + 20px; the page is offset 64px so it sits under the header
            var pageHeight = floating ? 640 : Math.Clamp(h - 220, 420, 700);
            if (floating) SetFloating(true, 560, pageHeight + 104);
            SignInCard.Height = pageHeight + 104;
            WebViewFrame.Width = 520;
            WebViewFrame.Height = pageHeight;
            WebViewFrame.HorizontalAlignment = HorizontalAlignment.Center;
            WebViewFrame.VerticalAlignment = VerticalAlignment.Center;
            WebViewFrame.Margin = new Thickness(0, 64, 0, 0);
        }
        else
        {
            WebViewFrame.Width = double.NaN;
            WebViewFrame.Height = double.NaN;
            WebViewFrame.HorizontalAlignment = HorizontalAlignment.Stretch;
            WebViewFrame.VerticalAlignment = VerticalAlignment.Stretch;
            WebViewFrame.Margin = new Thickness(0);
        }
    }

    async Task CancelSignIn()
    {
        if (!signInPanelOpen) return;
        StartupLog.Write("auth", "Sign-in panel cancelled");
        var backToWelcome = signInFromWelcome;
        var runtime = active;
        active = null;
        gameOpen = false;
        if (backToWelcome) { startupOpen = true; StartupLayer.Visibility = Visibility.Visible; WelcomePanel.Visibility = Visibility.Visible; }
        if (runtime is not null) await runtime.ExitAsync();
        ReturnToLauncher();
    }

    async void SignInCancel_Click(object sender, RoutedEventArgs e) => await CancelSignIn();

    // DWM cloak: the window exists, lays out and runs WebView2 normally, but is not drawn on screen
    void SetCloaked(bool value)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            int flag = value ? 1 : 0;
            var hr = DwmSetWindowAttribute(hwnd, 13 /* DWMWA_CLOAK */, ref flag, sizeof(int));
            cloaked = value && hr == 0;
            StartupLog.Write("window", value ? $"Cloaked behind splash (hr={hr})" : "Uncloaked");
        }
        catch (Exception ex) { StartupLog.Exception("DWM cloak", ex); cloaked = false; }
    }

    void Reveal()
    {
        if (revealed) return;
        revealed = true;
        revealFailsafe?.Stop();
        // give XAML one frame to lay out the launcher / sign-in card before it appears
        var t = DispatcherQueue.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(60);
        t.IsRepeating = false;
        t.Tick += (_, _) =>
        {
            if (cloaked) SetCloaked(false);
            Activate();
            SplashWindow.FadeOutAndClose(DispatcherQueue);
        };
        t.Start();
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // ---- signed-in account (header chip + in-app account panel) ----
    void SetAccount(NetworkAccount? a)
    {
        account = a is null || string.IsNullOrWhiteSpace(a.Name) && string.IsNullOrWhiteSpace(a.Username) ? null : a;
        var signedIn = account is not null;
        SignInButton.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        AccountButton.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        if (signedIn)
        {
            var name = (string.IsNullOrWhiteSpace(account!.Name) ? account.Username : account.Name).Trim();
            AccountName.Text = name.ToUpperInvariant();
            AccountInitial.Text = AccountPanelInitial.Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
            AccountPanelName.Text = name;
            AccountPanelUsername.Text = string.IsNullOrWhiteSpace(account.Username) ? "" : "@" + account.Username.ToUpperInvariant();
            if (Uri.TryCreate(account.Avatar, UriKind.Absolute, out var avatar) && avatar.Scheme == Uri.UriSchemeHttps)
            {
                var image = new BitmapImage(avatar);
                image.ImageFailed += (_, _) => { AccountAvatar.Visibility = AccountPanelAvatar.Visibility = Visibility.Collapsed; };
                AccountAvatarBrush.ImageSource = AccountPanelAvatarBrush.ImageSource = image;
                AccountAvatar.Visibility = AccountPanelAvatar.Visibility = Visibility.Visible;
            }
            else AccountAvatar.Visibility = AccountPanelAvatar.Visibility = Visibility.Collapsed;
            if (!NetworkState.Text.StartsWith("OFFLINE", StringComparison.OrdinalIgnoreCase)) NetworkState.Text = "GRID ONLINE";
            CheckDailyRewards();
        }
        else if (NetworkState.Text.StartsWith("SIGNED IN", StringComparison.OrdinalIgnoreCase)) NetworkState.Text = "GRID ONLINE";
        if (launcherFocus == LauncherFocus.SignIn && Launcher.Visibility == Visibility.Visible && Panel.Visibility != Visibility.Visible && !startupOpen)
            SetLauncherFocus(LauncherFocus.SignIn);
        StartupLog.Write("auth", signedIn ? "Account chip shown" : "Signed out; SIGN IN shown");
    }

    void Account_Click(object sender, RoutedEventArgs e)
    {
        if (account is null) { SignIn_Click(sender, e); return; }
        PanelTitle.Text = "ACCOUNT";
        PanelTitle.FontSize = 36;
        PanelBody.Text = "";
        PanelBody.Visibility = Visibility.Collapsed;
        OptionControls.Visibility = Visibility.Collapsed;
        UpdateCard.Visibility = Visibility.Collapsed;
        OptionsLegal.Visibility = Visibility.Collapsed;
        AccountControls.Visibility = Visibility.Visible;
        SignOutButton.IsEnabled = true;
        SignOutButton.Content = "SIGN OUT";
        DailyButton.IsEnabled = true;
        DailyButton.Content = "DAILY REWARDS";
        Panel.Visibility = Visibility.Visible;
        Launcher.Visibility = Visibility.Collapsed;
        panelFocus = 0;
        FocusPanelControl(PanelControls()[0]);
    }

    async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        SignOutButton.IsEnabled = false;
        SignOutButton.Content = "SIGNING OUT...";
        try { await EnsureWebRuntime().SignOutAsync(); }
        catch (Exception ex) { StartupLog.Exception("Sign out", ex); }
        SetAccount(null);
        ReturnToLauncher();
        SetLauncherFocus(LauncherFocus.SignIn);
    }

    // X / B / Esc on the floating sign-in window exits the app (PLAY AS GUEST opens the arcade without an account)
    void CloseWelcome()
    {
        StartupLog.Write("window", "Sign-in window closed; exiting");
        SplashWindow.Close();
        Close();
    }

    // ---- floating sign-in window: a small borderless window centered on the desktop (no fullscreen launcher) ----
    bool floating;

    void FloatWelcome()
    {
        WelcomeGlow.Visibility = Visibility.Collapsed;
        WelcomeCard.Measure(new Windows.Foundation.Size(500, double.PositiveInfinity));
        var height = WelcomeCard.DesiredSize.Height;
        SetFloating(true, 500, height > 100 ? height : 680);
    }

    void SetFloating(bool on, double width = 0, double height = 0)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));
            var wasFloating = floating && appWindow.Presenter.Kind == AppWindowPresenterKind.Overlapped;
            floating = on;
            var chrome = on ? Visibility.Collapsed : Visibility.Visible;
            WelcomeGlow.Visibility = SignInGlow.Visibility = chrome;
            WelcomeDim.Visibility = WelcomeScan.Visibility = chrome;
            WelcomeFace.CornerRadius = SignInFace.CornerRadius = new CornerRadius(on ? 8 : 28);
            Root.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[on ? "AgCardSolid" : "AgInk"];
            if (on)
            {
                presenterApplied = false;
                var scale = GetDpiForWindow(hwnd) / 96.0;
                if (scale <= 0) scale = 1;
                int w = (int)Math.Ceiling(width * scale), h = (int)Math.Ceiling(height * scale);
                if (appWindow.Presenter.Kind != AppWindowPresenterKind.Overlapped || !wasFloating)
                {
                    var presenter = OverlappedPresenter.Create();
                    presenter.IsResizable = false;
                    presenter.IsMaximizable = false;
                    presenter.SetBorderAndTitleBar(false, false);
                    appWindow.SetPresenter(presenter);
                }
                // first time: the saved screen (e.g. the TV); after that, stay on whatever screen the card was dragged to
                var screen = wasFloating ? ScreenOfWindow(appWindow) : SavedScreen() ?? ScreenOfWindow(appWindow);
                int ax, ay, aw, ah;
                if (screen is not null) { ax = screen.WorkX; ay = screen.WorkY; aw = screen.WorkWidth; ah = screen.WorkHeight; }
                else { var area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea; ax = area.X; ay = area.Y; aw = area.Width; ah = area.Height; }
                if (wasFloating)
                {
                    // keep the card where the player put it, just resized around its center
                    var cx = appWindow.Position.X + appWindow.Size.Width / 2;
                    var cy = appWindow.Position.Y + appWindow.Size.Height / 2;
                    var x = Math.Clamp(cx - w / 2, ax, Math.Max(ax, ax + aw - w));
                    var y = Math.Clamp(cy - h / 2, ay, Math.Max(ay, ay + ah - h));
                    appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, w, h));
                }
                else appWindow.MoveAndResize(new Windows.Graphics.RectInt32(ax + (aw - w) / 2, ay + (ah - h) / 2, w, h));
                int round = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
                StartupLog.Write("window", $"Floating sign-in window {w}x{h} on {screen?.Label ?? "primary"}");
            }
            else
            {
                // the arcade opens on the screen the sign-in card is on (drag it to the TV first, if you like)
                ApplyMainPresenter(ScreenOfWindow(appWindow));
            }
        }
        catch (Exception ex) { StartupLog.Exception("Floating sign-in window", ex); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint GetDpiForWindow(IntPtr hwnd);

    void WelcomeClose_Click(object sender, RoutedEventArgs e) => CloseWelcome();

    // ======== screens: fullscreen / windowed on the chosen monitor or TV ========
    bool presenterApplied, dragging, initializingDisplay;
    List<ScreenInfo> screens = [];

    AppWindow MainAppWindow() =>
        AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this)));

    ScreenInfo? SavedScreen()
    {
        if (screens.Count == 0) screens = DisplayService.Detect();
        return DisplayService.Find(screens, settings.Display);
    }

    ScreenInfo? ScreenOfWindow(AppWindow appWindow)
    {
        screens = DisplayService.Detect();
        int cx = appWindow.Position.X + appWindow.Size.Width / 2, cy = appWindow.Position.Y + appWindow.Size.Height / 2;
        return screens.FirstOrDefault(s => s.Contains(cx, cy)) ?? screens.FirstOrDefault(s => s.Primary) ?? screens.FirstOrDefault();
    }

    /// <summary>Puts the arcade fullscreen (or in a normal window) on a screen; null = the saved screen, else where it is now.</summary>
    void ApplyMainPresenter(ScreenInfo? target = null)
    {
        presenterApplied = true; // set first: switching presenters can re-raise Activated
        try
        {
            var appWindow = MainAppWindow();
            target ??= SavedScreen() ?? ScreenOfWindow(appWindow);
            if (target is not null)
            {
                // move while overlapped: a fullscreen window can't be moved, it takes the screen it is on
                if (appWindow.Presenter.Kind != AppWindowPresenterKind.Overlapped) appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                int w = settings.Windowed ? target.WorkWidth * 3 / 4 : Math.Min(target.WorkWidth, 1280);
                int h = settings.Windowed ? target.WorkHeight * 3 / 4 : Math.Min(target.WorkHeight, 720);
                appWindow.MoveAndResize(new Windows.Graphics.RectInt32(target.WorkX + (target.WorkWidth - w) / 2, target.WorkY + (target.WorkHeight - h) / 2, w, h));
            }
            if (settings.Windowed)
            {
                var presenter = OverlappedPresenter.Create();
                presenter.IsResizable = true;
                presenter.IsMaximizable = true;
                presenter.IsMinimizable = true;
                presenter.SetBorderAndTitleBar(true, true);
                appWindow.SetPresenter(presenter);
            }
            else appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            if (target is not null && settings.Display != target.Key) { settings.Display = target.Key; settings.Save(); }
            StartupLog.Write("window", $"{(settings.Windowed ? "Windowed" : "Fullscreen")} on {target?.Label ?? "current screen"}");
            // refresh the dropdown after this event finishes (never swap its items inside its own SelectionChanged)
            if (OptionsDisplayTab.Visibility == Visibility.Visible) DispatcherQueue.TryEnqueue(RefreshScreens);
        }
        catch (Exception ex) { StartupLog.Exception("Apply presenter", ex); }
    }

    void MoveToNextScreen(int delta)
    {
        screens = DisplayService.Detect();
        if (screens.Count < 2) return;
        var current = ScreenOfWindow(MainAppWindow());
        var i = current is null ? 0 : screens.FindIndex(s => s.Key == current.Key);
        ApplyMainPresenter(screens[((i + delta) % screens.Count + screens.Count) % screens.Count]);
    }

    void RefreshScreens()
    {
        initializingDisplay = true;
        try
        {
            var current = ScreenOfWindow(MainAppWindow()); // also re-detects, so a TV switched on later shows up
            ScreenCombo.ItemsSource = screens.Select(s => s.Label).ToList();
            ScreenCombo.SelectedIndex = current is null ? -1 : screens.FindIndex(s => s.Key == current.Key);
            ScreenCombo.IsEnabled = screens.Count > 1 && !floating;
            ScreenHint.Text = screens.Count > 1
                ? $"{screens.Count} screens found. Pick the monitor or TV to play on."
                : "Only one screen found. Plug in or turn on your TV, then reopen this tab.";
            DisplayModeCombo.SelectedIndex = settings.Windowed ? 1 : 0;
        }
        finally { initializingDisplay = false; }
    }

    void ScreenCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializingDisplay || initializingSettings || ScreenCombo.SelectedIndex < 0 || ScreenCombo.SelectedIndex >= screens.Count) return;
        var target = screens[ScreenCombo.SelectedIndex];
        ApplyMainPresenter(target);
        DisplayStatus.Text = $"NOW PLAYING ON {target.Name}.";
    }

    void DisplayModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializingDisplay || initializingSettings || DisplayModeCombo.SelectedIndex < 0) return;
        var windowed = DisplayModeCombo.SelectedIndex == 1;
        if (windowed == settings.Windowed) return;
        settings.Windowed = windowed;
        settings.Save();
        ApplyMainPresenter(ScreenOfWindow(MainAppWindow()));
        DisplayStatus.Text = windowed ? "WINDOWED. DRAG THE TITLE BAR ANYWHERE, OR MAXIMIZE IT." : "FULLSCREEN.";
    }

    void QualityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializingSettings || QualityCombo.SelectedIndex < 0) return;
        settings.Quality = QualityCombo.SelectedIndex == 1 ? "hd" : "standard";
        settings.Save();
        if (web is not null) web.Quality = settings.Quality;
    }

    void ScanlinesToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (initializingSettings) return;
        settings.HideScanlines = !ScanlinesToggle.IsOn;
        settings.Save();
        ApplyScanlines();
    }

    void ApplyScanlines()
    {
        var on = !settings.HideScanlines;
        LauncherScan.Opacity = on ? 0.6 : 0;
        CoverScan.Opacity = on ? 0.6 : 0;
        WelcomeScan.Opacity = on ? 0.35 : 0;
    }

    void GpuToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (initializingSettings) return;
        settings.DisableGpu = !GpuToggle.IsOn;
        settings.Save();
        DisplayStatus.Text = GpuToggle.IsOn
            ? "HARDWARE ACCELERATION ON. RESTART ARCADEGRIDZ TO APPLY."
            : "HARDWARE ACCELERATION OFF (ONLY FOR GLITCHY GRAPHICS). RESTART ARCADEGRIDZ TO APPLY.";
    }

    static void StepCombo(ComboBox combo, int delta)
    {
        var count = combo.Items.Count;
        if (count == 0) return;
        var i = combo.SelectedIndex;
        for (int n = 0; n < count; n++)
        {
            i = Math.Clamp(i + delta, 0, count - 1);
            if (combo.Items[i] is not ComboBoxItem { IsEnabled: false }) { combo.SelectedIndex = i; return; }
            if (i == 0 || i == count - 1) return;
        }
    }

    // ======== drag the window: the sign-in card anywhere, the fullscreen arcade by its background ========
    Windows.Foundation.Point? dragStart;

    void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (consoleMode || e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse) return;
        if (DailyLayer.Visibility == Visibility.Visible || updatePromptOpen) return; // popups: clicks never drag the window
        var point = e.GetCurrentPoint(Root);
        if (!point.Properties.IsLeftButtonPressed) return;
        if (floating && !signInPanelOpen || floating && point.Position.Y < 90)
        {
            // the floating sign-in card (or the email card's header): drag it like a title bar
            e.Handled = true;
            BeginNativeMove(false);
            return;
        }
        if (gameOpen || startupOpen || settings.Windowed) return;
        dragStart = point.Position;
    }

    void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (dragStart is not { } start || dragging) return;
        var point = e.GetCurrentPoint(Root);
        if (!point.Properties.IsLeftButtonPressed) { dragStart = null; return; }
        if (Math.Abs(point.Position.X - start.X) + Math.Abs(point.Position.Y - start.Y) < 24) return;
        dragStart = null;
        BeginNativeMove(true);
    }

    void Root_PointerReleased(object sender, PointerRoutedEventArgs e) => dragStart = null;

    void BeginNativeMove(bool fromFullscreen)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var appWindow = MainAppWindow();
            if (fromFullscreen)
            {
                // shrink to a window under the cursor, let Windows drag it, then go fullscreen on the screen it lands on
                dragging = true;
                GetCursorPos(out var cursor);
                var screen = screens.Count > 0 ? screens.FirstOrDefault(s => s.Contains(cursor.X, cursor.Y)) : null;
                screen ??= ScreenOfWindow(appWindow);
                int w = Math.Max(640, (screen?.Width ?? 1920) * 45 / 100), h = Math.Max(360, (screen?.Height ?? 1080) * 45 / 100);
                var presenter = OverlappedPresenter.Create();
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.SetBorderAndTitleBar(false, false);
                appWindow.SetPresenter(presenter);
                appWindow.MoveAndResize(new Windows.Graphics.RectInt32(cursor.X - w / 2, cursor.Y - 40, w, h));
                StartupLog.Write("window", "Dragging the arcade to another screen");
            }
            Root.ReleasePointerCaptures();
            ReleaseCapture();
            SendMessage(hwnd, 0x00A1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero); // returns on mouse up
            if (fromFullscreen)
            {
                GetCursorPos(out var drop);
                screens = DisplayService.Detect();
                var target = screens.FirstOrDefault(s => s.Contains(drop.X, drop.Y)) ?? ScreenOfWindow(appWindow);
                ApplyMainPresenter(target);
            }
        }
        catch (Exception ex)
        {
            StartupLog.Exception("Drag window", ex);
            if (fromFullscreen) ApplyMainPresenter();
        }
        finally { dragging = false; }
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    // ======== games never show the website: cover until the game runs, bail out if it can't ========
    void StartLaunchWatchdog(GameEntry g)
    {
        launchWatchdog?.Stop();
        launchWatchdog = DispatcherQueue.CreateTimer();
        launchWatchdog.Interval = TimeSpan.FromSeconds(30);
        launchWatchdog.IsRepeating = false;
        launchWatchdog.Tick += async (_, _) =>
        {
            if (!gameOpen || signInPanelOpen || GameCover.Visibility != Visibility.Visible) return;
            StartupLog.Write("runtime", $"Launch watchdog: {g.Id} never started");
            await ExitGame();
            ShowMessage("GAME DIDN'T START", $"{g.Name} took too long to load. Check your connection and try again.");
        };
        launchWatchdog.Start();
    }

    void OnGameNavigating(string url)
    {
        // any page load during a game (sign-in bounce, reload) stays behind the loading cover until the game is running again
        if (!gameOpen || signInPanelOpen || !ReferenceEquals(active, web) || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
        if (GameCover.Visibility != Visibility.Visible)
        {
            GameCover.Visibility = Visibility.Visible;
            BeginStoryboard("CoverPulse");
            if (GameGrid.SelectedItem is GameEntry g) StartLaunchWatchdog(g);
        }
    }

    async void OnPageError(string error)
    {
        if (!gameOpen || signInPanelOpen || !ReferenceEquals(active, web)) return;
        var name = GameCoverTitle.Text;
        await ExitGame();
        ShowMessage("GAME DIDN'T START", error switch
        {
            "unknown_game" => $"{name} isn't in the arcade right now. Refresh the game list in Options and try again.",
            "app_page_lost" => $"{name} lost its connection while loading. Please try again.",
            _ => $"{name} couldn't start ({error}). Please try again."
        });
    }

    // ======== daily rewards, natively (same Network rewards as the websites, no web page) ========
    async void CheckDailyRewards(bool force = false)
    {
        try
        {
            if (account is null || web?.Token is not { Length: > 0 } token) return;
            var state = await daily.VisitAsync(token, account.Id, force);
            if (state is null || state.NewSparkz <= 0) return;
            pendingDaily = state;
            TryShowDaily();
        }
        catch (Exception ex) { StartupLog.Exception("Daily rewards", ex); }
    }

    bool TryShowDaily()
    {
        if (pendingDaily is null || gameOpen || startupOpen || floating || Launcher.Visibility != Visibility.Visible || Panel.Visibility == Visibility.Visible) return false;
        var state = pendingDaily;
        pendingDaily = null;
        ShowDaily(state, fresh: true);
        return true;
    }

    async void Daily_Click(object sender, RoutedEventArgs e)
    {
        if (web?.Token is not { Length: > 0 } token) return;
        DailyButton.IsEnabled = false;
        DailyButton.Content = "LOADING...";
        var state = await daily.StateAsync(token);
        DailyButton.IsEnabled = true;
        DailyButton.Content = "DAILY REWARDS";
        if (state is null) { DailyButton.Content = "REWARDS UNAVAILABLE · TRY AGAIN"; return; }
        Panel.Visibility = Visibility.Collapsed;
        Launcher.Visibility = Visibility.Visible;
        ShowDaily(state, fresh: false);
    }

    void ShowDaily(DailyState state, bool fresh)
    {
        var stage = state.Stage;
        DailyEyebrow.Text = fresh ? "GRiDDiM NETWORK  ·  DAILY REWARD" : "GRiDDiM NETWORK  ·  DAILY REWARDS";
        DailySubtitle.Text = $"CYCLE {state.Cycle:00}  ·  {stage} / 7 SEALS RESTORED";
        DailySeals.Children.Clear();
        var gold = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgGold"];
        var cyan = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgCyan"];
        var dim = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgDim"];
        var ink = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgInk"];
        var pixel = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["AgPixel"];
        var display = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["AgDisplay"];
        for (int day = 1; day <= 7; day++)
        {
            var lit = day <= stage;
            var today = day == stage;
            var seal = new Border
            {
                Width = 74, Height = 92, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(today ? 3 : 2),
                BorderBrush = lit ? (day == 7 ? gold : cyan) : dim,
                Background = lit ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x55, 0x5E, 0xE7, 0xFF)) : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x99, 0x0C, 0x0A, 0x26)),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center, Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = $"DAY {day}", FontFamily = pixel, FontSize = 8, Foreground = lit ? ink : dim, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = lit ? (day == 7 ? "✦" : "◆") : "◇", FontFamily = display, FontSize = 26, Foreground = lit ? (day == 7 ? gold : cyan) : dim, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = day == 7 ? $"+{state.StreakBonus:N0}" : lit ? "LINKED" : "SEALED", FontFamily = pixel, FontSize = 7, Foreground = lit ? ink : dim, HorizontalAlignment = HorizontalAlignment.Center }
                    }
                }
            };
            if (lit) ((StackPanel)seal.Child).Children.OfType<TextBlock>().ToList().ForEach(t => { if (t.Text != "◆" && t.Text != "✦") t.Foreground = cyan; });
            DailySeals.Children.Add(seal);
        }
        var total = state.NewSparkz;
        dailyState = state;
        dailyRevealed = total <= 0;
        DailyDropScale.ScaleX = DailyDropScale.ScaleY = 1;
        DailyDrop.Text = total > 0 ? "DROP SECURED" : stage == 7 ? "CORE UNLOCKED" : stage > 0 ? "CHECKED IN" : "NO SIGNAL YET";
        DailyDropNote.Text = total > 0
            ? string.Join("  ·  ", state.Rewards!.Where(r => r.Amount > 0).Select(r => $"+{r.Amount:N0} {(r.Name ?? "REWARD").ToUpperInvariant()}")) + "\nAlready added to your balance."
            : stage == 7 ? "The core is open. Tomorrow starts a new seven-seal cycle." : "Come back tomorrow to release the next seal.";
        DailyBalance.Text = "✦ " + state.BalanceText;
        DailyStreak.Text = $"{state.Streak} {(state.Streak == 1 ? "DAY" : "DAYS")}";
        DailyBonus.Text = $"+{state.StreakBonus:N0}";
        DailyQuests.Children.Clear();
        foreach (var q in state.Quests ?? [])
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var mark = new TextBlock { Text = q.Completed ? "✓" : "○", FontSize = 18, Foreground = q.Completed ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgGreen"] : dim, VerticalAlignment = VerticalAlignment.Center };
            var copy = new StackPanel { Spacing = 2 };
            copy.Children.Add(new TextBlock { Text = (q.Name ?? "").ToUpperInvariant(), FontFamily = pixel, FontSize = 9, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgText"], TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrWhiteSpace(q.Description)) copy.Children.Add(new TextBlock { Text = q.Description, FontSize = 13, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgMuted"], TextWrapping = TextWrapping.Wrap });
            var reward = new TextBlock { Text = $"+{q.Reward:N0} ✦", FontFamily = display, FontSize = 16, Foreground = gold, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(copy, 1);
            Grid.SetColumn(reward, 2);
            row.Children.Add(mark);
            row.Children.Add(copy);
            row.Children.Add(reward);
            DailyQuests.Children.Add(new Border { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x99, 0x0C, 0x0A, 0x26)), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 8, 14, 8), Child = row });
        }
        if (total > 0) DailyDropNote.Text = "Your rewards are already credited. Reveal today's drop.";
        // same flow as the website: REVEAL DROP (OPEN CORE on day 7) first, then COLLECT closes
        DailyCloseLabel.Text = total > 0 ? (stage == 7 ? "OPEN CORE" : "REVEAL DROP") : "CLOSE";
        DailyLayer.Visibility = Visibility.Visible;
        PadFocus(DailyCloseButton);
        StartupLog.Write("daily", $"Shown; fresh={fresh}; stage={stage}; new={total}");
    }

    void CloseDaily()
    {
        StartupLog.Write("daily", "Closed");
        DailyLayer.Visibility = Visibility.Collapsed;
        if (Launcher.Visibility == Visibility.Visible) SetLauncherFocus(launcherFocus);
    }

    void DailyClose_Click(object sender, RoutedEventArgs e) => CloseDaily();

    DailyState? dailyState;
    bool dailyRevealed = true;

    void DailyPrimary_Click(object sender, RoutedEventArgs e) => DailyPrimary();

    void DailyPrimary()
    {
        if (dailyRevealed || dailyState is null) { CloseDaily(); return; }
        dailyRevealed = true;
        var state = dailyState;
        DailyDrop.Text = $"+{state.NewSparkz:N0} SPARKZ";
        DailyDropNote.Text = string.Join("  ·  ", (state.Rewards ?? []).Where(r => r.Amount > 0).Select(r => $"+{r.Amount:N0} {(r.Name ?? "REWARD").ToUpperInvariant()}"))
            + "\nSignal accepted. Your Sparkz are in your balance.";
        DailyCloseLabel.Text = "COLLECT";
        try
        {
            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            foreach (var prop in new[] { "ScaleX", "ScaleY" })
            {
                var a = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = 0.3, To = 1, Duration = TimeSpan.FromMilliseconds(700),
                    EasingFunction = new Microsoft.UI.Xaml.Media.Animation.ElasticEase { Oscillations = 2, Springiness = 4, EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
                };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(a, DailyDropScale);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(a, prop);
                sb.Children.Add(a);
            }
            sb.Begin();
        }
        catch (Exception ex) { StartupLog.Exception("Daily reveal animation", ex); }
        StartupLog.Write("daily", $"Revealed +{state.NewSparkz}");
    }

    // click outside the card closes it; clicks inside the card don't
    void DailyBackdrop_Tapped(object sender, TappedRoutedEventArgs e) => CloseDaily();

    void DailyCard_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    // ======== mouse wheel: scroll the game grid ========
    ScrollViewer? gridScroller;
    bool wheelLogged, wheelDiagLogged;
    double launcherScale = 1;

    void Root_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!wheelLogged) { wheelLogged = true; StartupLog.Write("input", $"First wheel event: handled={e.Handled}; source={e.OriginalSource?.GetType().Name}"); }
        // the grid's own ScrollViewer already took it: don't scroll twice
        if (e.Handled && e.OriginalSource is DependencyObject src && FindAncestor<ScrollViewer>(src) is { } sv && ReferenceEquals(sv, gridScroller ??= FindChild<ScrollViewer>(GameGrid))) return;
        if (Launcher.Visibility != Visibility.Visible || Panel.Visibility == Visibility.Visible || gameOpen || DailyLayer.Visibility == Visibility.Visible) return;
        var point = e.GetCurrentPoint(Root);
        // bounds in Root coordinates (the launcher is scaled by a Viewbox, so ActualWidth/Height alone would be off)
        var bounds = GameGrid.TransformToVisual(Root).TransformBounds(new Windows.Foundation.Rect(0, 0, GameGrid.ActualWidth, GameGrid.ActualHeight));
        var over = bounds.Contains(point.Position);
        gridScroller ??= FindChild<ScrollViewer>(GameGrid);
        if (!wheelDiagLogged)
        {
            wheelDiagLogged = true;
            StartupLog.Write("input", $"Wheel: over={over}; grid={GameGrid.ActualWidth:0}x{GameGrid.ActualHeight:0}; scroller={(gridScroller is null ? "none" : $"extent={gridScroller.ExtentHeight:0} viewport={gridScroller.ViewportHeight:0} scrollable={gridScroller.ScrollableHeight:0}")}; root={Root.ActualWidth:0}x{Root.ActualHeight:0}; scale={launcherScale:0.00}");
        }
        if (!over) return;
        var delta = point.Properties.MouseWheelDelta; // +120 per notch away from the user
        if (delta == 0) return;
        e.Handled = true;
        if (gridScroller is not null && gridScroller.ScrollableHeight > 1)
        {
            var target = Math.Clamp(gridScroller.VerticalOffset - delta / 120.0 * 150, 0, gridScroller.ScrollableHeight);
            gridScroller.ChangeView(null, target, null, false);
            return;
        }
        // fallback: step the selection a row at a time (like the d-pad), which scrolls the picked card into view
        if (visible.Count == 0) return;
        var cols = GridColumns();
        var i = Math.Clamp(Math.Max(0, GameGrid.SelectedIndex) + (delta > 0 ? -cols : cols), 0, visible.Count - 1);
        GameGrid.SelectedIndex = i;
        GameGrid.ScrollIntoView(GameGrid.SelectedItem);
    }

    static T? FindAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        for (var p = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node); p is not null; p = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(p))
            if (p is T hit) return hit;
        return null;
    }

    static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T hit) return hit;
            if (FindChild<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    // ======== fit the game browser to small screens ========
    const double DesignHeight = 900, DesignWidth = 1500;

    void FitLauncher()
    {
        var w = Root.ActualWidth;
        var h = Root.ActualHeight;
        if (w < 100 || h < 100) return;
        var scale = Math.Min(1, Math.Min(h / DesignHeight, w / DesignWidth));
        if (Math.Abs(scale - launcherScale) < 0.001 && Math.Abs(Launcher.Width - w / scale) < 0.5 && Math.Abs(Launcher.Height - h / scale) < 0.5) return;
        launcherScale = scale;
        Launcher.Width = w / scale;
        Launcher.Height = h / scale;
        StartupLog.Write("window", $"Game browser fit: window={w:0}x{h:0}; scale={scale:0.00}");
    }

    // ======== update check before the sign-in screen ========
    bool updatePromptOpen;
    Action? afterUpdatePrompt;

    async Task<UpdateInfo?> StartupUpdateCheckAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var result = await updates.CheckAsync(cts.Token);
            StartupLog.Write("update", $"Startup check; installed={result.InstalledVersion}; available={result.Update?.Version}");
            availableUpdate = result.Update;
            if (result.Update is not null) InstallUpdateButton.IsEnabled = true;
            return result.Update;
        }
        catch (Exception ex)
        {
            StartupLog.Write("update", $"Startup check skipped: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    void ShowUpdatePrompt(UpdateInfo update, Action continueWith)
    {
        afterUpdatePrompt = continueWith;
        updatePromptOpen = true;
        startupOpen = true;
        Launcher.Visibility = Visibility.Collapsed;
        Panel.Visibility = Visibility.Collapsed;
        StartupLayer.Visibility = Visibility.Visible;
        WelcomePanel.Visibility = Visibility.Visible;
        WelcomeCard.Visibility = Visibility.Collapsed;
        UpdatePromptCard.Visibility = Visibility.Visible;
        UpdatePromptVersion.Text = $"V{UpdateService.InstalledVersion}  →  V{update.Version}";
        UpdatePromptProgress.Visibility = Visibility.Collapsed;
        UpdateNowButton.IsEnabled = UpdateLaterButton.IsEnabled = true;
        UpdateNowLabel.Text = "UPDATE NOW";
        UpdatePromptCard.Measure(new Windows.Foundation.Size(500, double.PositiveInfinity));
        var height = UpdatePromptCard.DesiredSize.Height;
        SetFloating(true, 500, height > 100 ? height : 520);
        PadFocus(UpdateNowButton);
        StartupLog.Write("update", $"Prompting before sign-in: {update.Version}");
    }

    void CloseUpdatePrompt()
    {
        updatePromptOpen = false;
        UpdatePromptCard.Visibility = Visibility.Collapsed;
        WelcomeCard.Visibility = Visibility.Visible;
        WelcomePanel.Visibility = Visibility.Collapsed;
        startupOpen = false;
        var next = afterUpdatePrompt;
        afterUpdatePrompt = null;
        next?.Invoke();
    }

    void UpdateLater_Click(object sender, RoutedEventArgs e)
    {
        if (!UpdateLaterButton.IsEnabled) return;
        StartupLog.Write("update", "Startup prompt: not now");
        CloseUpdatePrompt();
    }

    async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (availableUpdate is null || !UpdateNowButton.IsEnabled) return;
        var update = availableUpdate;
        UpdateNowButton.IsEnabled = UpdateLaterButton.IsEnabled = false;
        UpdatePromptProgress.Value = 0;
        UpdatePromptProgress.Visibility = Visibility.Visible;
        UpdateNowLabel.Text = "DOWNLOADING...";
        try
        {
            var progress = new Progress<double>(value =>
            {
                UpdatePromptProgress.Value = value * 100;
                UpdateNowLabel.Text = $"DOWNLOADING  {value:P0}";
            });
            var installer = await updates.DownloadAsync(update, progress);
            UpdateNowLabel.Text = "INSTALLING...";
            StartupLog.Write("update", $"Startup prompt: verified installer for {update.Version}: {installer}");
            UpdateService.InstallAndRestart(installer);
        }
        catch (Exception ex)
        {
            StartupLog.Exception("Startup update install", ex);
            UpdatePromptText.Text = "The update couldn't be downloaded. You can try again from Options > UPDATES.";
            UpdatePromptProgress.Visibility = Visibility.Collapsed;
            UpdateNowLabel.Text = "TRY AGAIN";
            UpdateNowButton.IsEnabled = UpdateLaterButton.IsEnabled = true;
            PadFocus(UpdateLaterButton);
        }
    }

    void HandleUpdatePromptAction(PadAction action)
    {
        if (action == PadAction.B) { UpdateLater_Click(this, new RoutedEventArgs()); return; }
        if (action is PadAction.Up or PadAction.Left) PadFocus(UpdateNowButton);
        else if (action is PadAction.Down or PadAction.Right) PadFocus(UpdateLaterButton);
        else if (action == PadAction.A)
        {
            if (UpdateLaterButton.FocusState != FocusState.Unfocused) UpdateLater_Click(this, new RoutedEventArgs());
            else UpdateNow_Click(this, new RoutedEventArgs());
        }
    }

    // ======== controller / keyboard focus ========
    // ALWAYS move focus with FocusState.Keyboard, never Programmatic. Windows only draws the focus highlight for
    // keyboard-state focus, so after any mouse use (e.g. dragging the window to the TV) Programmatic focus moves
    // silently: up/down seem dead and only the grid's left/right selection shows. This has regressed several times.
    static void PadFocus(Control? control) => control?.Focus(FocusState.Keyboard);

    // the selected card only glows while the grid (or its PLAY button) has focus; menu buttons get the highlight alone
    void UpdateTileHighlight()
    {
        if (GameGrid.SelectedItem is null || GameGrid.ContainerFromItem(GameGrid.SelectedItem) is not GridViewItem tile) return;
        var lit = launcherFocus is LauncherFocus.GameGrid or LauncherFocus.Play;
        VisualStateManager.GoToState(tile, lit ? "Selected" : "Normal", true);
    }
}
