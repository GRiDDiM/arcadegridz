using Windows.Gaming.Input;
using Microsoft.UI.Dispatching;
namespace ArcadeGridz.App.Services;
public enum PadAction { Up,Down,Left,Right,A,B,LB,RB,ExitCombo }
public sealed class GamepadService : IDisposable
{
    readonly DispatcherQueueTimer timer;GamepadButtons previous;DateTime comboSince;bool comboSent;
    public event Action<PadAction>? Pressed;
    public GamepadService(DispatcherQueue queue){timer=queue.CreateTimer();timer.Interval=TimeSpan.FromMilliseconds(33);timer.Tick+=Tick;timer.Start();}
    void Tick(DispatcherQueueTimer s,object e){var pad=Gamepad.Gamepads.FirstOrDefault();if(pad is null){previous=0;return;}var b=pad.GetCurrentReading().Buttons;
      Emit(b,GamepadButtons.DPadUp,PadAction.Up);Emit(b,GamepadButtons.DPadDown,PadAction.Down);Emit(b,GamepadButtons.DPadLeft,PadAction.Left);Emit(b,GamepadButtons.DPadRight,PadAction.Right);
      Emit(b,GamepadButtons.A,PadAction.A);Emit(b,GamepadButtons.B,PadAction.B);Emit(b,GamepadButtons.LeftShoulder,PadAction.LB);Emit(b,GamepadButtons.RightShoulder,PadAction.RB);
      var combo=(b&(GamepadButtons.View|GamepadButtons.Menu))==(GamepadButtons.View|GamepadButtons.Menu);
      if(combo){if(comboSince==default)comboSince=DateTime.UtcNow;if(!comboSent&&DateTime.UtcNow-comboSince>=TimeSpan.FromMilliseconds(600)){comboSent=true;Pressed?.Invoke(PadAction.ExitCombo);}}else{comboSince=default;comboSent=false;}previous=b;}
    void Emit(GamepadButtons b,GamepadButtons mask,PadAction a){if((b&mask)!=0&&(previous&mask)==0)Pressed?.Invoke(a);}
    public void Dispose(){timer.Stop();}
}
