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
 readonly CatalogService catalogs=new();readonly LeaderboardService leaderboards=new();readonly SettingsService settings=SettingsService.Load();readonly ObservableCollection<GameEntry> visible=[];
 readonly List<GameEntry> all=[];readonly List<string> categories=[];GamepadService? pads;WebGameRuntime web;NativeGameRuntime native=new();IGameRuntime? active;int categoryIndex;bool gameOpen;
 public MainWindow(){InitializeComponent();GameGrid.ItemsSource=visible;web=new WebGameRuntime(GameWebView);web.Exited+=(s,e)=>DispatcherQueue.TryEnqueue(ReturnToLauncher);web.Status+=s=>DispatcherQueue.TryEnqueue(()=>GameStatus.Text=s.ToUpperInvariant()+" • HOLD SELECT + START TO EXIT");native.Exited+=(s,e)=>DispatcherQueue.TryEnqueue(ReturnToLauncher);Activated+=OnActivated;Root.Loaded+=OnLoaded;}
 void OnActivated(object sender,WindowActivatedEventArgs e){var hwnd=WindowNative.GetWindowHandle(this);var id=Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);AppWindow.GetFromWindowId(id).SetPresenter(AppWindowPresenterKind.FullScreen);}
 async void OnLoaded(object sender,RoutedEventArgs e){pads=new GamepadService(DispatcherQueue);pads.Pressed+=PadPressed;ShowAllToggle.IsOn=settings.ShowAll;await RefreshCatalog();Root.Focus(FocusState.Programmatic);}
 async Task RefreshCatalog(){try{var result=await catalogs.LoadAsync();all.Clear();all.AddRange(result.Catalog.Games.Where(g=>g.Playable));NetworkState.Text=result.Offline?"OFFLINE CACHE":"GRID ONLINE • "+result.Catalog.Version;BuildCategories();ApplyFilter();}catch(Exception ex){NetworkState.Text="CATALOG ERROR";PanelTitle.Text="NO GRID SIGNAL";PanelBody.Text=ex.Message;Panel.Visibility=Visibility.Visible;}}
 void BuildCategories(){categories.Clear();categories.Add("POTATO READY");categories.AddRange(all.Where(g=>settings.ShowAll||g.Potato).Select(g=>g.Category.ToUpperInvariant()).Where(x=>x.Length>0).Distinct().Order());categoryIndex=Math.Min(categoryIndex,categories.Count-1);}
 void ApplyFilter(){var cat=categories.Count==0?"POTATO READY":categories[categoryIndex];CategoryLabel.Text=cat;visible.Clear();foreach(var g in all.Where(g=>(settings.ShowAll||g.Potato)&&(cat=="POTATO READY"?g.Potato:g.Category.Equals(cat,StringComparison.OrdinalIgnoreCase))))visible.Add(g);if(visible.Count>0){GameGrid.SelectedIndex=0;_ = ShowHero(visible[0]);}}
 async Task ShowHero(GameEntry g){HeroName.Text=g.Name;HeroMeta.Text=$"{g.Genre}  •  {g.Status.ToUpperInvariant()}  •  {g.Runtime.Type.ToUpperInvariant()}";HeroDescription.Text=g.Description;try{HeroArt.Source=new BitmapImage(new Uri(g.Art));}catch{HeroArt.Source=null;}var scores=await leaderboards.TopAsync(g.Id);ScoreList.ItemsSource=scores.Select((s,i)=>$"{i+1}. {s.Name}  {s.Score:N0}  [{s.Tag}]");}
 void GameGrid_SelectionChanged(object sender,SelectionChangedEventArgs e){if(GameGrid.SelectedItem is GameEntry g)_=ShowHero(g);}
 async void GameGrid_ItemClick(object sender,ItemClickEventArgs e){if(e.ClickedItem is GameEntry g)await Launch(g);}
 async void Play_Click(object sender,RoutedEventArgs e){if(GameGrid.SelectedItem is GameEntry g)await Launch(g);}
 async Task Launch(GameEntry g){active=web.CanRun(g)?web:native.CanRun(g)?native:null;if(active is null){ShowMessage("RUNTIME ERROR","This game's runtime manifest is incomplete.");return;}gameOpen=true;GameLayer.Visibility=active==web?Visibility.Visible:Visibility.Collapsed;Launcher.Visibility=Visibility.Collapsed;Panel.Visibility=Visibility.Collapsed;try{await active.LaunchAsync(g);}catch(Exception ex){ShowMessage("LAUNCH FAILED",ex.Message);ReturnToLauncher();}}
 async Task ExitGame(){if(!gameOpen)return;var r=active;active=null;gameOpen=false;if(r is not null)await r.ExitAsync();ReturnToLauncher();}
 void ReturnToLauncher(){gameOpen=false;active=null;GameLayer.Visibility=Visibility.Collapsed;Launcher.Visibility=Visibility.Visible;Panel.Visibility=Visibility.Collapsed;GameGrid.Focus(FocusState.Programmatic);}
 void PadPressed(PadAction a)=>DispatcherQueue.TryEnqueue(async()=>{if(a==PadAction.ExitCombo&&gameOpen){await ExitGame();return;}if(gameOpen)return;if(Panel.Visibility==Visibility.Visible){if(a==PadAction.B)ReturnToLauncher();return;}if(a==PadAction.LB)ChangeCategory(-1);else if(a==PadAction.RB)ChangeCategory(1);else if(a==PadAction.A&&GameGrid.SelectedItem is GameEntry g)await Launch(g);else if(a==PadAction.B)ReturnToLauncher();else MoveSelection(a);});
 void MoveSelection(PadAction a){if(visible.Count==0)return;var i=Math.Max(0,GameGrid.SelectedIndex);i=a switch{PadAction.Left=>i-1,PadAction.Right=>i+1,PadAction.Up=>i-3,PadAction.Down=>i+3,_=>i};GameGrid.SelectedIndex=Math.Clamp(i,0,visible.Count-1);GameGrid.ScrollIntoView(GameGrid.SelectedItem);}
 void ChangeCategory(int d){if(categories.Count==0)return;categoryIndex=(categoryIndex+d+categories.Count)%categories.Count;ApplyFilter();}
 void PreviousCategory_Click(object s,RoutedEventArgs e)=>ChangeCategory(-1);void NextCategory_Click(object s,RoutedEventArgs e)=>ChangeCategory(1);
 void Root_KeyDown(object sender,KeyRoutedEventArgs e){if(e.Key==VirtualKey.Escape){if(gameOpen)_=ExitGame();else ReturnToLauncher();e.Handled=true;}else if(e.Key==VirtualKey.Enter&&GameGrid.SelectedItem is GameEntry g){_=Launch(g);e.Handled=true;}}
 async void SignIn_Click(object s,RoutedEventArgs e){gameOpen=true;GameLayer.Visibility=Visibility.Visible;Launcher.Visibility=Visibility.Collapsed;await web.NavigateAsync("https://arcadegridz.com/?host=winapp&mode=signin");}
 void Scores_Click(object s,RoutedEventArgs e)=>ShowMessage("NETWORK SCORES","Choose a game to see its live top five. A full cross-game score browser is planned after v0.1.");
 void Options_Click(object s,RoutedEventArgs e){PanelTitle.Text="OPTIONS";PanelBody.Text="Potato-ready is the safe controller-only list. Enable all games to include titles that may require a keyboard or touch.";ShowAllToggle.Visibility=Visibility.Visible;Panel.Visibility=Visibility.Visible;Launcher.Visibility=Visibility.Collapsed;}
 void Games_Click(object s,RoutedEventArgs e)=>ReturnToLauncher();void Back_Click(object s,RoutedEventArgs e)=>ReturnToLauncher();void Close_Click(object s,RoutedEventArgs e)=>Close();
 void ShowMessage(string title,string body){PanelTitle.Text=title;PanelBody.Text=body;ShowAllToggle.Visibility=Visibility.Collapsed;Panel.Visibility=Visibility.Visible;Launcher.Visibility=Visibility.Collapsed;}
 void ShowAllToggle_Toggled(object s,RoutedEventArgs e){settings.ShowAll=ShowAllToggle.IsOn;settings.Save();BuildCategories();ApplyFilter();}
}
