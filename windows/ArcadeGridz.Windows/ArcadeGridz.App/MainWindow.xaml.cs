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
    enum StartupFocus { Email, Google, Guest }
    readonly CatalogService catalogs = new();
    readonly LeaderboardService leaderboards = new();
    readonly SettingsService settings = SettingsService.Load();
    readonly UpdateService updates = new();
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
    Microsoft.UI.Dispatching.DispatcherQueueTimer? clockTimer;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? splashTimer;

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
        web.Exited += (_, _) => DispatcherQueue.TryEnqueue(ReturnToLauncher);
        web.Status += status => DispatcherQueue.TryEnqueue(() =>
        {
            GameStatus.Text = status.ToUpperInvariant();
            if (status.Equals("started", StringComparison.OrdinalIgnoreCase))
                GameCover.Visibility = Visibility.Collapsed;
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
            ShowAllToggle.IsOn = settings.ShowAll;
            clockTimer = DispatcherQueue.CreateTimer();
            clockTimer.Interval = TimeSpan.FromSeconds(1);
            clockTimer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("h:mm tt");
            ClockText.Text = DateTime.Now.ToString("h:mm tt");
            clockTimer.Start();
            await RefreshCatalog();
            splashTimer = DispatcherQueue.CreateTimer();
            splashTimer.Interval = TimeSpan.FromMilliseconds(1350);
            splashTimer.IsRepeating = false;
            splashTimer.Tick += (_, _) => ShowWelcome();
            splashTimer.Start();
            StartupLog.Write("window", "Catalog ready; splash active");
        }
        catch (Exception ex)
        {
            StartupLog.Exception("MainWindow.OnLoaded", ex);
            ShowMessage("STARTUP ERROR", $"The launcher could not finish loading. Details were saved to {StartupLog.LogFile}");
        }
    }

    async Task RefreshCatalog()
    {
        try
        {
            var result = await catalogs.LoadAsync();
            all.Clear();
            // Keep the complete live catalog. The options toggle controls visibility;
            // Playable controls launch authorization and is checked again at launch.
            all.AddRange(result.Catalog.Games);
            NetworkState.Text = result.Offline ? "OFFLINE CACHE" : "GRID ONLINE • " + result.Catalog.Version;
            StartupLog.Write("catalog", $"Loaded version={result.Catalog.Version}; games={result.Catalog.Games.Count}; offline={result.Offline}");
            BuildCategories();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StartupLog.Exception("Catalog load", ex);
            NetworkState.Text = "CATALOG ERROR";
            PanelTitle.Text = "NO GRID SIGNAL";
            PanelBody.Text = ex.Message;
            Panel.Visibility = Visibility.Visible;
        }
    }

    void BuildCategories()
    {
        categories.Clear();
        categories.Add("POTATO READY");
        categories.AddRange(all.Where(g => settings.ShowAll || (g.Potato && g.Playable)).Select(g => g.Category.ToUpperInvariant()).Where(x => x.Length > 0).Distinct().Order());
        categoryIndex = Math.Min(categoryIndex, categories.Count - 1);
    }

    void ApplyFilter()
    {
        var cat = categories.Count == 0 ? "POTATO READY" : categories[categoryIndex];
        CategoryLabel.Text = cat;
        visible.Clear();
        foreach (var g in all.Where(g => (settings.ShowAll || (g.Potato && g.Playable)) && (cat == "POTATO READY" ? (settings.ShowAll ? g.Potato : g.Potato && g.Playable) : g.Category.Equals(cat, StringComparison.OrdinalIgnoreCase))))
            visible.Add(g);
        if (visible.Count > 0) { GameGrid.SelectedIndex = 0; _ = ShowHero(visible[0]); }
        else
        {
            GameGrid.SelectedIndex = -1;
            HeroName.Text = "NO GAMES";
            HeroMeta.Text = "";
            HeroDescription.Text = "No catalog entries match this filter.";
            HeroArt.Source = null;
            PlayButton.IsEnabled = false;
            PlayButton.Content = "UNAVAILABLE";
            ScoreList.ItemsSource = null;
        }
    }

    async Task ShowHero(GameEntry g)
    {
        HeroName.Text = g.Name;
        HeroMeta.Text = $"{g.Genre}  •  {g.Status.ToUpperInvariant()}  •  {g.Runtime.Type.ToUpperInvariant()}";
        HeroDescription.Text = g.Description;
        PlayButton.IsEnabled = g.Playable;
        PlayButton.Content = g.Playable ? "A  PLAY" : "UNAVAILABLE";
        try { HeroArt.Source = new BitmapImage(new Uri(g.Art)); } catch { HeroArt.Source = null; }
        var scores = await leaderboards.TopAsync(g.Id);
        ScoreList.ItemsSource = scores.Select((s, i) => $"{i + 1}. {s.Name}  {s.Score:N0}  [{s.Tag}]");
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
            GameCover.Visibility = Visibility.Visible;
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
        GameCover.Visibility = Visibility.Visible;
        Launcher.Visibility = Visibility.Visible;
        Panel.Visibility = Visibility.Collapsed;
        SetLauncherFocus(LauncherFocus.GameGrid);
    }

    void PadPressed(PadAction action) => DispatcherQueue.TryEnqueue(async () =>
    {
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
    });

    void ShowWelcome()
    {
        SplashPanel.Visibility = Visibility.Collapsed;
        WelcomePanel.Visibility = Visibility.Visible;
        SetStartupFocus(StartupFocus.Email);
        StartupLog.Write("window", "Welcome screen ready");
    }

    void EnterLauncher(bool guest)
    {
        startupOpen = false;
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
            SetStartupFocus((StartupFocus)Math.Min(2, (int)startupFocus + 1));
        else if (action == PadAction.A)
        {
            if (startupFocus == StartupFocus.Email) StartupEmail_Click(this, new RoutedEventArgs());
            else if (startupFocus == StartupFocus.Google) StartupGoogle_Click(this, new RoutedEventArgs());
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
        StartupStatus.Text = "OPENED SECURE SIGN-IN IN YOUR BROWSER. DESKTOP GOOGLE HANDOFF IS NOT ENABLED YET; EMAIL OR GUEST WORK NOW.";
        StartupLog.Write("auth", "Opening system-browser sign-in; native callback is not registered");
        await Launcher.LaunchUriAsync(new Uri("https://arcadegridz.com/?host=winapp&mode=signin"));
        SetStartupFocus(StartupFocus.Google);
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
            if (action == PadAction.Up) { SetLauncherFocus(LauncherFocus.Games); return; }
            if (action == PadAction.Down) { SetLauncherFocus(LauncherFocus.Play); return; }
            MoveGameSelection(action);
            return;
        }

        if (launcherFocus == LauncherFocus.Play)
        {
            if (action == PadAction.Up || action == PadAction.Left) SetLauncherFocus(LauncherFocus.GameGrid);
            else if (action == PadAction.Down) SetLauncherFocus(LauncherFocus.Exit);
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
            if (action == PadAction.Down) SetLauncherFocus(LauncherFocus.Exit);
            else if (action == PadAction.Left) SetLauncherFocus(LauncherFocus.Categories);
            return;
        }

        var menu = new[] { LauncherFocus.Games, LauncherFocus.Scores, LauncherFocus.Options, LauncherFocus.Exit };
        var menuIndex = Array.IndexOf(menu, launcherFocus);
        if (menuIndex >= 0)
        {
            if (action == PadAction.Left) SetLauncherFocus(menu[Math.Max(0, menuIndex - 1)]);
            else if (action == PadAction.Right) SetLauncherFocus(menu[Math.Min(menu.Length - 1, menuIndex + 1)]);
            else if (action == PadAction.Up) SetLauncherFocus(launcherFocus == LauncherFocus.Exit ? LauncherFocus.SignIn : LauncherFocus.Categories);
            else if (action == PadAction.Down) SetLauncherFocus(launcherFocus == LauncherFocus.Exit ? LauncherFocus.Play : LauncherFocus.GameGrid);
        }
    }

    void MoveGameSelection(PadAction action)
    {
        if (visible.Count == 0) return;
        var i = Math.Max(0, GameGrid.SelectedIndex);
        i += action == PadAction.Left ? -1 : 1;
        GameGrid.SelectedIndex = Math.Clamp(i, 0, visible.Count - 1);
        GameGrid.ScrollIntoView(GameGrid.SelectedItem);
    }

    void SetLauncherFocus(LauncherFocus target)
    {
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

    async Task HandlePanelAction(PadAction action)
    {
        if (action == PadAction.B) { ReturnToLauncher(); return; }
        var options = ShowAllToggle.Visibility == Visibility.Visible;
        var max = options ? (InstallUpdateButton.IsEnabled ? 3 : 2) : 0;
        if (action is PadAction.Up or PadAction.Left) panelFocus = Math.Max(0, panelFocus - 1);
        else if (action is PadAction.Down or PadAction.Right) panelFocus = Math.Min(max, panelFocus + 1);
        else if (action == PadAction.A)
        {
            if (!options || panelFocus == max) { ReturnToLauncher(); return; }
            if (panelFocus == 0) ShowAllToggle.IsOn = !ShowAllToggle.IsOn;
            else if (panelFocus == 1) CheckUpdate_Click(this, new RoutedEventArgs());
            else if (panelFocus == 2 && InstallUpdateButton.IsEnabled) InstallUpdate_Click(this, new RoutedEventArgs());
        }
        FocusPanelControl(max);
        await Task.CompletedTask;
    }

    void FocusPanelControl(int max)
    {
        if (panelFocus >= max) BackButton.Focus(FocusState.Programmatic);
        else if (panelFocus == 0) ShowAllToggle.Focus(FocusState.Programmatic);
        else if (panelFocus == 1) CheckUpdateButton.Focus(FocusState.Programmatic);
        else InstallUpdateButton.Focus(FocusState.Programmatic);
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
        PanelBody.Text = "Potato-ready is the safe controller-only list. Enable all games to include titles that may require a keyboard or touch.";
        ShowAllToggle.Visibility = Visibility.Visible;
        VersionText.Text = $"INSTALLED VERSION  {UpdateService.InstalledVersion}";
        VersionText.Visibility = Visibility.Visible;
        UpdateStatus.Text = "Updates are optional. ArcadeGridz never installs one without confirmation.";
        UpdateStatus.Visibility = Visibility.Visible;
        UpdateActions.Visibility = Visibility.Visible;
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
        ShowAllToggle.Visibility = Visibility.Collapsed;
        VersionText.Visibility = Visibility.Collapsed;
        UpdateStatus.Visibility = Visibility.Collapsed;
        UpdateActions.Visibility = Visibility.Collapsed;
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
    void ShowAllToggle_Toggled(object sender, RoutedEventArgs e) { settings.ShowAll = ShowAllToggle.IsOn; settings.Save(); BuildCategories(); ApplyFilter(); }
}
