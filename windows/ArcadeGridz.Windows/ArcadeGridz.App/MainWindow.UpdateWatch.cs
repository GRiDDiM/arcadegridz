using ArcadeGridz.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace ArcadeGridz.App;

/// <summary>
/// Passive update check while the app is open: one small GitHub releases request every 30 minutes
/// (skipped while a game runs, caught up when it closes). A newer release shows a pulsing UPDATE READY
/// button next to the clock; it opens OPTIONS &gt; UPDATES with INSTALL focused. Nothing installs on its own.
/// </summary>
public sealed partial class MainWindow
{
    static readonly TimeSpan UpdateWatchEvery = TimeSpan.FromMinutes(30);
    Microsoft.UI.Dispatching.DispatcherQueueTimer? updateWatchTimer;
    DateTime lastUpdateWatch = DateTime.UtcNow; // the startup check just ran
    bool updateWatchBusy;
    Storyboard? updateReadyPulse;

    void InitUpdateWatch()
    {
        updateWatchTimer = DispatcherQueue.CreateTimer();
        updateWatchTimer.Interval = TimeSpan.FromMinutes(1); // cheap tick; the real check runs every 30 min
        updateWatchTimer.Tick += async (_, _) =>
        {
            if (gameOpen || updateWatchBusy || DateTime.UtcNow - lastUpdateWatch < UpdateWatchEvery) return;
            await PassiveUpdateCheck();
        };
        updateWatchTimer.Start();
        ShowUpdateReady();
    }

    async Task PassiveUpdateCheck()
    {
        updateWatchBusy = true;
        lastUpdateWatch = DateTime.UtcNow;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await updates.CheckAsync(cts.Token);
            if (result.Update is not null)
            {
                var isNew = availableUpdate?.Version != result.Update.Version;
                availableUpdate = result.Update;
                InstallUpdateButton.IsEnabled = true;
                if (isNew) StartupLog.Write("update", $"Background check: {result.Update.Version} is ready");
            }
            ShowUpdateReady();
        }
        catch (Exception ex) { StartupLog.Write("update", $"Background check skipped: {ex.GetType().Name}: {ex.Message}"); }
        finally { updateWatchBusy = false; }
    }

    void ShowUpdateReady()
    {
        var on = availableUpdate is not null;
        UpdateReadyButton.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on)
        {
            try { updateReadyPulse?.Stop(); } catch { }
            if (launcherFocus == LauncherFocus.Update) SetLauncherFocus(LauncherFocus.SignIn);
            return;
        }
        UpdateReadyVersion.Text = $"V{UpdateService.InstalledVersion} → V{availableUpdate!.Version}";
        if (updateReadyPulse is null)
        {
            var sb = new Storyboard();
            var a = new DoubleAnimation
            {
                From = 1, To = 0.55, Duration = TimeSpan.FromMilliseconds(900), AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(a, UpdateReadyButton);
            Storyboard.SetTargetProperty(a, "Opacity");
            sb.Children.Add(a);
            updateReadyPulse = sb;
        }
        try { updateReadyPulse.Begin(); } catch { }
    }

    void UpdateReady_Click(object sender, RoutedEventArgs e)
    {
        if (availableUpdate is null) return;
        Options_Click(this, new RoutedEventArgs());
        SelectOptionsTab(4, keepFocusOnTabs: false); // UPDATES, INSTALL focused when it's enabled
        UpdateStatus.Text = $"ARCADEGRIDZ {availableUpdate.Version} IS READY. PRESS INSTALL WHEN YOU'RE DONE PLAYING.";
        var controls = PanelControls();
        var install = controls.IndexOf(InstallUpdateButton);
        if (install >= 0) { panelFocus = install; FocusPanelControl(InstallUpdateButton); }
    }
}
