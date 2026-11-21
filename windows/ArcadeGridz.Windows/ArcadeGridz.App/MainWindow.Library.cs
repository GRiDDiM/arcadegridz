using System.Diagnostics;
using ArcadeGridz.App.Models;
using ArcadeGridz.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ArcadeGridz.App;

/// <summary>
/// The player's own library: an ADD GAME / ADD ALBUM card (OPTIONS &gt; GAMES, off by default) adds PC games and apps
/// (exe, shortcut, steam:// style links), emulator ROMs (through emulators the player installed; we ship none) and
/// music folders. Items get name, genre tags, description and a cover, live in the MY LIBRARY category (albums in
/// the ALBUMS view) and launch straight from the PC. TRAILER turns into EDIT for them.
/// </summary>
public sealed partial class MainWindow
{
    readonly LocalLibraryService library = CreateLibrary();
    static LocalLibraryService CreateLibrary() { var l = new LocalLibraryService(); l.Load(); return l; }

    const string AddRuntime = "add";
    static bool IsAddCard(GameEntry g) => g.Runtime.Type == AddRuntime;
    static bool IsLocalAlbum(GameEntry g) => IsAlbum(g) && MusicPlayerService.IsLocalId(AlbumId(g));
    static string LocalAlbumItemId(GameEntry g) => AlbumId(g)[MusicPlayerService.LocalPrefix.Length..];
    static bool IsLibraryItem(GameEntry g) => LocalLibraryService.IsLocal(g) || IsLocalAlbum(g);

    static readonly GameEntry AddGameCard = new()
    {
        Id = "add:game", Name = "ADD GAME", Category = LocalLibraryService.Category, Genre = "PC GAMES  ·  APPS  ·  EMULATOR ROMS", Status = "NEW",
        Description = "Add a game or app from this PC to the launcher: an .exe, a shortcut, or a store link (steam://, Battle.net and so on). " +
                      "You can also add ROMs you own and play them through an emulator you installed. ArcadeGridz doesn't supply emulators or ROMs.",
        Art = "ms-appx:///Assets/Library/add-game.png", Playable = true, Potato = true, Runtime = new RuntimeSpec { Type = AddRuntime }
    };
    static readonly GameEntry AddAlbumCard = new()
    {
        Id = "add:album", Name = "ADD ALBUM", Category = "ALBUM", Genre = "A MUSIC FOLDER ON THIS PC", Status = "NEW",
        Description = "Pick a folder of music on this PC (MP3, FLAC, WAV, M4A, OGG...) and play it in the launcher's music player like any other album.",
        Art = "ms-appx:///Assets/Library/add-album.png", Playable = true, Potato = true, Runtime = new RuntimeSpec { Type = AddRuntime }
    };

    // ---------------- library <-> browser ----------------
    void InitLibrary()
    {
        AddCardToggle.IsOn = settings.ShowAddCard;
        ThemeService.Exclude(AddCoverImage);
        SyncLocalAlbums();
    }

    /// <summary>Feeds the player's album folders to the music player (they show in the ALBUMS view).</summary>
    void SyncLocalAlbums() => music?.SetLocalAlbums(library.Data.Items.Where(i => i.Kind == LocalLibraryService.KindAlbum).Select(i =>
        (i.Id, i.Name, i.Artist, i.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "",
         i.Description, File.Exists(i.Cover) ? LocalLibraryService.FileUri(i.Cover) : "ms-appx:///Assets/Library/local-album.png", i.Path)));

    void RebuildLibrary(string? selectId)
    {
        var keep = selectId ?? (GameGrid.SelectedItem as GameEntry)?.Id;
        all.RemoveAll(LocalLibraryService.IsLocal);
        all.AddRange(library.GameEntries());
        SyncLocalAlbums();
        BuildAlbumEntries();
        var keepCat = categories.Count > 0 ? categories[Math.Clamp(categoryIndex, 0, categories.Count - 1)] : "ALL GAMES";
        BuildCategories();
        categoryIndex = Math.Max(0, categories.IndexOf(keepCat));
        ApplyFilter();
        if (keep is null) return;
        var i = visible.ToList().FindIndex(g => g.Id == keep);
        if (i >= 0) { GameGrid.SelectedIndex = i; GameGrid.ScrollIntoView(visible[i]); }
    }

    /// <summary>Called by ApplyFilter: the ADD card goes first (when enabled), and the first real tile stays selected.</summary>
    void InsertAddCard()
    {
        if (!settings.ShowAddCard || storyMode) return;
        visible.Insert(0, albumMode ? AddAlbumCard : AddGameCard);
    }

    void AddCardToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (initializingSettings) return;
        settings.ShowAddCard = AddCardToggle.IsOn;
        settings.Save();
        ApplyFilter();
    }

    // ---------------- details panel ----------------
    void SetTrailerAsEdit(bool edit)
    {
        TrailerLabel.Text = edit ? "EDIT" : "TRAILER";
        TrailerIcon.Glyph = edit ? "\uE70F" : "\uE714";
    }

    void ShowAddHero(GameEntry g)
    {
        HeroName.Text = g.Name;
        HeroMeta.Text = g.Genre;
        HeroStatus.Text = "YOUR PC";
        HeroStatus.Foreground = HeroStatusChip.BorderBrush = (Brush)Application.Current.Resources["AgCyan"];
        HeroStatusChip.Visibility = Visibility.Visible;
        HeroDescription.Text = g.Description;
        PlayButton.IsEnabled = true;
        PlayLabel.Text = g == AddAlbumCard ? "ADD ALBUM" : "ADD GAME";
        TrailerButton.Visibility = Visibility.Collapsed;
        if (launcherFocus == LauncherFocus.Trailer) SetLauncherFocus(LauncherFocus.Play);
        ScoreHeader.Text = "YOUR LIBRARY";
        ScoreHeaderIcon.Glyph = "\uE8F1";
        ScoreList.ItemsSource = null;
        var n = g == AddAlbumCard ? library.Data.Items.Count(i => i.Kind == LocalLibraryService.KindAlbum) : library.Data.Items.Count(i => i.Kind != LocalLibraryService.KindAlbum);
        ScoreEmpty.Text = n == 0 ? "NOTHING ADDED YET." : $"{n} ADDED  ·  {library.Data.Emulators.Count} EMULATOR{(library.Data.Emulators.Count == 1 ? "" : "S")} SET UP";
        ScoreEmpty.Visibility = Visibility.Visible;
        try { HeroArt.Source = new BitmapImage(new Uri(g.Art)); } catch { HeroArt.Source = null; }
        QueueTrailerPreview(g);
    }

    void ShowLocalHero(GameEntry g)
    {
        var item = library.Get(LocalLibraryService.LocalId(g));
        var emu = item?.Kind == LocalLibraryService.KindRom ? library.Emulator(item.Emulator) : null;
        HeroMeta.Text = item?.Kind == LocalLibraryService.KindRom ? $"{g.Genre}  ·  ROM  ·  {(emu?.Name ?? "NO EMULATOR").ToUpperInvariant()}" : $"{g.Genre}  ·  ON THIS PC";
        SetTrailerAsEdit(true);
        TrailerButton.Visibility = Visibility.Visible;
        ScoreHeader.Text = "YOUR LIBRARY";
        ScoreHeaderIcon.Glyph = "\uE8F1";
        ScoreList.ItemsSource = null;
        ScoreEmpty.Text = item is null ? "" : item.Kind == LocalLibraryService.KindRom ? "ADDED FROM THIS PC  ·  RUNS IN YOUR EMULATOR" : "ADDED FROM THIS PC  ·  NO NETWORK SCORES";
        ScoreEmpty.Visibility = Visibility.Visible;
    }

    // ---------------- launching ----------------
    bool localRunning, windowActive = true;
    Process? localProc;
    DateTime localStarted;
    string localKind = "";

    void LaunchLocal(GameEntry g)
    {
        var item = library.Get(LocalLibraryService.LocalId(g));
        if (item is null) return;
        try
        {
            var p = library.Launch(item);
            audio?.Play("launch");
            music?.PauseForGame();
            localRunning = true;
            localProc = p;
            localKind = item.Kind;
            localStarted = DateTime.UtcNow;
            if (p is not null)
            {
                try { p.EnableRaisingEvents = true; p.Exited += (_, _) => DispatcherQueue.TryEnqueue(() => OnLocalExited(p)); }
                catch { localProc = null; } // already gone (a launcher handed off), handled on re-activation
            }
        }
        catch (Exception ex)
        {
            StartupLog.Exception("Library launch", ex);
            audio?.Play("error");
            ShowMessage("COULDN'T START IT", ex.Message + "\n\nUse EDIT to fix the path or the emulator.");
        }
    }

    void OnLocalExited(Process p)
    {
        if (!localRunning || !ReferenceEquals(p, localProc)) return;
        // a store launcher that hands the game off (Steam, Battle.net...) exits right away: wait for the player to come back instead
        if (localKind != LocalLibraryService.KindRom && DateTime.UtcNow - localStarted < TimeSpan.FromSeconds(12)) { localProc = null; return; }
        EndLocalSession(bringBack: true);
    }

    void EndLocalSession(bool bringBack)
    {
        if (!localRunning) return;
        localRunning = false;
        localProc = null;
        music?.ResumeAfterGame();
        StartupLog.Write("library", "Back from a library game");
        if (bringBack) { try { Activate(); } catch { } }
    }

    /// <summary>Window (de)activation: the pad only drives the launcher while it's in front during a library game.</summary>
    void LibraryOnActivated(bool active)
    {
        windowActive = active;
        if (active && localRunning && (localProc is null || localProc.HasExited)) EndLocalSession(bringBack: false);
    }

    // ---------------- editor ----------------
    string? editId;             // null = adding
    string editKind = LocalLibraryService.KindApp;
    string? coverSource;        // chosen image (or the item's current cover); "" = default cover
    string? emuEditId;
    bool removeArmed, emuDeleteArmed;

    void OpenNewEditor(bool album)
    {
        editId = null;
        editKind = album ? LocalLibraryService.KindAlbum : LocalLibraryService.KindApp;
        AddPathBox.Text = AddArgsBox.Text = AddNameBox.Text = AddArtistBox.Text = AddTagsBox.Text = AddDescBox.Text = "";
        coverSource = "";
        ShowEditor();
    }

    void OpenEditor(GameEntry g)
    {
        var item = library.Get(IsLocalAlbum(g) ? LocalAlbumItemId(g) : LocalLibraryService.LocalId(g));
        if (item is null) return;
        editId = item.Id;
        editKind = item.Kind;
        AddPathBox.Text = item.Path;
        AddArgsBox.Text = item.Args;
        AddNameBox.Text = item.Name;
        AddArtistBox.Text = item.Artist;
        AddTagsBox.Text = item.Tags;
        AddDescBox.Text = item.Description;
        coverSource = File.Exists(item.Cover) ? item.Cover : "";
        ShowEditor();
        if (item.Kind == LocalLibraryService.KindRom)
        {
            var i = library.Data.Emulators.FindIndex(e => e.Id == item.Emulator);
            AddEmuCombo.SelectedIndex = i;
        }
    }

    void ShowEditor()
    {
        removeArmed = emuDeleteArmed = false;
        EmuForm.Visibility = Visibility.Collapsed;
        AddStatus.Visibility = Visibility.Collapsed;
        FillEmulators(null);
        UpdateEditorKind();
        UpdateCoverPreview();
        AddRemoveButton.Visibility = editId is null ? Visibility.Collapsed : Visibility.Visible;
        AddRemoveLabel.Text = "REMOVE";
        AddSaveLabel.Text = editId is null ? "ADD TO LIBRARY" : "SAVE";
        AddLayer.Visibility = Visibility.Visible;
        AddScroll.ChangeView(null, 0, null, true);
        audio?.Play("open");
        DispatcherQueue.TryEnqueue(() => PadFocus(editKind == LocalLibraryService.KindAlbum || editId is not null ? AddBrowseButton : AddKindAppButton));
    }

    void CloseEditor()
    {
        AddLayer.Visibility = Visibility.Collapsed;
        SetLauncherFocus(launcherFocus == LauncherFocus.Trailer && TrailerButton.Visibility == Visibility.Visible ? LauncherFocus.Trailer : LauncherFocus.Play);
    }

    void UpdateEditorKind()
    {
        bool album = editKind == LocalLibraryService.KindAlbum, rom = editKind == LocalLibraryService.KindRom;
        var add = editId is null;
        AddTitle.Text = (add ? "ADD " : "EDIT ") + (album ? "ALBUM" : rom ? "ROM" : "GAME");
        AddKindRow.Visibility = album ? Visibility.Collapsed : Visibility.Visible;
        var pink = (Brush)Application.Current.Resources["AgMagenta"];
        if (rom) { AddKindRomButton.BorderBrush = pink; AddKindAppButton.ClearValue(Control.BorderBrushProperty); }
        else { AddKindAppButton.BorderBrush = pink; AddKindRomButton.ClearValue(Control.BorderBrushProperty); }
        AddPathLabel.Text = album ? "MUSIC FOLDER" : rom ? "ROM FILE" : "GAME FILE, SHORTCUT OR LINK";
        AddPathBox.PlaceholderText = album ? @"C:\Music\My Album" : rom ? @"C:\ROMs\my-game.sfc" : @"C:\Games\MyGame\game.exe  or  steam://rungameid/...";
        AddBrowseLabel.Text = album ? "PICK FOLDER" : "BROWSE";
        AddEmuRow.Visibility = rom ? Visibility.Visible : Visibility.Collapsed;
        AddArgsBox.Visibility = !album && !rom ? Visibility.Visible : Visibility.Collapsed;
        AddArtistBox.Visibility = album ? Visibility.Visible : Visibility.Collapsed;
        AddDescBox.PlaceholderText = album ? "What the album is" : "A line or two about it";
        UpdateCoverPreview();
    }

    void UpdateCoverPreview()
    {
        var fallback = editKind == LocalLibraryService.KindRom ? "local-rom" : editKind == LocalLibraryService.KindAlbum ? "local-album" : "local-app";
        try
        {
            AddCoverImage.Source = new BitmapImage(new Uri(!string.IsNullOrEmpty(coverSource) && File.Exists(coverSource)
                ? coverSource : $"ms-appx:///Assets/Library/{fallback}.png"));
        }
        catch { AddCoverImage.Source = null; }
    }

    void FillEmulators(string? selectId)
    {
        var keep = selectId ?? (AddEmuCombo.SelectedIndex >= 0 && AddEmuCombo.SelectedIndex < library.Data.Emulators.Count ? library.Data.Emulators[AddEmuCombo.SelectedIndex].Id : null);
        AddEmuCombo.Items.Clear();
        foreach (var e in library.Data.Emulators) AddEmuCombo.Items.Add(e.Name);
        AddEmuCombo.SelectedIndex = keep is null ? (library.Data.Emulators.Count == 1 ? 0 : -1) : library.Data.Emulators.FindIndex(e => e.Id == keep);
        AddEmuEditButton.IsEnabled = library.Data.Emulators.Count > 0;
    }

    void AddKindApp_Click(object sender, RoutedEventArgs e) { editKind = LocalLibraryService.KindApp; UpdateEditorKind(); }
    void AddKindRom_Click(object sender, RoutedEventArgs e) { editKind = LocalLibraryService.KindRom; UpdateEditorKind(); if (library.Data.Emulators.Count == 0) AddEmuNew_Click(sender, e); }

    void AddPathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (editKind == LocalLibraryService.KindRom && AddEmuCombo.SelectedIndex < 0 && library.EmulatorFor(AddPathBox.Text) is { } emu)
            AddEmuCombo.SelectedIndex = library.Data.Emulators.IndexOf(emu);
    }

    T Picker<T>(T picker) where T : class { InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); return picker; }

    async void AddBrowse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (editKind == LocalLibraryService.KindAlbum)
            {
                var fp = Picker(new FolderPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary });
                fp.FileTypeFilter.Add("*");
                var folder = await fp.PickSingleFolderAsync();
                if (folder is null) return;
                AddPathBox.Text = folder.Path;
                if (string.IsNullOrWhiteSpace(AddNameBox.Text)) AddNameBox.Text = folder.Name;
                if (string.IsNullOrEmpty(coverSource) && LocalLibraryService.FindFolderCover(folder.Path) is { } c) { coverSource = c; UpdateCoverPreview(); }
                return;
            }
            var p = Picker(new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder, ViewMode = PickerViewMode.List });
            if (editKind == LocalLibraryService.KindRom) p.FileTypeFilter.Add("*");
            else foreach (var x in new[] { ".exe", ".lnk", ".url", ".bat", ".cmd" }) p.FileTypeFilter.Add(x);
            var file = await p.PickSingleFileAsync();
            if (file is null) return;
            AddPathBox.Text = file.Path;
            if (string.IsNullOrWhiteSpace(AddNameBox.Text)) AddNameBox.Text = SuggestName(file.Path);
            if (editKind == LocalLibraryService.KindRom && library.EmulatorFor(file.Path) is { } emu) AddEmuCombo.SelectedIndex = library.Data.Emulators.IndexOf(emu);
        }
        catch (Exception ex) { StartupLog.Exception("Library picker", ex); ShowAddStatus("The file picker didn't open. You can type or paste the path instead."); }
    }

    string SuggestName(string path)
    {
        try
        {
            if (editKind == LocalLibraryService.KindApp && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var v = FileVersionInfo.GetVersionInfo(path);
                var n = !string.IsNullOrWhiteSpace(v.ProductName) ? v.ProductName : v.FileDescription;
                if (!string.IsNullOrWhiteSpace(n)) return n.Trim();
            }
        }
        catch { }
        var name = Path.GetFileNameWithoutExtension(path);
        // "Game Name (USA) [!]" -> "Game Name"
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*[\(\[][^\)\]]*[\)\]]", "").Replace('_', ' ').Trim();
        return name.Length > 0 ? name : Path.GetFileNameWithoutExtension(path);
    }

    async void AddCover_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var p = Picker(new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail });
            foreach (var x in LocalLibraryService.ImageExtensions) p.FileTypeFilter.Add(x);
            var file = await p.PickSingleFileAsync();
            if (file is null) return;
            coverSource = file.Path;
            UpdateCoverPreview();
        }
        catch (Exception ex) { StartupLog.Exception("Cover picker", ex); ShowAddStatus("The image picker didn't open."); }
    }

    void AddCoverClear_Click(object sender, RoutedEventArgs e) { coverSource = ""; UpdateCoverPreview(); }

    string CoverName() => AddNameBox.Text.Trim().Length > 0 ? AddNameBox.Text.Trim()
        : AddPathBox.Text.Trim().Length == 0 ? "" : editKind == LocalLibraryService.KindAlbum ? Path.GetFileName(AddPathBox.Text.Trim().TrimEnd('\\', '/')) : SuggestName(AddPathBox.Text.Trim().Trim('"'));

    void ShowAddInfo(string text) { AddStatus.Text = text.ToUpperInvariant(); AddStatus.Visibility = Visibility.Visible; }

    bool coverBusy;
    async void AddCoverWeb_Click(object sender, RoutedEventArgs e)
    {
        if (coverBusy) return;
        var name = CoverName();
        if (name.Length == 0) { ShowAddStatus("Type the name first (or pick the file)."); return; }
        coverBusy = true;
        ShowAddInfo("Searching for a cover...");
        try
        {
            var found = editKind == LocalLibraryService.KindAlbum
                ? await CoverService.FindAlbumAsync(name, AddArtistBox.Text.Trim(), strict: false)
                : await CoverService.FindGameAsync(name, strict: false);
            if (found is { } f) { coverSource = f.Path; UpdateCoverPreview(); ShowAddInfo($"Found: {f.Title}"); audio?.Play("select"); }
            else ShowAddStatus(editKind == LocalLibraryService.KindAlbum ? "No album art found online. Try MAKE FROM ICON or pick one." : "Not found on Steam. Try MAKE FROM ICON or pick one.");
        }
        catch (Exception ex) { StartupLog.Exception("Cover search", ex); ShowAddStatus("Couldn't reach the cover search. Check the connection."); }
        finally { coverBusy = false; }
    }

    async void AddCoverGen_Click(object sender, RoutedEventArgs e)
    {
        if (coverBusy) return;
        coverBusy = true;
        try { coverSource = await MakeCover(CoverName()); UpdateCoverPreview(); AddStatus.Visibility = Visibility.Collapsed; audio?.Play("select"); }
        catch (Exception ex) { StartupLog.Exception("Cover generate", ex); ShowAddStatus("Couldn't make a cover."); }
        finally { coverBusy = false; }
    }

    /// <summary>Icon cover: the program's icon (a ROM uses its emulator's), or the name's initials.</summary>
    async Task<string> MakeCover(string name)
    {
        var path = AddPathBox.Text.Trim().Trim('"');
        string? iconFrom = editKind switch
        {
            LocalLibraryService.KindApp => path,
            LocalLibraryService.KindRom when AddEmuCombo.SelectedIndex >= 0 && AddEmuCombo.SelectedIndex < library.Data.Emulators.Count => library.Data.Emulators[AddEmuCombo.SelectedIndex].Exe,
            _ => null
        };
        using var icon = iconFrom is null ? null : await CoverService.IconAsync(iconFrom);
        var label = editKind == LocalLibraryService.KindRom ? "MY LIBRARY · ROM" : editKind == LocalLibraryService.KindAlbum ? "MY LIBRARY · ALBUM" : "MY LIBRARY · PC";
        return CoverService.Generate(name, label, icon);
    }

    void ShowAddStatus(string text) { AddStatus.Text = text.ToUpperInvariant(); AddStatus.Visibility = Visibility.Visible; audio?.Play("error"); }

    bool saving;
    async void AddSave_Click(object sender, RoutedEventArgs e)
    {
        if (saving || coverBusy) return;
        var path = AddPathBox.Text.Trim().Trim('"');
        bool album = editKind == LocalLibraryService.KindAlbum, rom = editKind == LocalLibraryService.KindRom;
        if (path.Length == 0) { ShowAddStatus(album ? "Pick the music folder first." : rom ? "Pick the ROM file first." : "Pick the game file (or paste a link) first."); return; }
        if (album)
        {
            if (!Directory.Exists(path)) { ShowAddStatus("That folder doesn't exist."); return; }
            if (!Directory.EnumerateFiles(path).Any(f => LocalLibraryService.AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())))
            { ShowAddStatus("No music files in that folder (MP3, FLAC, WAV, M4A, OGG, WMA, AAC)."); return; }
        }
        else if (!path.Contains("://") && !File.Exists(path)) { ShowAddStatus("That file doesn't exist."); return; }
        EmulatorConfig? emu = null;
        if (rom)
        {
            if (AddEmuCombo.SelectedIndex < 0 || AddEmuCombo.SelectedIndex >= library.Data.Emulators.Count) { ShowAddStatus("Pick an emulator for this ROM (or set one up with + NEW)."); return; }
            emu = library.Data.Emulators[AddEmuCombo.SelectedIndex];
        }
        var name = AddNameBox.Text.Trim();
        if (name.Length == 0) name = album ? Path.GetFileName(path.TrimEnd('\\', '/')) : SuggestName(path);

        // no cover picked: look it up online (exact name match only), else make one from the icon
        if (string.IsNullOrEmpty(coverSource) && !(album && LocalLibraryService.FindFolderCover(path) is not null))
        {
            saving = true;
            AddSaveButton.IsEnabled = false;
            ShowAddInfo("Getting a cover...");
            try
            {
                var found = album ? await CoverService.FindAlbumAsync(name, AddArtistBox.Text.Trim(), strict: true) : await CoverService.FindGameAsync(name, strict: true);
                coverSource = found?.Path ?? await MakeCover(name);
            }
            catch (Exception ex)
            {
                StartupLog.Write("library", "Online cover lookup skipped: " + ex.Message);
                try { coverSource = await MakeCover(name); } catch (Exception gx) { StartupLog.Exception("Cover generate", gx); }
            }
            finally { saving = false; AddSaveButton.IsEnabled = true; }
            if (AddLayer.Visibility != Visibility.Visible) return; // closed while it worked
        }
        var item = editId is null ? new LocalItem() : library.Get(editId) ?? new LocalItem();
        item.Kind = editKind;
        item.Path = path;
        item.Name = name;
        item.Tags = AddTagsBox.Text.Trim();
        item.Description = AddDescBox.Text.Trim();
        item.Args = rom || album ? "" : AddArgsBox.Text.Trim();
        item.Emulator = emu?.Id ?? "";
        item.Artist = album ? AddArtistBox.Text.Trim() : "";
        try
        {
            if (string.IsNullOrEmpty(coverSource))
            {
                var auto = album ? LocalLibraryService.FindFolderCover(path) : null;
                item.Cover = auto is null ? "" : LocalLibraryService.ImportCover(auto, item.Id);
            }
            else if (!string.Equals(coverSource, item.Cover, StringComparison.OrdinalIgnoreCase)) item.Cover = LocalLibraryService.ImportCover(coverSource, item.Id);
        }
        catch (Exception ex) { StartupLog.Exception("Cover import", ex); item.Cover = ""; }
        var wasNew = editId is null;
        library.Upsert(item);
        StartupLog.Write("library", $"{(wasNew ? "Added" : "Saved")} {item.Kind} '{item.Name}'");
        audio?.Play("launch");
        AddLayer.Visibility = Visibility.Collapsed;
        // show it: games land in MY LIBRARY, albums in the ALBUMS view
        if (album)
        {
            if (!albumMode) ToggleAlbumsView(fromPad: false);
            RebuildLibrary("album:" + MusicPlayerService.LocalPrefix + item.Id);
        }
        else
        {
            if (albumMode || storyMode) { albumMode = false; storyMode = false; }
            RebuildLibrary("local:" + item.Id);
            var ci = categories.IndexOf(LocalLibraryService.Category);
            if (ci >= 0 && categoryIndex != 0 && categoryIndex != ci) { categoryIndex = ci; ApplyFilter(); }
            SelectEntry("local:" + item.Id);
        }
        SetLauncherFocus(LauncherFocus.Play);
    }

    void SelectEntry(string id)
    {
        var i = visible.ToList().FindIndex(g => g.Id == id);
        if (i < 0) return;
        GameGrid.SelectedIndex = i;
        GameGrid.ScrollIntoView(visible[i]);
    }

    void AddCancel_Click(object sender, RoutedEventArgs e) { audio?.Play("back"); CloseEditor(); }

    void AddRemove_Click(object sender, RoutedEventArgs e)
    {
        if (editId is null) return;
        if (!removeArmed) { removeArmed = true; AddRemoveLabel.Text = "PRESS AGAIN TO REMOVE"; audio?.Play("error"); return; }
        var item = library.Get(editId);
        library.Remove(editId);
        StartupLog.Write("library", $"Removed {item?.Kind} '{item?.Name}' (the file on the PC is untouched)");
        AddLayer.Visibility = Visibility.Collapsed;
        RebuildLibrary(null);
        SetLauncherFocus(LauncherFocus.GameGrid);
    }

    // ---- emulators (the player's own) ----
    void AddEmuNew_Click(object sender, RoutedEventArgs e)
    {
        emuEditId = null;
        EmuFormTitle.Text = "NEW EMULATOR";
        EmuNameBox.Text = EmuExeBox.Text = EmuExtBox.Text = "";
        EmuArgsBox.Text = "\"{rom}\"";
        EmuDeleteButton.Visibility = Visibility.Collapsed;
        EmuForm.Visibility = Visibility.Visible;
        PadFocus(EmuNameBox);
    }

    void AddEmuEdit_Click(object sender, RoutedEventArgs e)
    {
        if (AddEmuCombo.SelectedIndex < 0 || AddEmuCombo.SelectedIndex >= library.Data.Emulators.Count) { ShowAddStatus("Pick the emulator to edit first."); return; }
        var emu = library.Data.Emulators[AddEmuCombo.SelectedIndex];
        emuEditId = emu.Id;
        EmuFormTitle.Text = "EDIT EMULATOR";
        EmuNameBox.Text = emu.Name;
        EmuExeBox.Text = emu.Exe;
        EmuArgsBox.Text = emu.Args;
        EmuExtBox.Text = emu.Extensions;
        emuDeleteArmed = false;
        EmuDeleteLabel.Text = "DELETE";
        EmuDeleteButton.Visibility = Visibility.Visible;
        EmuForm.Visibility = Visibility.Visible;
        PadFocus(EmuNameBox);
    }

    async void EmuBrowse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var p = Picker(new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder, ViewMode = PickerViewMode.List });
            p.FileTypeFilter.Add(".exe");
            var file = await p.PickSingleFileAsync();
            if (file is null) return;
            EmuExeBox.Text = file.Path;
            if (string.IsNullOrWhiteSpace(EmuNameBox.Text))
            {
                var v = FileVersionInfo.GetVersionInfo(file.Path);
                EmuNameBox.Text = !string.IsNullOrWhiteSpace(v.ProductName) ? v.ProductName.Trim() : Path.GetFileNameWithoutExtension(file.Path);
            }
        }
        catch (Exception ex) { StartupLog.Exception("Emulator picker", ex); ShowAddStatus("The file picker didn't open. You can paste the path instead."); }
    }

    void EmuSave_Click(object sender, RoutedEventArgs e)
    {
        var exe = EmuExeBox.Text.Trim().Trim('"');
        if (EmuNameBox.Text.Trim().Length == 0) { ShowAddStatus("Give the emulator a name."); return; }
        if (!File.Exists(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { ShowAddStatus("Point to the emulator's .exe on this PC."); return; }
        var args = EmuArgsBox.Text.Trim();
        if (!args.Contains("{rom}")) { ShowAddStatus("The launch arguments need {rom} where the ROM file goes."); return; }
        var emu = emuEditId is null ? new EmulatorConfig() : library.Emulator(emuEditId) ?? new EmulatorConfig();
        emu.Name = EmuNameBox.Text.Trim();
        emu.Exe = exe;
        emu.Args = args;
        emu.Extensions = EmuExtBox.Text.Trim();
        if (!library.Data.Emulators.Contains(emu)) library.Data.Emulators.Add(emu);
        library.Save();
        FillEmulators(emu.Id);
        EmuForm.Visibility = Visibility.Collapsed;
        AddStatus.Visibility = Visibility.Collapsed;
        audio?.Play("select");
        PadFocus(AddEmuCombo);
    }

    void EmuCancel_Click(object sender, RoutedEventArgs e) { EmuForm.Visibility = Visibility.Collapsed; PadFocus(AddEmuNewButton); }

    void EmuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (emuEditId is null) return;
        if (!emuDeleteArmed) { emuDeleteArmed = true; EmuDeleteLabel.Text = "PRESS AGAIN TO DELETE"; audio?.Play("error"); return; }
        library.Data.Emulators.RemoveAll(x => x.Id == emuEditId);
        library.Save();
        emuEditId = null;
        FillEmulators(null);
        EmuForm.Visibility = Visibility.Collapsed;
        PadFocus(AddEmuNewButton);
    }

    // ---- pad / keyboard inside the editor ----
    List<Control> EditorControls()
    {
        var list = new List<Control>();
        void Add(Control c) { if (c.Visibility == Visibility.Visible && IsShown(c)) list.Add(c); }
        if (AddKindRow.Visibility == Visibility.Visible) { Add(AddKindAppButton); Add(AddKindRomButton); }
        Add(AddPathBox); Add(AddBrowseButton);
        if (AddEmuRow.Visibility == Visibility.Visible)
        {
            Add(AddEmuCombo); Add(AddEmuNewButton); Add(AddEmuEditButton);
            if (EmuForm.Visibility == Visibility.Visible) { Add(EmuNameBox); Add(EmuExeBox); Add(EmuBrowseButton); Add(EmuArgsBox); Add(EmuExtBox); Add(EmuSaveButton); Add(EmuCancelButton); Add(EmuDeleteButton); }
        }
        Add(AddArgsBox); Add(AddNameBox); Add(AddArtistBox); Add(AddTagsBox); Add(AddDescBox);
        Add(AddCoverButton); Add(AddCoverWebButton); Add(AddCoverGenButton); Add(AddCoverClearButton);
        Add(AddSaveButton); Add(AddCancelButton); Add(AddRemoveButton);
        return list;
    }

    static bool IsShown(FrameworkElement e)
    {
        for (DependencyObject? d = e; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is UIElement u && u.Visibility != Visibility.Visible) return false;
        return true;
    }

    void HandleEditorAction(PadAction action)
    {
        var controls = EditorControls();
        if (controls.Count == 0) return;
        var focused = FocusManager.GetFocusedElement(AddLayer.XamlRoot) as Control;
        var i = focused is null ? -1 : controls.IndexOf(focused);
        switch (action)
        {
            case PadAction.B:
                if (EmuForm.Visibility == Visibility.Visible) { EmuCancel_Click(this, new RoutedEventArgs()); return; }
                audio?.Play("back"); CloseEditor(); return;
            case PadAction.A:
                if (focused is Button b) InvokeEditorButton(b);
                else if (focused is ComboBox cb) cb.IsDropDownOpen = true;
                else if (focused is TextBox) { /* type with the keyboard (or Windows' on-screen keyboard) */ }
                else PadFocus(controls[0]);
                return;
            case PadAction.Left when focused is ComboBox lc && lc.Items.Count > 0:
                lc.SelectedIndex = Math.Max(0, lc.SelectedIndex - 1); return;
            case PadAction.Right when focused is ComboBox rc && rc.Items.Count > 0:
                rc.SelectedIndex = Math.Min(rc.Items.Count - 1, rc.SelectedIndex + 1); return;
            case PadAction.Up or PadAction.Left:
                PadFocus(controls[i <= 0 ? 0 : i - 1]); return;
            case PadAction.Down or PadAction.Right:
                PadFocus(controls[Math.Min(controls.Count - 1, i + 1)]); return;
            case PadAction.RB:
                PadFocus(AddSaveButton); return;
        }
    }

    void InvokeEditorButton(Button b)
    {
        var e = new RoutedEventArgs();
        if (b == AddKindAppButton) AddKindApp_Click(b, e);
        else if (b == AddKindRomButton) AddKindRom_Click(b, e);
        else if (b == AddBrowseButton) AddBrowse_Click(b, e);
        else if (b == AddEmuNewButton) AddEmuNew_Click(b, e);
        else if (b == AddEmuEditButton) AddEmuEdit_Click(b, e);
        else if (b == EmuBrowseButton) EmuBrowse_Click(b, e);
        else if (b == EmuSaveButton) EmuSave_Click(b, e);
        else if (b == EmuCancelButton) EmuCancel_Click(b, e);
        else if (b == EmuDeleteButton) EmuDelete_Click(b, e);
        else if (b == AddCoverButton) AddCover_Click(b, e);
        else if (b == AddCoverClearButton) AddCoverClear_Click(b, e);
        else if (b == AddCoverWebButton) AddCoverWeb_Click(b, e);
        else if (b == AddCoverGenButton) AddCoverGen_Click(b, e);
        else if (b == AddSaveButton) AddSave_Click(b, e);
        else if (b == AddCancelButton) AddCancel_Click(b, e);
        else if (b == AddRemoveButton) AddRemove_Click(b, e);
    }
}
