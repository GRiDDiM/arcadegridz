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
            GameStatus.Text = status.ToUpperInvariant() + " • HOLD SELECT + START TO EXIT");
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
            await RefreshCatalog();
            Root.Focus(FocusState.Programmatic);
            StartupLog.Write("window", "Launcher ready");
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
            all.AddRange(result.Catalog.Games.Where(g => g.Playable));
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
        categories.AddRange(all.Where(g => settings.ShowAll || g.Potato).Select(g => g.Category.ToUpperInvariant()).Where(x => x.Length > 0).Distinct().Order());
        categoryIndex = Math.Min(categoryIndex, categories.Count - 1);
    }

    void ApplyFilter()
    {
        var cat = categories.Count == 0 ? "POTATO READY" : categories[categoryIndex];
        CategoryLabel.Text = cat;
        visible.Clear();
        foreach (var g in all.Where(g => (settings.ShowAll || g.Potato) && (cat == "POTATO READY" ? g.Potato : g.Category.Equals(cat, StringComparison.OrdinalIgnoreCase))))
            visible.Add(g);
        if (visible.Count > 0) { GameGrid.SelectedIndex = 0; _ = ShowHero(visible[0]); }
    }

    async Task ShowHero(GameEntry g)
    {
        HeroName.Text = g.Name;
        HeroMeta.Text = $"{g.Genre}  •  {g.Status.ToUpperInvariant()}  •  {g.Runtime.Type.ToUpperInvariant()}";
        HeroDescription.Text = g.Description;
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
            if (g.Runtime.Type.Equals("web", StringComparison.OrdinalIgnoreCase))
            {
                var webRuntime = EnsureWebRuntime();
                active = webRuntime.CanRun(g) ? webRuntime : null;
            }
            else active = native.CanRun(g) ? native : null;

            if (active is null) { ShowMessage("RUNTIME ERROR", "This game's runtime manifest is incomplete."); return; }
            StartupLog.Write("runtime", $"Launching id={g.Id}; type={g.Runtime.Type}");
            gameOpen = true;
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
        Launcher.Visibility = Visibility.Visible;
        Panel.Visibility = Visibility.Collapsed;
        GameGrid.Focus(FocusState.Programmatic);
    }

    void PadPressed(PadAction action) => DispatcherQueue.TryEnqueue(async () =>
    {
        if (action == PadAction.ExitCombo && gameOpen) { await ExitGame(); return; }
        if (gameOpen) return;
        if (Panel.Visibility == Visibility.Visible) { if (action == PadAction.B) ReturnToLauncher(); return; }
        if (action == PadAction.LB) ChangeCategory(-1);
        else if (action == PadAction.RB) ChangeCategory(1);
        else if (action == PadAction.A && GameGrid.SelectedItem is GameEntry g) await Launch(g);
        else if (action == PadAction.B) ReturnToLauncher();
        else MoveSelection(action);
    });

    void MoveSelection(PadAction action)
    {
        if (visible.Count == 0) return;
        var i = Math.Max(0, GameGrid.SelectedIndex);
        i = action switch { PadAction.Left => i - 1, PadAction.Right => i + 1, PadAction.Up => i - 3, PadAction.Down => i + 3, _ => i };
        GameGrid.SelectedIndex = Math.Clamp(i, 0, visible.Count - 1);
        GameGrid.ScrollIntoView(GameGrid.SelectedItem);
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
        else if (e.Key == VirtualKey.Enter && GameGrid.SelectedItem is GameEntry g) { _ = Launch(g); e.Handled = true; }
    }

    async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var webRuntime = EnsureWebRuntime();
            active = webRuntime;
            gameOpen = true;
            GameLayer.Visibility = Visibility.Visible;
            Launcher.Visibility = Visibility.Collapsed;
            await webRuntime.NavigateAsync("https://arcadegridz.com/?host=winapp&mode=signin");
        }
        catch (Exception ex)
        {
            StartupLog.Exception("Sign in", ex);
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
