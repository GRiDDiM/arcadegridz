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
    enum LauncherFocus { Categories, Games, Scores, Storyboards, Options, Testing, Exit, GameGrid, Play, SignIn, Player, Trailer, Update }
    enum StartupFocus { Email, Google, Guest, Terms, Privacy }
    const string TermsUrl = "https://griddim.com/terms/";
    const string PrivacyUrl = "https://griddim.com/privacy/";
    readonly CatalogService catalogs = new();
    readonly LeaderboardService leaderboards = new();
    readonly SettingsService settings = SettingsService.Load();
    AudioService? audio;
    MusicPlayerService? music;
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
    // ALBUMS view: the music player's ALBUM button swaps the game grid for BDC's albums (no nav button of its own)
    bool albumMode;
    readonly List<GameEntry> albumEntries = [];
    const string AlbumRuntime = "album";
    static bool IsAlbum(GameEntry g) => g.Runtime.Type == AlbumRuntime;
    static string AlbumId(GameEntry g) => g.Id.StartsWith("album:") ? g.Id[6..] : g.Id;
    static bool IsStoryboard(GameEntry g) => g.Category.Equals(StoryboardCategory, StringComparison.OrdinalIgnoreCase);
    static bool IsLive(GameEntry g) => g.Status.Equals("live", StringComparison.OrdinalIgnoreCase);
    /// <summary>Launcher list rule: launchable (or SHOW UNSUPPORTED on) and LIVE (or BETAS &amp; ALPHAS toggled on next to OPTIONS).</summary>
    bool Listed(GameEntry g) => PassesFilter(g) && (LocalLibraryService.IsLocal(g) || (settings.ShowAll || (g.Potato && g.Playable)) && (settings.ShowTesting || IsLive(g)));
    bool gameOpen;
    UpdateInfo? availableUpdate;
    LauncherFocus launcherFocus = LauncherFocus.GameGrid;
    int panelFocus;
    int optionsTab; // 0 GAMES, 1 DISPLAY, 2 THEMES, 3 SYSTEM, 4 UPDATES, 5 ABOUT
    const int OptionsTabCount = 6;
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
            SplashWindow.Dropped += (x, y) => DispatcherQueue.TryEnqueue(() => OnSplashDropped(x, y));
            audio = new AudioService(DispatcherQueue, !settings.SfxOff);
            music = new MusicPlayerService(DispatcherQueue, settings);
            music.Changed += UpdateMusicUi;
            music.AlbumsChanged += () => { BuildAlbumEntries(); if (albumMode) ApplyFilter(); };
            // keep trying until the album is in (the reveal-time load can miss: slow network, sign-in path, offline)
            musicRetry = DispatcherQueue.CreateTimer();
            musicRetry.Interval = TimeSpan.FromSeconds(12);
            musicRetry.IsRepeating = true;
            musicRetry.Tick += (_, _) =>
            {
                if (music is null || music.Album is not null) { musicRetry?.Stop(); return; }
                if (!music.Loading) _ = music.LoadAsync(autoPlay: !settings.MusicOff && !gameOpen);
            };
            // started by EnterLauncher: no music over the splash, the update prompt or the sign-in card
            BuildMeter();
            audio.Play("jingle"); // loading jingle over the splash
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
            // Alt+Enter as a window-wide accelerator: works even when nothing in the arcade has keyboard focus
            // (the PreviewKeyDown handler only saw it when focus was inside Root)
            var altEnter = new KeyboardAccelerator { Key = VirtualKey.Enter, Modifiers = Windows.System.VirtualKeyModifiers.Menu };
            altEnter.Invoked += (_, a) => { a.Handled = true; if (!floating) ToggleWindowed(); };
            WindowShell.KeyboardAccelerators.Add(altEnter);
            WindowShell.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
            // if anything (Windows, a drag, a dropped presenter switch) leaves the arcade in the wrong mode, fix it right away
            MainAppWindow().Changed += (_, args) =>
            {
                if (args.DidPresenterChange) DispatcherQueue.TryEnqueue(() => EnforcePresenter("presenter changed"));
                if (args.DidPresenterChange || args.DidSizeChange) DispatcherQueue.TryEnqueue(UpdateWindowBar);
                // fullscreen: if Windows rescales or nudges the window (DPI change, display change), put it back over the whole screen
                if ((args.DidSizeChange || args.DidPositionChange) && !settings.Windowed) DispatcherQueue.TryEnqueue(SnapFullscreen);
            };
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
        LibraryOnActivated(e.WindowActivationState != WindowActivationState.Deactivated);
        // the music player keeps playing when the app loses focus (it only stops for games)
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
            else if (e.WindowActivationState != WindowActivationState.Deactivated) EnforcePresenter("activated");
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
            UpdateTestingButton();
            StartWithWindowsToggle.IsOn = WindowsStartupService.IsEnabled;
            ScanlinesToggle.IsOn = !settings.HideScanlines;
            LaunchScreenToggle.IsOn = !settings.PinScreen;
            CityLifeToggle.IsOn = !settings.StillBackground;
            TrailerPreviewToggle.IsOn = !settings.NoTrailerPreview;
            try { InitLibrary(); } catch (Exception ex) { StartupLog.Exception("Library init", ex); }
            MusicToggle.IsOn = !settings.MusicOff;
            SfxToggle.IsOn = !settings.SfxOff;
            GpuToggle.IsOn = !settings.DisableGpu;
            DisplayModeCombo.SelectedIndex = settings.Windowed ? 1 : 0;
            QualityCombo.SelectedIndex = 0; // HD isn't available yet
            initializingSettings = false;
            ApplyScanlines();
            ApplyCityLife();
            try { InitThemes(); } catch (Exception ex) { StartupLog.Exception("Themes init", ex); }
            try { InitTrailerPreview(); } catch (Exception ex) { StartupLog.Exception("Trailer preview init", ex); }
            try { InitUpdateWatch(); } catch (Exception ex) { StartupLog.Exception("Update watch init", ex); }
            try { InitBanners(); } catch (Exception ex) { StartupLog.Exception("Banners init", ex); }
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
    bool catalogSignedIn;
    async Task<CatalogRefresh> RefreshCatalog(bool quiet = false)
    {
        if (catalogBusy) return CatalogRefresh.Same;
        catalogBusy = true;
        try
        {
            var result = await catalogs.LoadAsync(web?.Token);
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
            all.AddRange(library.GameEntries()); // the player's own PC games / ROMs (MY LIBRARY)
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
        var ready = all.Count(Listed);
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
        categories.AddRange(all.Where(g => Listed(g) && !IsStoryboard(g)).Select(g => g.Category.ToUpperInvariant()).Where(x => x.Length > 0).Distinct().Order());
        categoryIndex = Math.Min(categoryIndex, categories.Count - 1);
    }

    void ApplyFilter()
    {
        var cat = albumMode ? "ALBUMS" : storyMode ? "STORYBOARDS" : categories.Count == 0 ? "ALL GAMES" : categories[categoryIndex];
        CategoryLabel.Text = cat;
        visible.Clear();
        if (albumMode) foreach (var g in albumEntries) visible.Add(g);
        else foreach (var g in all.Where(g => Listed(g) &&
                 (storyMode ? IsStoryboard(g) : !IsStoryboard(g) && (cat == "ALL GAMES" || g.Category.Equals(cat, StringComparison.OrdinalIgnoreCase)))))
            visible.Add(g);
        InsertAddCard();
        if (albumMode) MusicAlbumButton.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgMagenta"];
        else MusicAlbumButton.ClearValue(Control.BorderBrushProperty);
        if (storyMode) StoryboardsButton.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgMagenta"];
        else StoryboardsButton.ClearValue(Control.BorderBrushProperty);
        if (visible.Count > 0) GameGrid.ScrollIntoView(visible[0]);
        // the ADD card leads the row, but the first real tile stays the default pick
        var first = visible.Count > 1 && IsAddCard(visible[0]) ? 1 : 0;
        if (visible.Count > 0) { GameGrid.SelectedIndex = first; _ = ShowHero(visible[first]); }
        else
        {
            GameGrid.SelectedIndex = -1;
            HeroName.Text = albumMode ? "LOADING ALBUMS" : "NO GAMES";
            HeroMeta.Text = "";
            HeroStatusChip.Visibility = Visibility.Collapsed;
            ScoreEmpty.Visibility = Visibility.Collapsed;
            HeroDescription.Text = albumMode ? "The album list is still loading. It shows up here as soon as the music is in."
                : settings.ShowTesting
                ? "No catalog entries match this filter."
                : FilterActive ? "Nothing matches this filter. Open the FILTER (next to OPTIONS) and change or RESET it."
                : "Nothing LIVE here yet. Open the FILTER (next to OPTIONS) and show BETAS & ALPHAS to see games still in testing.";
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

        if (IsAlbum(g)) { await ShowAlbumHero(g, cancellationToken); return; }
        if (IsAddCard(g)) { ShowAddHero(g); return; }
        SetTrailerAsEdit(false);
        ScoreHeader.Text = "HIGH SCORES";
        ScoreHeaderIcon.Glyph = "\uE734";
        ScoreEmpty.Text = "NO SCORES YET. BE THE FIRST.";

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
        TrailerButton.Visibility = HasTrailer(g) ? Visibility.Visible : Visibility.Collapsed;
        if (launcherFocus == LauncherFocus.Trailer && !HasTrailer(g)) SetLauncherFocus(LauncherFocus.Play);
        ScoreList.ItemsSource = null;
        ScoreEmpty.Visibility = Visibility.Collapsed;
        try { HeroArt.Source = new BitmapImage(new Uri(g.Art)); } catch { HeroArt.Source = null; }
        QueueTrailerPreview(g);
        if (LocalLibraryService.IsLocal(g)) { ShowLocalHero(g); return; } // no network scores for the player's own games

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
    // A mouse/touch click only picks the game; launching is the PLAY button's job
    // (controller A / Enter on a tile moves focus to the details panel via HandleLauncherAction).
    void GameGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not GameEntry g) return;
        if (!ReferenceEquals(GameGrid.SelectedItem, g)) { GameGrid.SelectedItem = g; audio?.Play("move"); }
        else _ = ShowHero(g);
    }
    async void Play_Click(object sender, RoutedEventArgs e) { if (GameGrid.SelectedItem is GameEntry g) await Launch(g); }
    // double-click (or double-tap) a tile: play it straight away
    async void GameGrid_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        var g = (e.OriginalSource as FrameworkElement)?.DataContext as GameEntry ?? GameGrid.SelectedItem as GameEntry;
        if (g is null || gameOpen) return;
        e.Handled = true;
        if (!ReferenceEquals(GameGrid.SelectedItem, g)) GameGrid.SelectedItem = g;
        await Launch(g);
    }
    static bool HasTrailer(GameEntry g) => g.Trailer && g.Playable && g.Runtime.Type.Equals("web", StringComparison.OrdinalIgnoreCase);
    async void Trailer_Click(object sender, RoutedEventArgs e)
    {
        if (GameGrid.SelectedItem is not GameEntry g) return;
        if (IsLibraryItem(g)) { OpenEditor(g); return; } // TRAILER is EDIT on library items
        await LaunchTrailer(g);
    }
    // A trailer runs on the game itself: same web runtime and URL, plus trailer=1 (the site starts the trailer instead of the game)
    async Task LaunchTrailer(GameEntry g)
    {
        if (!HasTrailer(g)) return;
        EnsureWebRuntime().TrailerNext = true;
        StartupLog.Write("runtime", $"Trailer id={g.Id}");
        await Launch(g);
    }

    async Task Launch(GameEntry g)
    {
        if (IsAddCard(g)) { OpenNewEditor(album: ReferenceEquals(g, AddAlbumCard)); return; }
        if (LocalLibraryService.IsLocal(g)) { LaunchLocal(g); return; }
        if (IsAlbum(g))
        {
            if (music is null) return;
            audio?.Play("select");
            await music.PlayAlbumAsync(AlbumId(g));
            return;
        }
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
            audio?.Play("launch");
            music?.PauseForGame(); // the game brings its own audio; never play under it
            PauseCityLife();
            SetCoverTitle(g.Id, g.Name);
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
        if (gameOpen) music?.ResumeAfterGame();
        gameOpen = false;
        ApplyCityLife();
        active = null;
        launchWatchdog?.Stop();
        GameLayer.Visibility = Visibility.Collapsed;
        SetSignInPanel(false);
        StopStoryboard("CoverPulse");
        GameCover.Visibility = Visibility.Visible;
        Panel.Visibility = Visibility.Collapsed;
        ScoresLayer.Visibility = Visibility.Collapsed;
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
        if (!gameOpen) audio?.Nav(action);
        if (DailyLayer.Visibility == Visibility.Visible)
        {
            if (action == PadAction.A) DailyPrimary();
            else if (action == PadAction.B) CloseDaily();
            return;
        }
        if (FilterLayer.Visibility == Visibility.Visible) { FilterPad(action); return; }
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
        if (localRunning && !windowActive) return; // a library game is in front: the pad is its
        if (AddLayer.Visibility == Visibility.Visible) { HandleEditorAction(action); return; }
        if (ScoresLayer.Visibility == Visibility.Visible) { await HandleScoresAction(action); return; }
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
        StartMusicOnce();
    }

    // The music starts once the launcher itself is up (a beat after it appears), never on the splash / update / sign-in screens.
    Microsoft.UI.Dispatching.DispatcherQueueTimer? musicStartTimer;
    void StartMusicOnce()
    {
        if (musicStartTimer is not null) return;
        musicStartTimer = DispatcherQueue.CreateTimer();
        musicStartTimer.Interval = TimeSpan.FromMilliseconds(1200);
        musicStartTimer.IsRepeating = false;
        musicStartTimer.Tick += (_, _) =>
        {
            if (music is not null && music.Album is null && !music.Loading) _ = music.LoadAsync(autoPlay: !settings.MusicOff && !gameOpen);
            musicRetry?.Start(); // keeps trying if the first load misses (slow network / offline)
        };
        musicStartTimer.Start();
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
                case LauncherFocus.Testing: Testing_Click(this, new RoutedEventArgs()); break;
                case LauncherFocus.Exit: Close(); break;
                case LauncherFocus.Update: UpdateReady_Click(this, new RoutedEventArgs()); break;
                case LauncherFocus.SignIn:
                    if (account is not null) Account_Click(this, new RoutedEventArgs());
                    else SignIn_Click(this, new RoutedEventArgs());
                    break;
                case LauncherFocus.GameGrid:
                    // A on a tile opens its details panel (PLAY, TRAILER...); it never launches straight from the grid
                    if (GameGrid.SelectedItem is GameEntry) { SetLauncherFocus(LauncherFocus.Play); audio?.Play("select"); }
                    break;
                case LauncherFocus.Play:
                    if (GameGrid.SelectedItem is GameEntry game) await Launch(game);
                    break;
                case LauncherFocus.Player: PlayerActivate(); break;
                case LauncherFocus.Trailer:
                    if (GameGrid.SelectedItem is GameEntry tg) { if (IsLibraryItem(tg)) OpenEditor(tg); else await LaunchTrailer(tg); }
                    break;
            }
            return;
        }

        if (launcherFocus == LauncherFocus.Player) { PlayerMove(action); return; }

        if (launcherFocus == LauncherFocus.GameGrid &&
            action is PadAction.Left or PadAction.Right or PadAction.Up or PadAction.Down)
        {
            MoveGameSelection(action);
            return;
        }

        if (launcherFocus == LauncherFocus.Play)
        {
            if (action == PadAction.Right && TrailerButton.Visibility == Visibility.Visible) SetLauncherFocus(LauncherFocus.Trailer);
            else if (action == PadAction.Up || action == PadAction.Left) SetLauncherFocus(LauncherFocus.GameGrid);
            else if (action == PadAction.Down) SetLauncherFocus(LauncherFocus.Player);
            return;
        }
        if (launcherFocus == LauncherFocus.Trailer)
        {
            if (action == PadAction.Left) SetLauncherFocus(LauncherFocus.Play);
            else if (action == PadAction.Up) SetLauncherFocus(LauncherFocus.GameGrid);
            else if (action == PadAction.Down) SetLauncherFocus(LauncherFocus.Player);
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

        if (launcherFocus == LauncherFocus.Update)
        {
            if (action is PadAction.Down or PadAction.Right) SetLauncherFocus(LauncherFocus.SignIn);
            else if (action == PadAction.Left) SetLauncherFocus(LauncherFocus.Categories);
            return;
        }
        if (launcherFocus == LauncherFocus.SignIn)
        {
            if (action == PadAction.Up && UpdateReadyButton.Visibility == Visibility.Visible) SetLauncherFocus(LauncherFocus.Update);
            else if (action == PadAction.Down) SetLauncherFocus(consoleMode ? LauncherFocus.Options : LauncherFocus.Exit);
            else if (action == PadAction.Left) SetLauncherFocus(LauncherFocus.Categories);
            return;
        }

        var menu = consoleMode
            ? new[] { LauncherFocus.Games, LauncherFocus.Scores, LauncherFocus.Storyboards, LauncherFocus.Options, LauncherFocus.Testing }
            : new[] { LauncherFocus.Games, LauncherFocus.Scores, LauncherFocus.Storyboards, LauncherFocus.Options, LauncherFocus.Testing, LauncherFocus.Exit };
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
                if (i / cols == (visible.Count - 1) / cols) { SetLauncherFocus(LauncherFocus.Player); return; }
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
        if (target != LauncherFocus.Player) SetVolumeAdjust(false);
        UpdateTileHighlight();
        UpdateDetailsFocus(target is LauncherFocus.Play or LauncherFocus.Trailer);
        Control control = target switch
        {
            LauncherFocus.Categories => NextCategoryButton,
            LauncherFocus.Games => GamesButton,
            LauncherFocus.Scores => ScoresButton,
            LauncherFocus.Storyboards => StoryboardsButton,
            LauncherFocus.Options => OptionsButton,
            LauncherFocus.Testing => TestingButton,
            LauncherFocus.Exit => ExitButton,
            LauncherFocus.Play => PlayButton,
            LauncherFocus.Update => UpdateReadyButton,
            LauncherFocus.Trailer => TrailerButton,
            LauncherFocus.SignIn => account is not null ? AccountButton : SignInButton,
            LauncherFocus.Player => PlayerControls[Math.Clamp(playerIndex, 0, PlayerControls.Length - 1)],
            _ => GameGrid
        };
        PadFocus(control);
    }

    Button[] OptionsTabButtons => [OptionsTabGames, OptionsTabDisplay, OptionsTabThemes, OptionsTabSystem, OptionsTabUpdates, OptionsTabAbout];

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
                    controls.Add(TrailerPreviewToggle);
                    controls.Add(AddCardToggle);
                    controls.Add(RefreshCatalogButton);
                    break;
                case 1:
                    controls.Add(ScreenCombo);
                    controls.Add(LaunchScreenToggle);
                    controls.Add(DisplayModeCombo);
                    controls.Add(QualityCombo);
                    controls.Add(ScanlinesToggle);
                    controls.Add(CityLifeToggle);
                    controls.Add(GpuToggle);
                    break;
                case ThemesTab:
                    controls.AddRange(themeCards);
                    break;
                case 3:
                    controls.Add(MusicToggle);
                    controls.Add(SfxToggle);
                    controls.Add(StartWithWindowsToggle);
                    if (ConsoleModeButton.IsEnabled) controls.Add(ConsoleModeButton);
                    break;
                case 4:
                    if (CheckUpdateButton.IsEnabled) controls.Add(CheckUpdateButton);
                    if (InstallUpdateButton.IsEnabled) controls.Add(InstallUpdateButton);
                    break;
                case 5:
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

    Button[]? SideBySide(Button b)
    {
        Button[][] rows = [[CheckUpdateButton, InstallUpdateButton], [OptionsTermsButton, OptionsPrivacyButton]];
        return rows.FirstOrDefault(r => r.Contains(b));
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
        // buttons laid out side by side (CHECK FOR UPDATES | INSTALL, TERMS | PRIVACY): left/right moves between them
        if (action is PadAction.Left or PadAction.Right && current is Button side && SideBySide(side) is { } row)
        {
            var i = Array.IndexOf(row, side) + (action == PadAction.Left ? -1 : 1);
            while (i >= 0 && i < row.Length && controls.IndexOf(row[i]) < 0) i += action == PadAction.Left ? -1 : 1; // skip disabled ones
            if (i >= 0 && i < row.Length)
            {
                panelFocus = controls.IndexOf(row[i]);
                FocusPanelControl(row[i]);
                audio?.Play("move");
            }
            return;
        }
        // theme cards: a 3x2 grid, d-pad moves in 2D and A applies the theme
        if (options && optionsTab == ThemesTab && HandleThemeGrid(action, current, controls)) return;
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
            else if (control == MusicToggle) MusicToggle.IsOn = !MusicToggle.IsOn;
            else if (control == SfxToggle) SfxToggle.IsOn = !SfxToggle.IsOn;
            else if (control == ScanlinesToggle) ScanlinesToggle.IsOn = !ScanlinesToggle.IsOn;
            else if (control == LaunchScreenToggle) LaunchScreenToggle.IsOn = !LaunchScreenToggle.IsOn;
            else if (control == CityLifeToggle) CityLifeToggle.IsOn = !CityLifeToggle.IsOn;
            else if (control == TrailerPreviewToggle) TrailerPreviewToggle.IsOn = !TrailerPreviewToggle.IsOn;
            else if (control == AddCardToggle) AddCardToggle.IsOn = !AddCardToggle.IsOn;
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
        MusicRow.BorderBrush = control == MusicToggle ? cyan : line;
        SfxRow.BorderBrush = control == SfxToggle ? cyan : line;
        ScreenRow.BorderBrush = control == ScreenCombo ? cyan : line;
        LaunchScreenRow.BorderBrush = control == LaunchScreenToggle ? cyan : line;
        DisplayModeRow.BorderBrush = control == DisplayModeCombo ? cyan : line;
        QualityRow.BorderBrush = control == QualityCombo ? cyan : line;
        ScanlinesRow.BorderBrush = control == ScanlinesToggle ? cyan : line;
        CityLifeRow.BorderBrush = control == CityLifeToggle ? cyan : line;
        TrailerPreviewRow.BorderBrush = control == TrailerPreviewToggle ? cyan : line;
        AddCardRow.BorderBrush = control == AddCardToggle ? cyan : line;
        GpuRow.BorderBrush = control == GpuToggle ? cyan : line;
    }

    void SelectOptionsTab(int tab, bool keepFocusOnTabs = true)
    {
        optionsTab = Math.Clamp(tab, 0, OptionsTabCount - 1);
        if (optionsTab == 1) RefreshScreens();
        OptionsGamesTab.Visibility = optionsTab == 0 ? Visibility.Visible : Visibility.Collapsed;
        OptionsDisplayTab.Visibility = optionsTab == 1 ? Visibility.Visible : Visibility.Collapsed;
        OptionsThemesTab.Visibility = optionsTab == ThemesTab ? Visibility.Visible : Visibility.Collapsed;
        OptionsSystemTab.Visibility = optionsTab == 3 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCard.Visibility = optionsTab == 4 ? Visibility.Visible : Visibility.Collapsed;
        OptionsAboutTab.Visibility = optionsTab == 5 ? Visibility.Visible : Visibility.Collapsed;
        OptionsLegal.Visibility = optionsTab == 5 ? Visibility.Visible : Visibility.Collapsed;
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
        if (!ThemeService.Current.IsNeon) ThemeWalk(); // tab highlight brushes are made here: theme them before they paint
    }

    void OptionsTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var tab)) SelectOptionsTab(tab);
    }

    void ChangeCategory(int delta)
    {
        if (categories.Count == 0) return;
        if (storyMode || albumMode) { storyMode = false; albumMode = false; ApplyFilter(); return; } // LB/RB from STORYBOARDS / ALBUMS goes back to the games
        categoryIndex = (categoryIndex + delta + categories.Count) % categories.Count;
        ApplyFilter();
    }

    void PreviousCategory_Click(object sender, RoutedEventArgs e) => ChangeCategory(-1);
    void NextCategory_Click(object sender, RoutedEventArgs e) => ChangeCategory(1);

    void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Alt+Enter = toggle fullscreen / windowed (anywhere except the floating sign-in card)
        if (!floating && e.Key == VirtualKey.Enter &&
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        { ToggleWindowed(); e.Handled = true; return; }
        if (consoleMode) { e.Handled = true; return; }
        if (AddLayer.Visibility == Visibility.Visible && !gameOpen)
        {
            // the add/edit form takes typing: only Escape is ours (closes the emulator form, then the editor)
            if (e.Key == VirtualKey.Escape) { HandleEditorAction(PadAction.B); e.Handled = true; }
            return;
        }
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
        if (FilterLayer.Visibility == Visibility.Visible)
        {
            PadAction? a = e.Key switch
            {
                VirtualKey.Up => PadAction.Up, VirtualKey.Down => PadAction.Down, VirtualKey.Left => PadAction.Left, VirtualKey.Right => PadAction.Right,
                VirtualKey.Enter or VirtualKey.Space => PadAction.A, VirtualKey.Escape => PadAction.B, _ => null
            };
            if (a is { } act) { FilterPad(act); e.Handled = true; }
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
                audio?.Nav(action.Value);
                if (startupOpen && WelcomePanel.Visibility == Visibility.Visible)
                    HandleStartupAction(action.Value);
                else
                    _ = ScoresLayer.Visibility == Visibility.Visible ? HandleScoresAction(action.Value)
                        : Panel.Visibility == Visibility.Visible ? HandlePanelAction(action.Value)
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
        audio?.Play("open");
        SelectOptionsTab(optionsTab, keepFocusOnTabs: true);
    }
    void Games_Click(object sender, RoutedEventArgs e) => ShowSection(false);

    void Storyboards_Click(object sender, RoutedEventArgs e) => ShowSection(true);

    void ShowSection(bool storyboards)
    {
        var changed = storyMode != storyboards || albumMode;
        storyMode = storyboards;
        albumMode = false;
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
        audio?.Play("open");
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
            ShowUpdateReady();
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
    void Testing_Click(object sender, RoutedEventArgs e) => OpenFilter();

    void UpdateTestingButton()
    {
        TestingLabel.Text = FilterSummary();
        TestingIcon.Glyph = "\uE71C";
        if (settings.ShowTesting || FilterActive) TestingButton.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgMagenta"];
        else TestingButton.ClearValue(Control.BorderBrushProperty);
    }

    void ShowAllToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (initializingSettings) return;
        settings.ShowAll = ShowAllToggle.IsOn;
        settings.Save();
        BuildCategories();
        ApplyFilter();
    }

    void MusicToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (initializingSettings) return;
        settings.MusicOff = !MusicToggle.IsOn;
        settings.Save();
    }

    void SfxToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (initializingSettings) return;
        settings.SfxOff = !SfxToggle.IsOn;
        settings.Save();
        if (audio is not null) audio.SfxOn = SfxToggle.IsOn;
        audio?.Play("select");
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
        // give XAML one frame to lay out the launcher / sign-in card before it appears. The timer lives in a field:
        // a local DispatcherQueueTimer can be garbage collected before it fires, which left the window cloaked behind
        // the splash logo forever (seen on the first start after an update).
        revealTimer = DispatcherQueue.CreateTimer();
        revealTimer.Interval = TimeSpan.FromMilliseconds(60);
        revealTimer.IsRepeating = false;
        revealTimer.Tick += (_, _) => FinishReveal();
        revealTimer.Start();
        // belt and braces: if that tick still never comes, uncloak anyway
        revealBackstop = DispatcherQueue.CreateTimer();
        revealBackstop.Interval = TimeSpan.FromSeconds(2);
        revealBackstop.IsRepeating = false;
        revealBackstop.Tick += (_, _) => { if (!revealDone) { StartupLog.Write("window", "Reveal backstop fired"); FinishReveal(); } };
        revealBackstop.Start();
    }

    Microsoft.UI.Dispatching.DispatcherQueueTimer? revealTimer, revealBackstop;
    bool revealDone;
    void FinishReveal()
    {
        if (revealDone) return;
        revealDone = true;
        try { if (cloaked) SetCloaked(false); Activate(); }
        catch (Exception ex) { StartupLog.Exception("Reveal", ex); }
        DispatcherQueue.TryEnqueue(() => EnforcePresenter("reveal"));
        if (SplashWindow.IsOpen) SplashWindow.FadeOutAndClose(DispatcherQueue);
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // ---- signed-in account (header chip + in-app account panel) ----
    void SetAccount(NetworkAccount? a)
    {
        account = a is null || string.IsNullOrWhiteSpace(a.Name) && string.IsNullOrWhiteSpace(a.Username) ? null : a;
        var signedIn = account is not null;
        // Signing in or out can change the game list (owner-only games), so re-check the catalog.
        if (signedIn != catalogSignedIn) { catalogSignedIn = signedIn; if (all.Count > 0) _ = RefreshCatalog(true); }
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
        audio?.Play("open");
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

    // the splash logo was dragged: put the floating card (or the arcade) where it was dropped
    void OnSplashDropped(int x, int y)
    {
        try
        {
            screens = DisplayService.Detect();
            var screen = screens.FirstOrDefault(s => s.Contains(x, y));
            if (screen is null) return;
            if (settings.Display != screen.Key) { settings.Display = screen.Key; settings.Save(); }
            var appWindow = MainAppWindow();
            if (floating)
            {
                int w = appWindow.Size.Width, h = appWindow.Size.Height;
                int cx = Math.Clamp(x - w / 2, screen.WorkX, Math.Max(screen.WorkX, screen.WorkX + screen.WorkWidth - w));
                int cy = Math.Clamp(y - h / 2, screen.WorkY, Math.Max(screen.WorkY, screen.WorkY + screen.WorkHeight - h));
                appWindow.Move(new Windows.Graphics.PointInt32(cx, cy));
            }
            else if (presenterApplied && ScreenOfWindow(appWindow)?.Key != screen.Key) ApplyMainPresenter(screen);
        }
        catch (Exception ex) { StartupLog.Exception("Splash dropped", ex); }
    }

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
            UpdateWindowBar();
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
    bool presenterApplied, applyingPresenter, dragging, initializingDisplay;
    List<ScreenInfo> screens = [];

    AppWindow MainAppWindow() =>
        AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this)));

    bool launchScreenUsed;

    ScreenInfo? SavedScreen()
    {
        if (screens.Count == 0) screens = DisplayService.Detect();
        // first time at startup: open on the screen the app was launched from (taskbar icon / Start / shortcut)
        if (!launchScreenUsed)
        {
            launchScreenUsed = true;
            if (!settings.PinScreen && Program.LaunchPoint is { } lp && screens.FirstOrDefault(s => s.Contains(lp.X, lp.Y)) is { } launched)
            {
                StartupLog.Write("display", $"Opening on the launch screen {launched.Label}");
                if (settings.Display != launched.Key) { settings.Display = launched.Key; settings.Save(); }
                return launched;
            }
        }
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
        applyingPresenter = true;
        try
        {
            var appWindow = MainAppWindow();
            target ??= SavedScreen() ?? ScreenOfWindow(appWindow);
            if (target is not null)
            {
                // move while overlapped: a fullscreen window can't be moved, it takes the screen it is on. Use a borderless,
                // un-maximized window for the move: going fullscreen straight from a maximized window with a system title bar
                // could leave its minimize/maximize/close buttons drawn over the fullscreen arcade.
                if (appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized or OverlappedPresenterState.Minimized } op) op.Restore();
                if (appWindow.Presenter.Kind != AppWindowPresenterKind.Overlapped || !settings.Windowed) appWindow.SetPresenter(BorderlessPresenter());
                int w = settings.Windowed ? target.WorkWidth * 3 / 4 : Math.Min(target.WorkWidth, 1280);
                int h = settings.Windowed ? target.WorkHeight * 3 / 4 : Math.Min(target.WorkHeight, 720);
                // Screens with different scaling (laptop 150% vs TV 300%): crossing onto the other screen makes Windows rescale
                // the window, which threw off the size we set and left the arcade spread over two screens. So hop a small
                // window onto the target screen first (that's where the rescale happens), then size it for real.
                if (!target.Contains(appWindow.Position.X + appWindow.Size.Width / 2, appWindow.Position.Y + appWindow.Size.Height / 2))
                    appWindow.MoveAndResize(new Windows.Graphics.RectInt32(target.WorkX + target.WorkWidth / 2 - 160, target.WorkY + target.WorkHeight / 2 - 90, 320, 180));
                appWindow.MoveAndResize(new Windows.Graphics.RectInt32(target.WorkX + (target.WorkWidth - w) / 2, target.WorkY + (target.WorkHeight - h) / 2, w, h));
            }
            if (settings.Windowed)
            {
                var presenter = OverlappedPresenter.Create();
                presenter.IsResizable = true;
                presenter.IsMaximizable = true;
                presenter.IsMinimizable = true;
                presenter.SetBorderAndTitleBar(true, false); // resizable frame, no system title bar: the app draws its own (WindowBar)
                appWindow.SetPresenter(presenter);
            }
            else
            {
                // Borderless window covering the whole screen ("borderless fullscreen", like most PC games) instead of the
                // system FullScreen presenter: Windows' DPI rescale (laptop 150% vs TV 300%) kept shrinking that one to ~75%
                // of the screen. This one is snapped back to the full screen whenever anything resizes or moves it.
                if (appWindow.Presenter is not OverlappedPresenter { HasTitleBar: false, HasBorder: false }) appWindow.SetPresenter(BorderlessPresenter());
                var full = target ?? ScreenOfWindow(appWindow);
                if (full is not null) appWindow.MoveAndResize(new Windows.Graphics.RectInt32(full.X, full.Y, full.Width, full.Height));
            }
            UpdateWindowBar();
            if (target is not null) VerifyPlacementSoon(target);
            if (target is not null && settings.Display != target.Key) { settings.Display = target.Key; settings.Save(); }
            StartupLog.Write("window", $"{(settings.Windowed ? "Windowed" : "Fullscreen")} on {target?.Label ?? "current screen"}");
            // refresh the dropdown after this event finishes (never swap its items inside its own SelectionChanged)
            if (OptionsDisplayTab.Visibility == Visibility.Visible) DispatcherQueue.TryEnqueue(RefreshScreens);
        }
        catch (Exception ex) { StartupLog.Exception("Apply presenter", ex); }
        finally { DispatcherQueue.TryEnqueue(() => applyingPresenter = false); }
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

    // A late rescale from Windows can still land after the presenter switch; check the result and redo it once if it's off.
    Microsoft.UI.Dispatching.DispatcherQueueTimer? placementTimer;
    int placementRetries;
    void VerifyPlacementSoon(ScreenInfo target)
    {
        placementTimer?.Stop();
        placementTimer = DispatcherQueue.CreateTimer();
        placementTimer.Interval = TimeSpan.FromMilliseconds(450);
        placementTimer.IsRepeating = false;
        placementTimer.Tick += (_, _) =>
        {
            try
            {
                if (floating || dragging || applyingPresenter) return;
                var aw = MainAppWindow();
                int l = aw.Position.X, t = aw.Position.Y, r = l + aw.Size.Width, b = t + aw.Size.Height;
                bool ok;
                if (settings.Windowed)
                {
                    // a normal window may be maximized or dragged freely afterwards; only fix one that spills off this screen
                    var cx = l + aw.Size.Width / 2; var cy = t + aw.Size.Height / 2;
                    ok = !target.Contains(cx, cy) || (l >= target.X - 16 && t >= target.Y - 16 && r <= target.X + target.Width + 16 && b <= target.Y + target.Height + 16);
                }
                else ok = Math.Abs(l - target.X) <= 2 && Math.Abs(t - target.Y) <= 2 && Math.Abs(r - (target.X + target.Width)) <= 2 && Math.Abs(b - (target.Y + target.Height)) <= 2;
                if (ok) { placementRetries = 0; return; }
                StartupLog.Write("window", $"Placement off after switch: window {l},{t},{r - l}x{b - t} vs {target.Label}; retry {placementRetries + 1}");
                if (++placementRetries > 2) { placementRetries = 0; return; }
                ApplyMainPresenter(target);
            }
            catch (Exception ex) { StartupLog.Exception("Verify placement", ex); }
        };
        placementTimer.Start();
    }

    /// <summary>Fullscreen mode: make the window cover exactly the screen it's on.</summary>
    void SnapFullscreen()
    {
        if (settings.Windowed || floating || dragging || applyingPresenter || !presenterApplied) return;
        try
        {
            var aw = MainAppWindow();
            if (aw.Presenter is not OverlappedPresenter { HasTitleBar: false, HasBorder: false } op || op.State == OverlappedPresenterState.Minimized) return;
            int cx = aw.Position.X + aw.Size.Width / 2, cy = aw.Position.Y + aw.Size.Height / 2;
            var list = DisplayService.Detect(log: false);
            var s = list.FirstOrDefault(x => x.Contains(cx, cy)) ?? list.FirstOrDefault(x => x.Primary) ?? list.FirstOrDefault();
            if (s is null) return;
            if (aw.Position.X == s.X && aw.Position.Y == s.Y && aw.Size.Width == s.Width && aw.Size.Height == s.Height) return;
            StartupLog.Write("window", $"Fullscreen snap: {aw.Position.X},{aw.Position.Y} {aw.Size.Width}x{aw.Size.Height} -> {s.Label}");
            aw.MoveAndResize(new Windows.Graphics.RectInt32(s.X, s.Y, s.Width, s.Height));
        }
        catch (Exception ex) { StartupLog.Exception("Fullscreen snap", ex); }
    }

    static OverlappedPresenter BorderlessPresenter()
    {
        var p = OverlappedPresenter.Create();
        p.IsResizable = false;
        p.IsMaximizable = false;
        p.SetBorderAndTitleBar(false, false);
        return p;
    }

    // ---- the app's own title bar (windowed mode only) ----
    void UpdateWindowBar()
    {
        try
        {
            var show = settings.Windowed && !floating && !dragging;
            WindowBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;
            var max = MainAppWindow().Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
            WinMaxGlyph.Glyph = max ? "\uE923" : "\uE922";
            ToolTipService.SetToolTip(WinMaxButton, max ? "Restore" : "Maximize");
        }
        catch (Exception ex) { StartupLog.Exception("Window bar", ex); }
    }

    DateTime lastBarPress;
    void WindowBar_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(WindowBarDrag).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        var now = DateTime.UtcNow;
        if ((now - lastBarPress).TotalMilliseconds < 450) { lastBarPress = default; ToggleMaximize(); return; }
        lastBarPress = now;
        if (MainAppWindow().Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized }) return; // like Windows: restore first
        BeginNativeMove(false); // Windows drags it (snap layouts and moving to the TV still work)
    }

    void ToggleMaximize()
    {
        if (MainAppWindow().Presenter is not OverlappedPresenter op) return;
        if (op.State == OverlappedPresenterState.Maximized) op.Restore(); else op.Maximize();
        UpdateWindowBar();
    }

    void WinMin_Click(object sender, RoutedEventArgs e) { if (MainAppWindow().Presenter is OverlappedPresenter op) op.Minimize(); }
    void WinMax_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    void WinFull_Click(object sender, RoutedEventArgs e) { if (settings.Windowed) ToggleWindowed(); }

    /// <summary>Makes the window match the saved mode: FULLSCREEN = true fullscreen, no title bar; WINDOWED = normal window.</summary>
    void EnforcePresenter(string why)
    {
        if (applyingPresenter || floating || dragging || !presenterApplied) return;
        try
        {
            var kind = MainAppWindow().Presenter.Kind;
            var aw0 = MainAppWindow();
            bool ok = settings.Windowed
                ? kind == AppWindowPresenterKind.Overlapped && aw0.Presenter is OverlappedPresenter { HasTitleBar: false, HasBorder: true }
                : aw0.Presenter is OverlappedPresenter { HasTitleBar: false, HasBorder: false };
            if (ok && !settings.Windowed) { SnapFullscreen(); return; }
            if (ok) return;
            StartupLog.Write("window", $"{(settings.Windowed ? "Windowed" : "Fullscreen")} setting but presenter is {kind} ({why}); fixing");
            ApplyMainPresenter(ScreenOfWindow(MainAppWindow()));
        }
        catch (Exception ex) { StartupLog.Exception("Enforce presenter", ex); }
    }

    DateTime lastToggle;
    void ToggleWindowed()
    {
        // the accelerator and the key handler can both see one Alt+Enter: toggle once
        if ((DateTime.UtcNow - lastToggle).TotalMilliseconds < 500) return;
        lastToggle = DateTime.UtcNow;
        settings.Windowed = !settings.Windowed;
        settings.Save();
        StartupLog.Write("window", "Alt+Enter toggle -> " + (settings.Windowed ? "Windowed" : "Fullscreen"));
        ApplyMainPresenter(ScreenOfWindow(MainAppWindow()));
        initializingDisplay = true;
        try { DisplayModeCombo.SelectedIndex = settings.Windowed ? 1 : 0; } finally { initializingDisplay = false; }
    }

    void DisplayModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializingDisplay || initializingSettings || DisplayModeCombo.SelectedIndex < 0) return;
        var windowed = DisplayModeCombo.SelectedIndex == 1;
        // always re-apply, even if the saved setting already matches: the window may not (that's how a title bar
        // could get stuck while OPTIONS still said FULLSCREEN)
        if (windowed != settings.Windowed) { settings.Windowed = windowed; settings.Save(); }
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

    void LaunchScreenToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (initializingSettings) return;
        settings.PinScreen = !LaunchScreenToggle.IsOn;
        settings.Save();
        DisplayStatus.Text = settings.PinScreen ? "ALWAYS OPENS ON THE SCREEN PICKED ABOVE." : "OPENS ON WHICHEVER SCREEN YOU LAUNCH IT FROM.";
    }

    void ScanlinesToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (initializingSettings) return;
        settings.HideScanlines = !ScanlinesToggle.IsOn;
        settings.Save();
        ApplyScanlines();
    }

    void CityLifeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (initializingSettings) return;
        settings.StillBackground = !CityLifeToggle.IsOn;
        settings.Save();
        ApplyCityLife();
    }

    // The skyline animations only touch Opacity and transforms, so they run on the
    // compositor thread and cost the UI thread nothing. Paused while a game is up.
    bool cityLifeRunning;
    Microsoft.UI.Xaml.Media.Animation.Storyboard? CityLifeStory =>
        CityLife.Resources.TryGetValue("CityLifeStory", out var sb) ? sb as Microsoft.UI.Xaml.Media.Animation.Storyboard : null;

    void ApplyCityLife()
    {
        try
        {
            ApplyThemeSceneMotion();
            var sb = CityLifeStory;
            if (sb is null) return;
            if (!ThemeService.Current.IsNeon)
            {
                // other themes hide the hall: stop its city animation entirely
                sb.Stop(); cityLifeRunning = false; CityLife.Visibility = Visibility.Collapsed;
                return;
            }
            if (settings.StillBackground || gameOpen)
            {
                if (settings.StillBackground) { sb.Stop(); cityLifeRunning = false; CityLife.Visibility = Visibility.Collapsed; }
                else sb.Pause();
                return;
            }
            CityLife.Visibility = Visibility.Visible;
            if (!cityLifeRunning) { sb.Begin(); cityLifeRunning = true; }
            else sb.Resume();
        }
        catch (Exception ex) { StartupLog.Exception("CityLife", ex); }
    }

    void PauseCityLife()
    {
        try { if (cityLifeRunning) CityLifeStory?.Pause(); } catch { }
        try { if (themeSceneRunning) themeScene?.Story.Pause(); } catch { }
        StopTrailerPreview(); // a game is starting: free the preview engine
    }

    void ApplyScanlines()
    {
        var on = !settings.HideScanlines && ThemeService.Current.Scanlines;
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
        if (DailyLayer.Visibility == Visibility.Visible || FilterLayer.Visibility == Visibility.Visible || updatePromptOpen && !floating) return; // fullscreen popups: clicks never drag the window
        var point = e.GetCurrentPoint(Root);
        if (!point.Properties.IsLeftButtonPressed) return;
        if (floating)
        {
            // floating sign-in / email / update card: drag it from any empty spot (buttons and text boxes keep their clicks)
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
            audio?.Play("error");
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
        audio?.Play("error");
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
        audio?.Play("jingle");
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
        // in a game: wheel input that landed on the host instead of the WebView goes to the page (games zoom with it)
        if (gameOpen && web is not null && GameLayer.Visibility == Visibility.Visible)
        {
            var gp = e.GetCurrentPoint(WebViewHost);
            if (gp.Properties.MouseWheelDelta != 0) { web.PostHostWheel(gp.Properties.MouseWheelDelta, gp.Position.X, gp.Position.Y); e.Handled = true; }
            return;
        }
        // the grid's own ScrollViewer already took it: don't scroll twice
        if (e.Handled && e.OriginalSource is DependencyObject src && FindAncestor<ScrollViewer>(src) is { } sv && ReferenceEquals(sv, gridScroller ??= FindChild<ScrollViewer>(GameGrid))) return;
        if (AddLayer.Visibility == Visibility.Visible && !gameOpen)
        {
            if (!e.Handled) AddScroll.ChangeView(null, AddScroll.VerticalOffset - e.GetCurrentPoint(Root).Properties.MouseWheelDelta, null);
            e.Handled = true;
            return;
        }
        if (ScoresLayer.Visibility == Visibility.Visible && !gameOpen) { ScoresWheel(e); return; }
        if (Panel.Visibility == Visibility.Visible && !gameOpen)
        {
            // options and other panels: the wheel scrolls the panel (tall tabs on small/scaled screens)
            var d = e.GetCurrentPoint(Root).Properties.MouseWheelDelta;
            if (d != 0 && PanelScroll.ScrollableHeight > 1)
            {
                PanelScroll.ChangeView(null, Math.Clamp(PanelScroll.VerticalOffset - d, 0, PanelScroll.ScrollableHeight), null, false);
                e.Handled = true;
            }
            return;
        }
        if (Launcher.Visibility != Visibility.Visible || Panel.Visibility == Visibility.Visible || gameOpen || DailyLayer.Visibility == Visibility.Visible || FilterLayer.Visibility == Visibility.Visible) return;
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
            ShowUpdateReady();
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
        var lit = launcherFocus is LauncherFocus.GameGrid or LauncherFocus.Play or LauncherFocus.Trailer;
        VisualStateManager.GoToState(tile, lit ? "Selected" : "Normal", true);
    }

    // The loading cover shows the game's name. 2DISO SHIFT styles its last word in italics, as the game's own logo does.
    private void SetCoverTitle(string id, string name)
    {
        name ??= "";
        const string word = "SHIFT";
        var at = id == "2diso-shift" ? name.IndexOf(word, System.StringComparison.Ordinal) : -1;
        if (at < 0) { GameCoverTitle.Text = name; return; }
        GameCoverTitle.Inlines.Clear();
        if (at > 0) GameCoverTitle.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = name.Substring(0, at) });
        GameCoverTitle.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = word, FontStyle = Windows.UI.Text.FontStyle.Italic });
        if (at + word.Length < name.Length) GameCoverTitle.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = name.Substring(at + word.Length) });
    }

    // ======== music player (bottom bar) ========
    int playerIndex = 1;          // 0 prev, 1 play, 2 next, 3 album, 4 shuffle, 5 repeat, 6 mute, 7 volume
    bool volumeAdjust;            // A on the volume slider: left/right change volume until A/B
    bool syncingVolume;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? meterTimer;

    Microsoft.UI.Dispatching.DispatcherQueueTimer? musicRetry;

    Control[] PlayerControls => [MusicPrevButton, MusicPlayButton, MusicNextButton, MusicAlbumButton, MusicShuffleButton, MusicRepeatButton, MusicMuteButton, MusicVolumeSlider];

    void PlayerMove(PadAction action)
    {
        if (volumeAdjust)
        {
            if (action is PadAction.Left or PadAction.Right) { music?.SetVolume((music?.Volume ?? 0.6) + (action == PadAction.Right ? 0.05 : -0.05)); return; }
            if (action is PadAction.B or PadAction.Up or PadAction.Down) { SetVolumeAdjust(false); if (action != PadAction.Up) return; }
        }
        switch (action)
        {
            case PadAction.Up: SetLauncherFocus(LauncherFocus.GameGrid); break;
            case PadAction.Left: if (playerIndex > 0) { playerIndex--; SetLauncherFocus(LauncherFocus.Player); } break;
            case PadAction.Right: if (playerIndex < PlayerControls.Length - 1) { playerIndex++; SetLauncherFocus(LauncherFocus.Player); } break;
        }
    }

    void PlayerActivate()
    {
        switch (playerIndex)
        {
            case 0: music?.Previous(); break;
            case 1: music?.TogglePlay(); break;
            case 2: music?.Next(); break;
            case 3: ToggleAlbumsView(fromPad: true); break;
            case 4: music?.ToggleShuffle(); break;
            case 5: music?.CycleRepeat(); break;
            case 6: music?.ToggleMute(); break;
            case 7: SetVolumeAdjust(!volumeAdjust); break;
        }
    }

    void SetVolumeAdjust(bool on)
    {
        volumeAdjust = on;
        MusicVolumeFrame.BorderBrush = on ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgCyan"] : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    void MusicPrev_Click(object sender, RoutedEventArgs e) => music?.Previous();
    void MusicPlay_Click(object sender, RoutedEventArgs e) => music?.TogglePlay();
    void MusicNext_Click(object sender, RoutedEventArgs e) => music?.Next();
    void MusicAlbumSwitch_Click(object sender, RoutedEventArgs e) => ToggleAlbumsView(fromPad: false);

    /// <summary>ALBUM button: show BDC's albums in the game browser (press again, GAMES, or LB/RB to go back).</summary>
    void ToggleAlbumsView(bool fromPad)
    {
        albumMode = !albumMode;
        if (albumMode) storyMode = false;
        if (Launcher.Visibility != Visibility.Visible) ReturnToLauncher();
        if (albumEntries.Count == 0) BuildAlbumEntries();
        if (music is not null && music.Album is null && !music.Loading) _ = music.LoadAsync(autoPlay: false);
        audio?.Play(albumMode ? "open" : "back");
        ApplyFilter();
        if (fromPad && albumMode) SetLauncherFocus(LauncherFocus.GameGrid);
    }

    void BuildAlbumEntries()
    {
        albumEntries.Clear();
        if (music is null) return;
        foreach (var r in music.AlbumRefs)
        {
            if (string.IsNullOrEmpty(r.Title)) continue; // only the fallback entry (album list not loaded yet)
            // the tile's corner chip (Status) shows the album's genre; the line under the title says what it is
            var genre = string.Join("  ·  ", new[] { "ALBUM", r.Year > 0 ? r.Year.ToString() : "", r.Artist }.Where(s => !string.IsNullOrEmpty(s)));
            albumEntries.Add(new GameEntry
            {
                Id = "album:" + r.Id, Name = r.Title, Category = "ALBUM", Genre = genre, Status = string.IsNullOrWhiteSpace(r.Genre) ? "ALBUM" : r.Genre.ToUpperInvariant(),
                Description = r.Description, Art = r.Cover, Playable = true, Tone = "magenta",
                Runtime = new RuntimeSpec { Type = AlbumRuntime }
            });
        }
    }

    GameEntry? shownAlbum;
    Album? shownAlbumData;

    async Task ShowAlbumHero(GameEntry g, CancellationToken ct)
    {
        shownAlbum = g;
        shownAlbumData = null;
        HeroName.Text = g.Name;
        HeroDescription.Text = g.Description;
        // the player's own album folders get EDIT where a trailer would be
        SetTrailerAsEdit(IsLocalAlbum(g));
        TrailerButton.Visibility = IsLocalAlbum(g) ? Visibility.Visible : Visibility.Collapsed;
        if (launcherFocus == LauncherFocus.Trailer && !IsLocalAlbum(g)) SetLauncherFocus(LauncherFocus.Play);
        QueueTrailerPreview(g); // albums have no trailer: this just stops any running preview
        ScoreHeader.Text = "TRACKS";
        ScoreHeaderIcon.Glyph = "\uE8D6";
        ScoreList.ItemsSource = null;
        ScoreEmpty.Text = "LOADING TRACKS...";
        ScoreEmpty.Visibility = Visibility.Visible;
        HeroMeta.Text = g.Genre.ToUpperInvariant();
        try { HeroArt.Source = string.IsNullOrEmpty(g.Art) ? null : new BitmapImage(new Uri(g.Art)); } catch { HeroArt.Source = null; }
        UpdateAlbumHeroState();
        if (music is null) return;
        var a = await music.GetAlbumAsync(AlbumId(g));
        if (ct.IsCancellationRequested || !ReferenceEquals(shownAlbum, g)) return;
        shownAlbumData = a;
        if (a is null) { ScoreEmpty.Text = "COULDN'T LOAD THE TRACK LIST."; return; }
        var total = TimeSpan.FromSeconds(a.Tracks.Sum(t => t.Duration));
        HeroMeta.Text = $"{g.Genre.ToUpperInvariant()}  ·  {a.Tracks.Count} TRACKS  ·  {(int)total.TotalMinutes} MIN";
        UpdateAlbumHeroState();
    }

    /// <summary>Refresh the album details (now-playing chip, PLAY label, playing track) without reloading art.</summary>
    void UpdateAlbumHeroState()
    {
        if (music is null || shownAlbum is null || !albumMode || !ReferenceEquals(GameGrid.SelectedItem, shownAlbum)) return;
        var id = AlbumId(shownAlbum);
        var current = music.Album?.Id == id;
        var playing = current && music.IsPlaying;
        HeroStatus.Text = playing ? "NOW PLAYING" : current ? "PAUSED" : shownAlbum.Status;
        var brush = playing ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgMagenta"] : UiTheme.StatusBrush("album");
        HeroStatus.Foreground = brush;
        HeroStatusChip.BorderBrush = brush;
        HeroStatusChip.Visibility = Visibility.Visible;
        PlayButton.IsEnabled = true; // pressing it while the album plays is a no-op; keeps pad focus stable
        PlayLabel.Text = playing ? "NOW PLAYING" : current ? "RESUME" : "PLAY ALBUM";
        if (shownAlbumData is { } a)
        {
            var now = current ? music.Current?.Number ?? -1 : -1;
            ScoreList.ItemsSource = a.Tracks.Select(t => new ScoreRow(t.Number.ToString("00"), t.Title,
                TimeSpan.FromSeconds(t.Duration).ToString(t.Duration >= 3600 ? @"h\:mm\:ss" : @"m\:ss"),
                t.Number == now ? "PLAYING" : "")).ToList();
            ScoreEmpty.Visibility = Visibility.Collapsed;
        }
    }
    void MusicShuffle_Click(object sender, RoutedEventArgs e) => music?.ToggleShuffle();
    void MusicRepeat_Click(object sender, RoutedEventArgs e) => music?.CycleRepeat();
    void MusicMute_Click(object sender, RoutedEventArgs e) => music?.ToggleMute();

    void MusicVolume_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (syncingVolume || music is null) return;
        music.SetVolume(e.NewValue / 100.0);
    }

    void MusicSeek_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var x = e.GetPosition(MusicSeek).X;
        if (MusicSeek.ActualWidth > 0) music?.Seek(x / MusicSeek.ActualWidth);
    }

    void UpdateMusicUi()
    {
        if (music is null) return;
        UpdateAlbumHeroState();
        var t = music.Current;
        var cyan = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgCyan"];
        var white = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgText"];
        var dim = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgDim"];
        if (t is not null && music.Album is not null)
        {
            MusicTitle.Text = $"{t.Number:00}  ·  {t.Title.ToUpperInvariant()}";
            MusicAlbum.Text = string.Join("  ·  ", new[] { music.Album.Title, music.Album.Subtitle.ToUpperInvariant(), music.Album.Artist.ToUpperInvariant() }.Where(s => s.Length > 0));
            ToolTipService.SetToolTip(MusicAlbumButton, music.AlbumCount > 1 ? $"Switch album ({music.AlbumCount} albums)" : "Only one album");
        }
        else if (music.Album is null)
        {
            MusicTitle.Text = music.Loading || music.LoadAttempts == 0 ? "LOADING MUSIC..." : "MUSIC OFFLINE  ·  RETRYING";
            ToolTipService.SetToolTip(MusicAlbumButton, "Load music");
        }
        MusicPlayIcon.Glyph = music.IsPlaying ? "" : "";
        var magenta = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgMagenta"];
        MusicShuffleIcon.Foreground = music.ShuffleMode switch { ShuffleMode.All => magenta, ShuffleMode.Album => cyan, _ => dim };
        MusicShuffleBadge.Visibility = music.ShuffleMode == ShuffleMode.All ? Visibility.Visible : Visibility.Collapsed;
        MusicShuffleBadge.Foreground = magenta;
        ToolTipService.SetToolTip(MusicShuffleButton, music.ShuffleMode switch { ShuffleMode.All => "Shuffle: all albums", ShuffleMode.Album => "Shuffle: this album", _ => "Shuffle: off" });
        MusicRepeatIcon.Glyph = music.Repeat == RepeatMode.One ? "" : "";
        MusicRepeatIcon.Foreground = music.Repeat == RepeatMode.Off ? dim : cyan;
        ToolTipService.SetToolTip(MusicRepeatButton, music.Repeat switch { RepeatMode.All => "Repeat: all", RepeatMode.One => "Repeat: this track", _ => "Repeat: off" });
        MusicMuteIcon.Glyph = music.Muted || music.Volume <= 0 ? "" : "";
        MusicMuteIcon.Foreground = music.Muted ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgMagenta"] : white;
        syncingVolume = true;
        MusicVolumeSlider.Value = Math.Round(music.Volume * 100);
        syncingVolume = false;
    }

    const int SpecBands = 16, SpecRows = 10, SpecSegW = 7, SpecSegH = 4, SpecGap = 2;
    readonly Microsoft.UI.Xaml.Shapes.Rectangle[,] specSegs = new Microsoft.UI.Xaml.Shapes.Rectangle[SpecBands, SpecRows];
    readonly double[] specIn = new double[SpecBands], specLevel = new double[SpecBands], specHold = new double[SpecBands];
    readonly long[] specHoldAt = new long[SpecBands];
    readonly int[] specLit = new int[SpecBands], specCap = new int[SpecBands];

    // bottom rows cyan, middle magenta, top gold; caps (peak hold) white
    static Microsoft.UI.Xaml.Media.SolidColorBrush SpecRowBrush(int row)
    {
        static byte Mix(byte a, byte b, double f) => (byte)Math.Round(a + (b - a) * f);
        var f = row / (double)(SpecRows - 1);
        (byte r, byte g, byte b) c = f < 0.6
            ? (Mix(0x5E, 0xFF, f / 0.6), Mix(0xE7, 0x4D, f / 0.6), Mix(0xFF, 0xD2, f / 0.6))
            : (Mix(0xFF, 0xFF, (f - 0.6) / 0.4), Mix(0x4D, 0xD9, (f - 0.6) / 0.4), Mix(0xD2, 0x3A, (f - 0.6) / 0.4));
        return new(Microsoft.UI.ColorHelper.FromArgb(255, c.r, c.g, c.b));
    }
    static readonly Microsoft.UI.Xaml.Media.SolidColorBrush[] specOn = Enumerable.Range(0, SpecRows).Select(SpecRowBrush).ToArray();
    static readonly Microsoft.UI.Xaml.Media.SolidColorBrush specOff = new(Microsoft.UI.ColorHelper.FromArgb(255, 0x1E, 0x1A, 0x33));
    static readonly Microsoft.UI.Xaml.Media.SolidColorBrush specCapBrush = new(Microsoft.UI.ColorHelper.FromArgb(255, 0xF4, 0xF0, 0xFF));

    void BuildMeter()
    {
        SpectrumCanvas.Children.Clear();
        for (int b = 0; b < SpecBands; b++)
            for (int r = 0; r < SpecRows; r++)
            {
                var seg = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = SpecSegW, Height = SpecSegH, RadiusX = 1, RadiusY = 1, Fill = specOff };
                Canvas.SetLeft(seg, b * (SpecSegW + SpecGap));
                Canvas.SetTop(seg, (SpecRows - 1 - r) * (SpecSegH + SpecGap));
                SpectrumCanvas.Children.Add(seg);
                specSegs[b, r] = seg;
            }
        Array.Fill(specLit, -1);
        Array.Fill(specCap, -1);
        meterTimer = DispatcherQueue.CreateTimer();
        meterTimer.Interval = TimeSpan.FromMilliseconds(33);
        meterTimer.IsRepeating = true;
        var tick = 0;
        meterTimer.Tick += (_, _) =>
        {
            if (music is null || Launcher.Visibility != Visibility.Visible) return;
            if (!music.Spectrum(specIn))
            {
                // no spectrum file for this track: spread the L/R peaks over the bands with a little shimmer
                var (l, r) = music.Levels();
                for (int b = 0; b < SpecBands; b++)
                {
                    var side = b < SpecBands / 2 ? l : r;
                    specIn[b] = side * (0.75 + 0.25 * Math.Sin(Environment.TickCount64 / 90.0 + b * 1.7));
                }
            }
            var now = Environment.TickCount64;
            for (int b = 0; b < SpecBands; b++)
            {
                var v = Math.Clamp(specIn[b], 0, 1);
                // instant attack, quick release (~0.35 s full fall), peak caps hold 0.5 s then drop
                specLevel[b] = v > specLevel[b] ? v : Math.Max(v, specLevel[b] - 0.09);
                if (specLevel[b] >= specHold[b] || now - specHoldAt[b] > 500)
                {
                    if (specLevel[b] >= specHold[b]) specHoldAt[b] = now;
                    specHold[b] = Math.Max(specLevel[b], specHold[b] - 0.05);
                }
                var lit = (int)Math.Round(specLevel[b] * SpecRows) - 1;
                var cap = specHold[b] > 0.08 ? Math.Min(SpecRows - 1, (int)Math.Round(specHold[b] * SpecRows) - 1) : -1;
                if (lit == specLit[b] && cap == specCap[b]) continue;
                specLit[b] = lit;
                specCap[b] = cap;
                for (int r = 0; r < SpecRows; r++)
                {
                    var brush = r <= lit ? specOn[r] : r == cap ? specCapBrush : specOff;
                    if (!ReferenceEquals(specSegs[b, r].Fill, brush)) specSegs[b, r].Fill = brush;
                }
            }
            if (++tick % 8 == 0) UpdateMusicTime();
        };
        meterTimer.Start();
    }

    void UpdateMusicTime()
    {
        if (music?.Current is null) return;
        var p = music.Position;
        var d = music.Duration;
        MusicTime.Text = $"{(int)p.TotalMinutes}:{p.Seconds:00} / {(int)d.TotalMinutes}:{d.Seconds:00}";
        MusicProgress.Value = d.TotalSeconds > 0 ? p.TotalSeconds / d.TotalSeconds * 1000 : 0;
    }
}
