using System.Collections.ObjectModel;
using ArcadeGridz.App.Models;
using ArcadeGridz.App.Runtimes;
using ArcadeGridz.App.Services;
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
    enum LauncherFocus { Categories, Games, Scores, Options, Exit, GameGrid, Play, SignIn }
    enum StartupFocus { Email, Google, Guest, Terms, Privacy }
    const string TermsUrl = "https://griddim.com/terms/";
    const string PrivacyUrl = "https://griddim.com/privacy/";
    readonly CatalogService catalogs = new();
    readonly LeaderboardService leaderboards = new();
    readonly SettingsService settings = SettingsService.Load();
    readonly UpdateService updates = new();
    readonly DesktopAuthService desktopAuth = new();
    readonly ObservableCollection<GameEntry> visible = [];
    readonly List<GameEntry> all = [];
    readonly List<string> categories = [];
    readonly NativeGameRuntime native = new();
    GamepadService? pads;
    WebGameRuntime? web;
    IGameRuntime? active;
    int categoryIndex;
    bool gameOpen;
    UpdateInfo? availableUpdate;
    LauncherFocus launcherFocus = LauncherFocus.GameGrid;
    int panelFocus;
    StartupFocus startupFocus = StartupFocus.Email;
    bool startupOpen = true;
    bool consoleMode;
    bool initializingSettings;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? clockTimer;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? splashTimer;
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
            GameGrid.ItemsSource = visible;
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
        web = new WebGameRuntime(view);
        web.SetConsoleMode(consoleMode);
        web.Exited += (_, _) => DispatcherQueue.TryEnqueue(ReturnToLauncher);
        web.Status += status => DispatcherQueue.TryEnqueue(() =>
        {
            GameStatus.Text = status.ToUpperInvariant();
            if (status.Equals("started", StringComparison.OrdinalIgnoreCase))
            {
                GameCover.Visibility = Visibility.Collapsed;
                StopStoryboard("CoverPulse");
            }
            else if (status.Equals("auth", StringComparison.OrdinalIgnoreCase))
            {
                NetworkState.Text = "SIGNED IN • GRID ONLINE";
                StartupLog.Write("auth", "Desktop sign-in completed");
                ReturnToLauncher();
            }
        });
        return web;
    }

    void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState != WindowActivationState.Deactivated) QueueCatalogCheck();
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
            appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            StartupLog.Write("window", "Fullscreen presenter active");
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
            BeginStoryboard("SplashPulse");
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
            initializingSettings = false;
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
            var hasPersistedSession = await EnsureWebRuntime().HasPersistedSessionAsync();
            splashTimer = DispatcherQueue.CreateTimer();
            splashTimer.Interval = TimeSpan.FromMilliseconds(1350);
            splashTimer.IsRepeating = false;
            splashTimer.Tick += (_, _) =>
            {
                if (hasPersistedSession)
                {
                    NetworkState.Text = "SIGNED IN • GRID ONLINE";
                    EnterLauncher(false);
                }
                else ShowWelcome();
            };
            splashTimer.Start();
            StartupLog.Write("window", hasPersistedSession
                ? "Launcher ready; valid session will bypass welcome"
                : "Launcher ready; splash active");
        }
        catch (Exception ex)
        {
            StartupLog.Exception("MainWindow.OnLoaded", ex);
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
        RefreshCatalogButton.Focus(FocusState.Programmatic);
    }

    void BuildCategories()
    {
        categories.Clear();
        categories.Add("ALL GAMES");
        categories.AddRange(all.Where(g => settings.ShowAll || (g.Potato && g.Playable)).Select(g => g.Category.ToUpperInvariant()).Where(x => x.Length > 0).Distinct().Order());
        categoryIndex = Math.Min(categoryIndex, categories.Count - 1);
    }

    void ApplyFilter()
    {
        var cat = categories.Count == 0 ? "ALL GAMES" : categories[categoryIndex];
        CategoryLabel.Text = cat;
        visible.Clear();
        foreach (var g in all.Where(g => (settings.ShowAll || (g.Potato && g.Playable)) && (cat == "ALL GAMES" || g.Category.Equals(cat, StringComparison.OrdinalIgnoreCase))))
            visible.Add(g);
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
        GameLayer.Visibility = Visibility.Collapsed;
        StopStoryboard("CoverPulse");
        GameCover.Visibility = Visibility.Visible;
        Launcher.Visibility = Visibility.Visible;
        Panel.Visibility = Visibility.Collapsed;
        SetLauncherFocus(LauncherFocus.GameGrid);
        QueueCatalogCheck();
    }

    async void PadPressed(PadAction action)
    {
        // GamepadService is driven by a DispatcherQueueTimer, so this already runs
        // on the UI thread. Handling it directly avoids a second queued UI hop.
        if (startupOpen && WelcomePanel.Visibility == Visibility.Visible)
        {
            HandleStartupAction(action);
            return;
        }
        if (action == PadAction.ExitCombo && gameOpen) { await ExitGame(); return; }
        if (gameOpen) return;
        if (Panel.Visibility == Visibility.Visible) { await HandlePanelAction(action); return; }
        if (action == PadAction.LB) ChangeCategory(-1);
        else if (action == PadAction.RB) ChangeCategory(1);
        else if (action == PadAction.B) ReturnToLauncher();
        else await HandleLauncherAction(action);
    }

    void ShowWelcome()
    {
        SplashPanel.Visibility = Visibility.Collapsed;
        StopStoryboard("SplashPulse");
        WelcomeShowcase.ItemsSource = all.Where(g => g.Potato && g.Playable).Take(4).ToList();
        WelcomePanel.Visibility = Visibility.Visible;
        BeginStoryboard("InsertCoinBlink");
        SetStartupFocus(StartupFocus.Email);
        StartupLog.Write("window", "Welcome screen ready");
    }

    void EnterLauncher(bool guest)
    {
        startupOpen = false;
        StopStoryboard("SplashPulse");
        StopStoryboard("InsertCoinBlink");
        StartupLayer.Visibility = Visibility.Collapsed;
        WelcomePanel.Visibility = Visibility.Collapsed;
        Launcher.Visibility = Visibility.Visible;
        if (guest)
            NetworkState.Text = NetworkState.Text.StartsWith("OFFLINE", StringComparison.OrdinalIgnoreCase)
                ? "GUEST • OFFLINE CACHE"
                : "GUEST • " + NetworkState.Text;
        SetLauncherFocus(LauncherFocus.GameGrid);
        StartupLog.Write("window", guest ? "Entered launcher as guest" : "Entered launcher signed in");
    }

    void HandleStartupAction(PadAction action)
    {
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
        control.Focus(FocusState.Programmatic);
    }

    async void StartupEmail_Click(object sender, RoutedEventArgs e)
    {
        startupOpen = false;
        StartupLayer.Visibility = Visibility.Collapsed;
        await BeginSignInAsync();
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
                case LauncherFocus.Games: ReturnToLauncher(); break;
                case LauncherFocus.Scores: Scores_Click(this, new RoutedEventArgs()); break;
                case LauncherFocus.Options: Options_Click(this, new RoutedEventArgs()); break;
                case LauncherFocus.Exit: Close(); break;
                case LauncherFocus.SignIn: SignIn_Click(this, new RoutedEventArgs()); break;
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
            ? new[] { LauncherFocus.Games, LauncherFocus.Scores, LauncherFocus.Options }
            : new[] { LauncherFocus.Games, LauncherFocus.Scores, LauncherFocus.Options, LauncherFocus.Exit };
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
        launcherFocus = target;
        Control control = target switch
        {
            LauncherFocus.Categories => NextCategoryButton,
            LauncherFocus.Games => GamesButton,
            LauncherFocus.Scores => ScoresButton,
            LauncherFocus.Options => OptionsButton,
            LauncherFocus.Exit => ExitButton,
            LauncherFocus.Play => PlayButton,
            LauncherFocus.SignIn => SignInButton,
            _ => GameGrid
        };
        control.Focus(FocusState.Programmatic);
    }

    List<Control> PanelControls()
    {
        var controls = new List<Control>();
        if (OptionControls.Visibility == Visibility.Visible)
        {
            controls.Add(ShowAllToggle);
            controls.Add(StartWithWindowsToggle);
            controls.Add(RefreshCatalogButton);
            if (ConsoleModeButton.IsEnabled) controls.Add(ConsoleModeButton);
        }
        if (UpdateActions.Visibility == Visibility.Visible)
        {
            controls.Add(CheckUpdateButton);
            if (InstallUpdateButton.IsEnabled) controls.Add(InstallUpdateButton);
        }
        if (OptionsLegal.Visibility == Visibility.Visible)
        {
            controls.Add(OptionsTermsButton);
            controls.Add(OptionsPrivacyButton);
        }
        controls.Add(BackButton);
        return controls;
    }

    async Task HandlePanelAction(PadAction action)
    {
        if (action == PadAction.B) { ReturnToLauncher(); return; }
        var controls = PanelControls();
        if (action is PadAction.Up or PadAction.Left) panelFocus = Math.Max(0, panelFocus - 1);
        else if (action is PadAction.Down or PadAction.Right) panelFocus = Math.Min(controls.Count - 1, panelFocus + 1);
        else if (action == PadAction.A)
        {
            var control = controls[Math.Clamp(panelFocus, 0, controls.Count - 1)];
            if (control == BackButton) { ReturnToLauncher(); return; }
            if (control == ShowAllToggle) ShowAllToggle.IsOn = !ShowAllToggle.IsOn;
            else if (control == StartWithWindowsToggle) StartWithWindowsToggle.IsOn = !StartWithWindowsToggle.IsOn;
            else if (control == RefreshCatalogButton) { if (RefreshCatalogButton.IsEnabled) RefreshCatalog_Click(this, new RoutedEventArgs()); return; }
            else if (control == ConsoleModeButton) ConsoleMode_Click(this, new RoutedEventArgs());
            else if (control == CheckUpdateButton) CheckUpdate_Click(this, new RoutedEventArgs());
            else if (control == InstallUpdateButton) InstallUpdate_Click(this, new RoutedEventArgs());
            else if (control == OptionsTermsButton) OpenExternal(TermsUrl);
            else if (control == OptionsPrivacyButton) OpenExternal(PrivacyUrl);
            controls = PanelControls();
            panelFocus = Math.Min(panelFocus, controls.Count - 1);
        }
        controls[Math.Clamp(panelFocus, 0, controls.Count - 1)].Focus(FocusState.Programmatic);
        await Task.CompletedTask;
    }

    void ChangeCategory(int delta)
    {
        if (categories.Count == 0) return;
        categoryIndex = (categoryIndex + delta + categories.Count) % categories.Count;
        ApplyFilter();
    }

    void PreviousCategory_Click(object sender, RoutedEventArgs e) => ChangeCategory(-1);
    void NextCategory_Click(object sender, RoutedEventArgs e) => ChangeCategory(1);

    void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (consoleMode) { e.Handled = true; return; }
        if (e.Key == VirtualKey.Escape) { if (gameOpen) _ = ExitGame(); else ReturnToLauncher(); e.Handled = true; }
        else if (!gameOpen)
        {
            var action = e.Key switch
            {
                VirtualKey.Up => PadAction.Up,
                VirtualKey.Down => PadAction.Down,
                VirtualKey.Left => PadAction.Left,
                VirtualKey.Right => PadAction.Right,
                VirtualKey.Enter or VirtualKey.Space => PadAction.A,
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

    async Task BeginSignInAsync()
    {
        try
        {
            var webRuntime = EnsureWebRuntime();
            active = webRuntime;
            gameOpen = true;
            GameCover.Visibility = Visibility.Collapsed;
            GameLayer.Visibility = Visibility.Visible;
            Launcher.Visibility = Visibility.Collapsed;
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
        PanelBody.Text = "Tune your arcade: pick which games show up, launch with Windows, refresh the game list, lock it to controller-only console mode, or grab the latest update.";
        OptionControls.Visibility = Visibility.Visible;
        initializingSettings = true;
        StartWithWindowsToggle.IsOn = WindowsStartupService.IsEnabled;
        initializingSettings = false;
        VersionText.Text = $"INSTALLED VERSION  {UpdateService.InstalledVersion}";
        VersionText.Visibility = Visibility.Visible;
        UpdateStatus.Text = "Updates are optional. ArcadeGridz never installs one without confirmation.";
        UpdateStatus.Visibility = Visibility.Visible;
        UpdateActions.Visibility = Visibility.Visible;
        UpdateCard.Visibility = Visibility.Visible;
        OptionsLegal.Visibility = Visibility.Visible;
        UpdateProgress.Visibility = Visibility.Collapsed;
        InstallUpdateButton.IsEnabled = availableUpdate is not null;
        Panel.Visibility = Visibility.Visible;
        Launcher.Visibility = Visibility.Collapsed;
        panelFocus = 0;
        ShowAllToggle.Focus(FocusState.Programmatic);
    }
    void Games_Click(object sender, RoutedEventArgs e) => ReturnToLauncher();
    void Back_Click(object sender, RoutedEventArgs e) => ReturnToLauncher();
    void Close_Click(object sender, RoutedEventArgs e) => Close();
    void ShowMessage(string title, string body)
    {
        PanelTitle.Text = title;
        PanelBody.Text = body;
        OptionControls.Visibility = Visibility.Collapsed;
        VersionText.Visibility = Visibility.Collapsed;
        UpdateStatus.Visibility = Visibility.Collapsed;
        UpdateActions.Visibility = Visibility.Collapsed;
        UpdateCard.Visibility = Visibility.Collapsed;
        OptionsLegal.Visibility = Visibility.Collapsed;
        UpdateProgress.Visibility = Visibility.Collapsed;
        Panel.Visibility = Visibility.Visible;
        Launcher.Visibility = Visibility.Collapsed;
        panelFocus = 0;
        BackButton.Focus(FocusState.Programmatic);
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
        BackButton.Focus(FocusState.Programmatic);
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
}
