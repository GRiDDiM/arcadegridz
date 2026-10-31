using Microsoft.UI.Dispatching;
using Windows.Gaming.Input;

namespace ArcadeGridz.App.Services;

public enum PadAction { Up, Down, Left, Right, A, B, LB, RB, ExitCombo }

public sealed class GamepadService : IDisposable
{
    readonly DispatcherQueueTimer timer;
    GamepadButtons previous;
    RawGameController? activeRaw;
    bool[] previousRawButtons = [];
    readonly bool[] previousDirections = new bool[4];
    DateTime comboSince;
    bool comboSent;
    bool loggedNoController;

    public event Action<PadAction>? Pressed;

    public GamepadService(DispatcherQueue queue)
    {
        timer = queue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(33);
        timer.Tick += Tick;
        timer.Start();
    }

    void Tick(DispatcherQueueTimer sender, object args)
    {
        var pad = Gamepad.Gamepads.FirstOrDefault();
        if (pad is not null)
        {
            ReadGamepad(pad);
            return;
        }

        previous = 0;
        var raw = RawGameController.RawGameControllers.FirstOrDefault();
        if (raw is null)
        {
            ResetRaw();
            if (!loggedNoController)
            {
                StartupLog.Write("controller", "No Gamepad or RawGameController detected.");
                loggedNoController = true;
            }
            return;
        }

        loggedNoController = false;
        ReadRawController(raw);
    }

    void ReadGamepad(Gamepad pad)
    {
        if (activeRaw is not null)
            ResetRaw();

        var buttons = pad.GetCurrentReading().Buttons;
        Emit(buttons, GamepadButtons.DPadUp, PadAction.Up);
        Emit(buttons, GamepadButtons.DPadDown, PadAction.Down);
        Emit(buttons, GamepadButtons.DPadLeft, PadAction.Left);
        Emit(buttons, GamepadButtons.DPadRight, PadAction.Right);
        Emit(buttons, GamepadButtons.A, PadAction.A);
        Emit(buttons, GamepadButtons.B, PadAction.B);
        Emit(buttons, GamepadButtons.LeftShoulder, PadAction.LB);
        Emit(buttons, GamepadButtons.RightShoulder, PadAction.RB);
        UpdateExitCombo((buttons & (GamepadButtons.View | GamepadButtons.Menu)) ==
                        (GamepadButtons.View | GamepadButtons.Menu));
        previous = buttons;
    }

    void ReadRawController(RawGameController raw)
    {
        if (!ReferenceEquals(activeRaw, raw))
        {
            activeRaw = raw;
            previousRawButtons = new bool[(int)raw.ButtonCount];
            Array.Clear(previousDirections);
            StartupLog.Write("controller",
                $"Raw controller connected: name={raw.DisplayName}; buttons={raw.ButtonCount}; switches={raw.SwitchCount}; axes={raw.AxisCount}");
            for (var i = 0; i < (int)raw.ButtonCount; i++)
                StartupLog.Write("controller", $"button[{i}] label={raw.GetButtonLabel(i)}");
        }

        var buttons = new bool[(int)raw.ButtonCount];
        var switches = new GameControllerSwitchPosition[(int)raw.SwitchCount];
        var axes = new double[(int)raw.AxisCount];
        raw.GetCurrentReading(buttons, switches, axes);

        // Most inexpensive SNES-style DirectInput adapters expose their D-pad
        // as the first two 0..1 axes. Prefer an actual POV switch when present.
        var up = false;
        var down = false;
        var left = false;
        var right = false;
        if (switches.Length > 0)
        {
            var position = switches[0];
            up = position is GameControllerSwitchPosition.Up or GameControllerSwitchPosition.UpLeft or GameControllerSwitchPosition.UpRight;
            down = position is GameControllerSwitchPosition.Down or GameControllerSwitchPosition.DownLeft or GameControllerSwitchPosition.DownRight;
            left = position is GameControllerSwitchPosition.Left or GameControllerSwitchPosition.UpLeft or GameControllerSwitchPosition.DownLeft;
            right = position is GameControllerSwitchPosition.Right or GameControllerSwitchPosition.UpRight or GameControllerSwitchPosition.DownRight;
        }
        else if (axes.Length >= 2)
        {
            left = axes[0] <= 0.25;
            right = axes[0] >= 0.75;
            up = axes[1] <= 0.25;
            down = axes[1] >= 0.75;
        }

        EmitDirection(0, up, PadAction.Up);
        EmitDirection(1, down, PadAction.Down);
        EmitDirection(2, left, PadAction.Left);
        EmitDirection(3, right, PadAction.Right);

        // Standard 8-button USB/SNES DirectInput order (zero-based):
        // face 0..3, shoulders 4..5, Select 6, Start 7.
        EmitRawButton(buttons, 0, PadAction.A);
        EmitRawButton(buttons, 1, PadAction.B);
        EmitRawButton(buttons, 4, PadAction.LB);
        EmitRawButton(buttons, 5, PadAction.RB);
        UpdateExitCombo(IsPressed(buttons, 6) && IsPressed(buttons, 7));

        for (var i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] && (i >= previousRawButtons.Length || !previousRawButtons[i]))
                StartupLog.Write("controller", $"raw button pressed: index={i}; label={raw.GetButtonLabel(i)}");
        }

        previousRawButtons = buttons;
    }

    void Emit(GamepadButtons buttons, GamepadButtons mask, PadAction action)
    {
        if ((buttons & mask) != 0 && (previous & mask) == 0)
            Pressed?.Invoke(action);
    }

    void EmitRawButton(bool[] buttons, int index, PadAction action)
    {
        if (IsPressed(buttons, index) &&
            (index >= previousRawButtons.Length || !previousRawButtons[index]))
            Pressed?.Invoke(action);
    }

    void EmitDirection(int index, bool active, PadAction action)
    {
        if (active && !previousDirections[index])
            Pressed?.Invoke(action);
        previousDirections[index] = active;
    }

    void UpdateExitCombo(bool held)
    {
        if (held)
        {
            if (comboSince == default)
                comboSince = DateTime.UtcNow;
            if (!comboSent && DateTime.UtcNow - comboSince >= TimeSpan.FromMilliseconds(600))
            {
                comboSent = true;
                Pressed?.Invoke(PadAction.ExitCombo);
            }
        }
        else
        {
            comboSince = default;
            comboSent = false;
        }
    }

    static bool IsPressed(bool[] buttons, int index) =>
        index >= 0 && index < buttons.Length && buttons[index];

    void ResetRaw()
    {
        activeRaw = null;
        previousRawButtons = [];
        Array.Clear(previousDirections);
        comboSince = default;
        comboSent = false;
    }

    public void Dispose() => timer.Stop();
}
