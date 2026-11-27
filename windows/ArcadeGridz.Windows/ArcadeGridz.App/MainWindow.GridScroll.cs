using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ArcadeGridz.App;

/// <summary>
/// A themed scrollbar between the game tiles and the banner, so you can see how far into the list you are.
/// It mirrors the GameGrid's own (hidden) ScrollViewer: drag the thumb or click the track to move.
/// </summary>
public sealed partial class MainWindow
{
    bool gridScrollDragging, gridScrollHooked;
    double gridScrollGrab;

    void InitGridScroll()
    {
        GameGrid.Loaded += (_, _) => HookGridScroller();
        GameGrid.SizeChanged += (_, _) => UpdateGridScroll();
        GridScrollTrack.SizeChanged += (_, _) => UpdateGridScroll();
        HookGridScroller();
    }

    void HookGridScroller()
    {
        if (gridScrollHooked) return;
        gridScroller ??= FindChild<ScrollViewer>(GameGrid);
        if (gridScroller is null) return;
        gridScrollHooked = true;
        gridScroller.ViewChanged += (_, _) => UpdateGridScroll();
        gridScroller.SizeChanged += (_, _) => UpdateGridScroll();
        UpdateGridScroll();
    }

    void UpdateGridScroll()
    {
        if (!gridScrollHooked) HookGridScroller(); if (gridScroller is null) return;
        var extent = gridScroller.ExtentHeight; var view = gridScroller.ViewportHeight; var track = GridScrollTrack.ActualHeight;
        var overflow = extent > view + 1 && view > 0;
        GridScrollTrack.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
        if (!overflow || track <= 0) return;
        var room = Math.Max(0, track - 4);
        var thumb = Math.Clamp(room * view / extent, 34, room);
        GridScrollThumb.Height = thumb;
        var max = Math.Max(1, gridScroller.ScrollableHeight);
        GridScrollShift.Y = (room - thumb) * Math.Clamp(gridScroller.VerticalOffset / max, 0, 1);
    }

    void GridScrollTo(double y)
    {
        if (gridScroller is null) return;
        var room = Math.Max(1, GridScrollTrack.ActualHeight - 4 - GridScrollThumb.Height);
        var f = Math.Clamp((y - 2 - gridScrollGrab) / room, 0, 1);
        gridScroller.ChangeView(null, f * gridScroller.ScrollableHeight, null, true);
    }

    void GridScroll_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var y = e.GetCurrentPoint(GridScrollTrack).Position.Y;
        var top = 2 + GridScrollShift.Y;
        // grab the thumb where it was clicked; a click on the track centres the thumb there
        gridScrollGrab = y >= top && y <= top + GridScrollThumb.Height ? y - top : GridScrollThumb.Height / 2;
        gridScrollDragging = GridScrollTrack.CapturePointer(e.Pointer);
        GridScrollTo(y);
        e.Handled = true;
    }

    void GridScroll_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!gridScrollDragging) return;
        GridScrollTo(e.GetCurrentPoint(GridScrollTrack).Position.Y);
        e.Handled = true;
    }

    void GridScroll_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        gridScrollDragging = false;
        GridScrollTrack.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    void GridScroll_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => gridScrollDragging = false;
}
