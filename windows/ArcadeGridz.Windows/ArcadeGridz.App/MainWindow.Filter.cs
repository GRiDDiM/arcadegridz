using ArcadeGridz.App.Models;
using ArcadeGridz.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace ArcadeGridz.App;

/// <summary>
/// Game browser FILTER (v0.1.74): the LIVE ONLY button opens a panel with four rows that each cycle a value:
/// ArcadeGridz status (LIVE ONLY / + BETAS & ALPHAS), SHOW (everything / ArcadeGridz / PC games / ROMs),
/// PC STORE (Steam, Epic, Battle.net...) and ROM SYSTEM (SNES, NES, GBA...). Store and system lists only offer
/// what's actually in the player's library. Storyboards and albums aren't filtered.
/// </summary>
public sealed partial class MainWindow
{
    const string SrcAll = "ALL", SrcArcade = "ARCADEGRIDZ", SrcPc = "PC", SrcRoms = "ROMS";
    static readonly string[] Sources = [SrcAll, SrcArcade, SrcPc, SrcRoms];
    int filterFocus; // 0 status, 1 show, 2 store, 3 system, 4 reset, 5 done

    static readonly Dictionary<string, string> SystemByExt = new(StringComparer.OrdinalIgnoreCase)
    {
        [".sfc"] = "SNES", [".smc"] = "SNES", [".nes"] = "NES", [".fds"] = "NES",
        [".gba"] = "GBA", [".gb"] = "GAME BOY", [".gbc"] = "GAME BOY COLOR",
        [".n64"] = "N64", [".z64"] = "N64", [".v64"] = "N64", [".nds"] = "DS", [".dsi"] = "DS",
        [".3ds"] = "3DS", [".cci"] = "3DS", [".cxi"] = "3DS", [".3dsx"] = "3DS",
        [".md"] = "GENESIS", [".gen"] = "GENESIS", [".smd"] = "GENESIS", [".32x"] = "32X",
        [".sms"] = "MASTER SYSTEM", [".gg"] = "GAME GEAR", [".cdi"] = "DREAMCAST", [".gdi"] = "DREAMCAST",
        [".pce"] = "PC ENGINE", [".a26"] = "ATARI 2600", [".nsp"] = "SWITCH", [".xci"] = "SWITCH",
        [".gcm"] = "GAMECUBE / WII", [".gcz"] = "GAMECUBE / WII", [".rvz"] = "GAMECUBE / WII", [".wbfs"] = "GAMECUBE / WII",
        [".wud"] = "WII U", [".wux"] = "WII U", [".wua"] = "WII U", [".rpx"] = "WII U", [".xiso"] = "XBOX", [".xex"] = "XBOX 360",
    };

    // emulator name -> system, for file types several systems share (.iso, .cue, .chd, .zip)
    static string? SystemByEmulator(string emu)
    {
        var e = emu.ToUpperInvariant();
        if (e.Contains("SNES") || e.Contains("BSNES") || e.Contains("SNES9X")) return "SNES";
        if (e.Contains("PCSX2")) return "PS2";
        if (e.Contains("DUCKSTATION") || e.Contains("PLAYSTATION")) return "PLAYSTATION";
        if (e.Contains("PPSSPP")) return "PSP";
        if (e.Contains("DOLPHIN")) return "GAMECUBE / WII";
        if (e.Contains("MAME")) return "ARCADE";
        if (e.Contains("RPCS3")) return "PS3";
        if (e.Contains("RETROARCH · ")) return e[(e.IndexOf("· ", StringComparison.Ordinal) + 2)..].Trim();
        return null;
    }

    LocalItem? LocalItemOf(GameEntry g)
    {
        if (!LocalLibraryService.IsLocal(g)) return null;
        var id = LocalLibraryService.LocalId(g);
        return library.Data.Items.FirstOrDefault(i => i.Id == id);
    }

    string RomSystem(LocalItem i)
    {
        if (SystemByExt.TryGetValue(Path.GetExtension(i.Path), out var s)) return s;
        var emu = library.Emulator(i.Emulator)?.Name ?? i.Tags;
        return SystemByEmulator(emu) ?? "OTHER";
    }

    static string PcStore(LocalItem i)
    {
        var t = (i.Tags + " " + i.Path).ToUpperInvariant();
        if (t.Contains("STEAM")) return "STEAM";
        if (t.Contains("EPIC") || t.Contains("COM.EPICGAMES")) return "EPIC";
        if (t.Contains("BATTLE.NET") || t.Contains("BATTLENET")) return "BATTLE.NET";
        if (t.Contains("GOG")) return "GOG";
        if (t.Contains("EA APP") || t.Contains("ORIGIN")) return "EA";
        if (t.Contains("UBISOFT") || t.Contains("UPLAY")) return "UBISOFT";
        if (t.Contains("XBOX")) return "XBOX";
        return "OTHER PC";
    }

    bool FilterActive => settings.FilterSource != SrcAll || settings.FilterStore != SrcAll || settings.FilterSystem != SrcAll;

    bool PassesFilter(GameEntry g)
    {
        if (IsStoryboard(g)) return true;
        var item = LocalItemOf(g);
        var kind = item is null ? SrcArcade : item.Kind == LocalLibraryService.KindRom ? SrcRoms : SrcPc;
        // a store or system choice implies its source
        var src = settings.FilterSystem != SrcAll ? SrcRoms : settings.FilterStore != SrcAll ? SrcPc : settings.FilterSource;
        if (src != SrcAll && src != kind) return false;
        if (settings.FilterStore != SrcAll && (item is null || PcStore(item) != settings.FilterStore)) return false;
        if (settings.FilterSystem != SrcAll && (item is null || RomSystem(item) != settings.FilterSystem)) return false;
        return true;
    }

    List<string> StoreChoices() => [SrcAll, .. library.Data.Items.Where(i => i.Kind == LocalLibraryService.KindApp).Select(PcStore).Distinct().Order()];
    List<string> SystemChoices() => [SrcAll, .. library.Data.Items.Where(i => i.Kind == LocalLibraryService.KindRom).Select(RomSystem).Distinct().Order()];

    static string SourceLabel(string s) => s switch { SrcArcade => "ARCADEGRIDZ GAMES", SrcPc => "PC GAMES", SrcRoms => "ROMS", _ => "EVERYTHING" };

    string FilterSummary()
    {
        var parts = new List<string>();
        if (settings.FilterSystem != SrcAll) parts.Add(settings.FilterSystem + " ROMS");
        else if (settings.FilterStore != SrcAll) parts.Add(settings.FilterStore);
        else if (settings.FilterSource != SrcAll) parts.Add(SourceLabel(settings.FilterSource));
        var showsArcade = settings.FilterSource is SrcAll or SrcArcade && settings.FilterStore == SrcAll && settings.FilterSystem == SrcAll;
        if (showsArcade) parts.Add(settings.ShowTesting ? "+ BETAS" : "LIVE ONLY");
        return string.Join(" · ", parts);
    }

    void OpenFilter()
    {
        // drop choices that no longer exist (a store or system whose games were removed)
        if (!StoreChoices().Contains(settings.FilterStore)) settings.FilterStore = SrcAll;
        if (!SystemChoices().Contains(settings.FilterSystem)) settings.FilterSystem = SrcAll;
        UpdateFilterPanel();
        FilterLayer.Visibility = Visibility.Visible;
        audio?.Play("open");
        filterFocus = 1;
        FocusFilterRow();
    }

    void CloseFilter()
    {
        FilterLayer.Visibility = Visibility.Collapsed;
        audio?.Play("back");
        SetLauncherFocus(LauncherFocus.Testing);
    }

    void UpdateFilterPanel()
    {
        FilterStatusValue.Text = settings.ShowTesting ? "+ BETAS & ALPHAS" : "LIVE ONLY";
        FilterSourceValue.Text = SourceLabel(settings.FilterSource);
        FilterStoreValue.Text = settings.FilterStore == SrcAll ? "ANY" : settings.FilterStore;
        FilterSystemValue.Text = settings.FilterSystem == SrcAll ? "ANY" : settings.FilterSystem;
        FilterStoreButton.IsEnabled = StoreChoices().Count > 1;
        FilterSystemButton.IsEnabled = SystemChoices().Count > 1;
        var n = all.Count(g => Listed(g) && !IsStoryboard(g));
        FilterCount.Text = n == 1 ? "1 GAME MATCHES" : $"{n} GAMES MATCH";
    }

    void ApplyFilterChange()
    {
        settings.Save();
        UpdateFilterPanel();
        UpdateTestingButton();
        StartupLog.Write("nav", $"filter: status={(settings.ShowTesting ? "all" : "live")} source={settings.FilterSource} store={settings.FilterStore} system={settings.FilterSystem}");
        BuildCategories();
        ApplyFilter();
    }

    static string Cycle(List<string> list, string current, int delta)
    {
        if (list.Count == 0) return SrcAll;
        var i = list.IndexOf(current);
        if (i < 0) i = 0;
        return list[((i + delta) % list.Count + list.Count) % list.Count];
    }

    void CycleFilterRow(int row, int delta)
    {
        switch (row)
        {
            case 0: settings.ShowTesting = !settings.ShowTesting; break;
            case 1:
                settings.FilterSource = Cycle([.. Sources], settings.FilterSource, delta);
                // picking a source clears a store/system that no longer fits it
                if (settings.FilterSource != SrcPc) settings.FilterStore = SrcAll;
                if (settings.FilterSource != SrcRoms) settings.FilterSystem = SrcAll;
                break;
            case 2:
                if (!FilterStoreButton.IsEnabled) return;
                settings.FilterStore = Cycle(StoreChoices(), settings.FilterStore, delta);
                if (settings.FilterStore != SrcAll) { settings.FilterSource = SrcPc; settings.FilterSystem = SrcAll; }
                break;
            case 3:
                if (!FilterSystemButton.IsEnabled) return;
                settings.FilterSystem = Cycle(SystemChoices(), settings.FilterSystem, delta);
                if (settings.FilterSystem != SrcAll) { settings.FilterSource = SrcRoms; settings.FilterStore = SrcAll; }
                break;
            default: return;
        }
        audio?.Play("select");
        ApplyFilterChange();
    }

    Button FilterRowButton(int row) => row switch
    {
        0 => FilterStatusButton, 1 => FilterSourceButton, 2 => FilterStoreButton, 3 => FilterSystemButton, 4 => FilterResetButton, _ => FilterDoneButton
    };

    void FocusFilterRow() => PadFocus(FilterRowButton(filterFocus));

    void FilterPad(PadAction action)
    {
        switch (action)
        {
            case PadAction.Up:
                do filterFocus = filterFocus == 5 ? 3 : Math.Max(0, filterFocus - 1);
                while (filterFocus > 0 && !FilterRowButton(filterFocus).IsEnabled);
                FocusFilterRow();
                break;
            case PadAction.Down:
                if (filterFocus >= 4) break;
                do filterFocus++; while (filterFocus < 4 && !FilterRowButton(filterFocus).IsEnabled);
                FocusFilterRow();
                break;
            case PadAction.Left:
                if (filterFocus == 5) { filterFocus = 4; FocusFilterRow(); } else if (filterFocus < 4) CycleFilterRow(filterFocus, -1);
                break;
            case PadAction.Right:
                if (filterFocus == 4) { filterFocus = 5; FocusFilterRow(); } else if (filterFocus < 4) CycleFilterRow(filterFocus, 1);
                break;
            case PadAction.A:
                if (filterFocus < 4) CycleFilterRow(filterFocus, 1);
                else if (filterFocus == 4) ResetFilter();
                else CloseFilter();
                break;
            case PadAction.B:
            case PadAction.ExitCombo:
                CloseFilter();
                break;
        }
    }

    void ResetFilter()
    {
        settings.ShowTesting = false;
        settings.FilterSource = settings.FilterStore = settings.FilterSystem = SrcAll;
        audio?.Play("back");
        ApplyFilterChange();
    }

    void FilterRow_Click(object sender, RoutedEventArgs e)
    {
        for (var r = 0; r < 4; r++)
            if (ReferenceEquals(sender, FilterRowButton(r))) { filterFocus = r; CycleFilterRow(r, 1); return; }
    }

    void FilterReset_Click(object sender, RoutedEventArgs e) => ResetFilter();
    void FilterDone_Click(object sender, RoutedEventArgs e) => CloseFilter();
    void FilterBackdrop_Tapped(object sender, TappedRoutedEventArgs e) => CloseFilter();
    void FilterCard_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;
}
