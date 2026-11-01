using Microsoft.UI.Dispatching;
using Windows.Gaming.Input;

namespace ArcadeGridz.App.Services;

public enum PadAction { Up, Down, Left, Right, A, B, LB, RB, ExitCombo }

public sealed class GamepadService : IDisposable
{
    const int PollMilliseconds = 16;
    const long InitialRepeatDelayMilliseconds = 320;
    const long RepeatIntervalMilliseconds = 105;
    const double AxisPressLow = 0.28;
    const double AxisReleaseLow = 0.42;
    const double AxisPressHigh = 0.72;
    const double AxisReleaseHigh = 0.58;

    readonly DispatcherQueueTimer timer;
    readonly bool[] directionHeld = new bool[4];
    readonly long[] nextRepeatAt = new long[4];
    GamepadButtons previous;
    RawGameController? activeRaw;
    bool[] rawButtons = [];
    bool[] previousRawButtons = [];
    GameControllerSwitchPosition[] rawSwitches = [];
    double[] rawAxes = [];
    DateTime comboSince;
    bool comboSent;
    bool loggedNoController;

    public event Action<PadAction>? Pressed;

    public GamepadService(DispatcherQueue queue)
    {
        timer = queue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(PollMilliseconds);
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
        EmitDirection(0, (buttons & GamepadButtons.DPadUp) != 0, PadAction.Up);
        EmitDirection(1, (buttons & GamepadButtons.DPadDown) != 0, PadAction.Down);
        EmitDirection(2, (buttons & GamepadButtons.DPadLeft) != 0, PadAction.Left);
        EmitDirection(3, (buttons & GamepadButtons.DPadRight) != 0, PadAction.Right);
        EmitButton(buttons, GamepadButtons.A, PadAction.A);
        EmitButton(buttons, GamepadButtons.B, PadAction.B);
        EmitButton(buttons, GamepadButtons.LeftShoulder, PadAction.LB);
        EmitButton(buttons, GamepadButtons.RightShoulder, PadAction.RB);
        UpdateExitCombo((buttons & (GamepadButtons.View | GamepadButtons.Menu)) ==
                        (GamepadButtons.View | GamepadButtons.Menu));
        previous = buttons;
    }

    void ReadRawController(RawGameController raw)
    {
        if (!ReferenceEquals(activeRaw, raw))
        {
            activeRaw = raw;
            rawButtons = new bool[(int)raw.ButtonCount];
            previousRawButtons = new bool[(int)raw.ButtonCount];
            rawSwitches = new GameControllerSwitchPosition[(int)raw.SwitchCount];
            rawAxes = new double[(int)raw.AxisCount];
            ResetDirections();
            StartupLog.Write("controller",
                $"Raw controller connected: name={raw.DisplayName}; buttons={raw.ButtonCount}; switches={raw.SwitchCount}; axes={raw.AxisCount}");
            for (var i = 0; i < (int)raw.ButtonCount; i++)
                StartupLog.Write("controller", $"button[{i}] label={raw.GetButtonLabel(i)}");
        }

        raw.GetCurrentReading(rawButtons, rawSwitches, rawAxes);

        var up = false;
        var down = false;
        var left = false;
        var right = false;
        if (rawSwitches.Length > 0)
        {
            var position = rawSwitches[0];
            up = position is GameControllerSwitchPosition.Up or GameControllerSwitchPosition.UpLeft or GameControllerSwitchPosition.UpRight;
            down = position is GameControllerSwitchPosition.Down or GameControllerSwitchPosition.DownLeft or GameControllerSwitchPosition.DownRight;
            left = position is GameControllerSwitchPosition.Left or GameControllerSwitchPosition.UpLeft or GameControllerSwitchPosition.DownLeft;
            right = position is GameControllerSwitchPosition.Right or GameControllerSwitchPosition.UpRight or GameControllerSwitchPosition.DownRight;
        }
        else if (rawAxes.Length >= 2)
        {
            left = LowAxisActive(rawAxes[0], directionHeld[2]);
            right = HighAxisActive(rawAxes[0], directionHeld[3]);
            up = LowAxisActive(rawAxes[1], directionHeld[0]);
            down = HighAxisActive(rawAxes[1], directionHeld[1]);
        }

        EmitDirection(0, up, PadAction.Up);
        EmitDirection(1, down, PadAction.Down);
        EmitDirection(2, left, PadAction.Left);
        EmitDirection(3, right, PadAction.Right);

        EmitRawButton(0, PadAction.A);
        EmitRawButton(1, PadAction.B);
        EmitRawButton(4, PadAction.LB);
        EmitRawButton(5, PadAction.RB);
        UpdateExitCombo(IsPressed(rawButtons, 6) && IsPressed(rawButtons, 7));

        for (var i = 0; i < rawButtons.Length; i++)
        {
            if (rawButtons[i] && !previousRawButtons[i])
                StartupLog.Write("controller", $"raw button pressed: index={i}; label={raw.GetButtonLabel(i)}");
        }

        Array.Copy(rawButtons, previousRawButtons, rawButtons.Length);
    }

    static bool LowAxisActive(double value, bool wasHeld) =>
        wasHeld ? value < AxisReleaseLow : value <= AxisPressLow;

    static bool HighAxisActive(double value, bool wasHeld) =>
        wasHeld ? value > AxisReleaseHigh : value >= AxisPressHigh;

    void EmitButton(GamepadButtons buttons, GamepadButtons mask, PadAction action)
    {
        if ((buttons & mask) != 0 && (previous & mask) == 0)
            Pressed?.Invoke(action);
    }

    void EmitRawButton(int index, PadAction action)
    {
        if (IsPressed(rawButtons, index) && !IsPressed(previousRawButtons, index))
            Pressed?.Invoke(action);
    }

    void EmitDirection(int index, bool active, PadAction action)
    {
        var now = Environment.TickCount64;
        if (!active)
        {
            directionHeld[index] = false;
            nextRepeatAt[index] = 0;
            return;
        }

        if (!directionHeld[index])
        {
            directionHeld[index] = true;
            nextRepeatAt[index] = now + InitialRepeatDelayMilliseconds;
            Pressed?.Invoke(action);
            return;
        }

        if (now < nextRepeatAt[index])
            return;

        // Emit at most once per poll. Reset from the current time instead of
        // catching up after a busy UI frame, which prevents queued bursts.
        nextRepeatAt[index] = now + RepeatIntervalMilliseconds;
        Pressed?.Invoke(action);
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

    void ResetDirections()
    {
        Array.Clear(directionHeld);
        Array.Clear(nextRepeatAt);
    }

    void ResetRaw()
    {
        activeRaw = null;
        rawButtons = [];
        previousRawButtons = [];
        rawSwitches = [];
        rawAxes = [];
        ResetDirections();
        comboSince = default;
        comboSent = false;
    }

    public void Dispose() => timer.Stop();
}
