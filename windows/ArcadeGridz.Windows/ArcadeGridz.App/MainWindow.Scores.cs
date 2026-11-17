using ArcadeGridz.App.Models;
using ArcadeGridz.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ArcadeGridz.App;

/// <summary>
/// HI-SCORES: every game's Network leaderboard in one screen. Left: games (scored ones first, with their #1),
/// right: the selected game's full board with the signed-in player's rows marked YOU. Pad: up/down picks a game,
/// LB/RB jump 5, A plays the selected game, B goes back.
/// </summary>
public sealed partial class MainWindow
{
    Dictionary<string, List<ScoreEntry>> scoreBoards = [];
    List<ScoreGameRow> scoreGames = [];

    async void Scores_Click(object sender, RoutedEventArgs e) => await OpenScores();

    async Task OpenScores()
    {
        audio?.Play("open");
        Panel.Visibility = Visibility.Collapsed;
        Launcher.Visibility = Visibility.Collapsed;
        ScoresLayer.Visibility = Visibility.Visible;
        ScoresSummary.Text = "LOADING THE NETWORK BOARDS...";
        var keep = (GameGrid.SelectedItem as GameEntry)?.Id;
        BuildScoreGames(keep);
        PadFocus(ScoresGameList);
        try
        {
            scoreBoards = await leaderboards.AllAsync();
            if (ScoresLayer.Visibility != Visibility.Visible) return;
            BuildScoreGames((ScoresGameList.SelectedItem as ScoreGameRow)?.Game.Id ?? keep);
            UpdateScoresSummary();
        }
        catch (Exception ex)
        {
            StartupLog.Exception("Hi-scores", ex);
            ScoresSummary.Text = "COULDN'T REACH THE NETWORK BOARDS. TRY AGAIN IN A MOMENT.";
        }
    }

    bool IsMine(ScoreEntry s) => account is not null &&
        (s.Name.Equals(account.Name, StringComparison.OrdinalIgnoreCase) ||
         (!string.IsNullOrWhiteSpace(account.Username) && s.Name.Equals(account.Username, StringComparison.OrdinalIgnoreCase)));

    void BuildScoreGames(string? selectId)
    {
        scoreGames = all
            .Where(g => !IsStoryboard(g) && (Listed(g) || scoreBoards.ContainsKey(g.Id) && scoreBoards[g.Id].Count > 0))
            .Select(g =>
            {
                var board = scoreBoards.GetValueOrDefault(g.Id) ?? [];
                var top = board.FirstOrDefault();
                return new ScoreGameRow(g, g.Name, g.Art,
                    top is null ? "NO SCORES YET" : $"#1  {top.Name.ToUpperInvariant()}  ·  {top.Score:N0}",
                    board.Count == 0 ? "" : board.Count == 1 ? "1 SCORE" : $"{board.Count} SCORES",
                    board.Count > 0);
            })
            .OrderByDescending(r => r.HasScores)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ScoresGameList.ItemsSource = scoreGames;
        var pick = scoreGames.FindIndex(r => r.Game.Id == selectId);
        ScoresGameList.SelectedIndex = scoreGames.Count == 0 ? -1 : Math.Max(0, pick);
        if (ScoresGameList.SelectedItem is { } item) ScoresGameList.ScrollIntoView(item);
        ShowScoreBoard();
    }

    void UpdateScoresSummary()
    {
        var scored = scoreBoards.Count(kv => kv.Value.Count > 0);
        var total = scoreBoards.Sum(kv => kv.Value.Count);
        var text = $"{scored} GAMES ON THE BOARDS  ·  {total} SCORES POSTED";
        if (account is not null)
        {
            var firsts = scoreBoards.Count(kv => kv.Value.FirstOrDefault() is { } top && IsMine(top));
            var mine = scoreBoards.Sum(kv => kv.Value.Count(IsMine));
            text += $"  ·  YOU: {mine} SCORES, {firsts} × #1";
        }
        else text += "  ·  SIGN IN TO SEE YOUR OWN SCORES";
        ScoresSummary.Text = text;
    }

    void ShowScoreBoard()
    {
        if (ScoresGameList.SelectedItem is not ScoreGameRow row)
        {
            ScoresGameName.Text = ScoresGameNameShadow.Text = "";
            ScoresBoard.ItemsSource = null;
            return;
        }
        var g = row.Game;
        ScoresGameName.Text = ScoresGameNameShadow.Text = g.Name;
        ScoresGameGenre.Text = g.Genre.ToUpperInvariant();
        try { ScoresArt.Source = new BitmapImage(new Uri(g.Art)); } catch { ScoresArt.Source = null; }
        var board = scoreBoards.GetValueOrDefault(g.Id) ?? [];
        ScoresBoard.ItemsSource = board.Select((s, i) =>
            new ScoreBoardRow(i switch { 0 => "1ST", 1 => "2ND", 2 => "3RD", _ => $"{i + 1}TH" }, s.Name, s.Score.ToString("N0"), s.Tag, IsMine(s))).ToList();
        ScoresEmpty.Visibility = board.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var best = board.Select((s, i) => (s, i)).FirstOrDefault(x => IsMine(x.s));
        ScoresMyBest.Text = account is null ? "SIGN IN TO TRACK YOUR BEST."
            : best.s is null ? "YOU HAVEN'T POSTED A SCORE HERE YET."
            : $"YOUR BEST: {best.s.Score:N0}  ·  RANK #{best.i + 1}";
        ScoresPlayButton.IsEnabled = g.Playable;
        ScoresPlayLabel.Text = g.Playable ? "PLAY" : "UNAVAILABLE";
        ScoresBoardScroll.ChangeView(null, 0, null, true);
    }

    void ScoresGameList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowScoreBoard();

    void ScoresGameList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ScoreGameRow row && !ReferenceEquals(ScoresGameList.SelectedItem, row)) audio?.Play("move");
    }

    async void ScoresPlay_Click(object sender, RoutedEventArgs e) => await PlayFromScores();

    void ScoresBack_Click(object sender, RoutedEventArgs e) { audio?.Play("back"); CloseScores(); }

    async Task PlayFromScores()
    {
        if (ScoresGameList.SelectedItem is not ScoreGameRow { Game: var g } || !g.Playable) { audio?.Play("error"); return; }
        CloseScores();
        if (GameGrid.Items.Contains(g)) GameGrid.SelectedItem = g;
        await Launch(g);
    }

    void CloseScores()
    {
        if (ScoresLayer.Visibility != Visibility.Visible) return;
        ScoresLayer.Visibility = Visibility.Collapsed;
        ReturnToLauncher();
    }

    async Task HandleScoresAction(PadAction action)
    {
        var n = scoreGames.Count;
        var i = ScoresGameList.SelectedIndex;
        int Move(int d) => n == 0 ? -1 : Math.Clamp(i + d, 0, n - 1);
        switch (action)
        {
            case PadAction.B: CloseScores(); return;
            case PadAction.A: await PlayFromScores(); return;
            case PadAction.Up: i = Move(-1); break;
            case PadAction.Down: i = Move(1); break;
            case PadAction.LB: i = Move(-5); break;
            case PadAction.RB: i = Move(5); break;
            default: return;
        }
        if (i >= 0 && i != ScoresGameList.SelectedIndex)
        {
            ScoresGameList.SelectedIndex = i;
            ScoresGameList.ScrollIntoView(ScoresGameList.SelectedItem);
        }
    }
}
