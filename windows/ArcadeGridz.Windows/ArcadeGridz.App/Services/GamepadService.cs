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
    int rawA = 1, rawB = 0;

    // Axis safety. Generic USB pads (this one has no hat switch, its d-pad is axes 0/1) can read 0.0 (= "left/up
    // held") until their first real HID report, or rest off-centre. Either pins the launcher focus to one row so
    // only left/right seem to work. So: axes are ignored until the pad has sent a real report (any change from the
    // connect snapshot), and a direction that stays held at exactly the same value for StuckMs with no other input
    // is treated as the new centre for that axis until the value moves.
    const long StuckMs = 6000;
    double[] connectAxes = [];
    bool axesArmed;
    readonly double?[] axisNeutral = new double?[2];   // re-centred value per axis (null = 0.5)
    readonly long[] heldSince = new long[4];
    readonly double[] heldValue = new double[4];
    long lastLoggedDirection;

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
        // Network-wide SNES/Nintendo layout (same as 2DISO, Potato Mode and every web game):
        // the RIGHT face button (SNES A, Xbox "B") confirms, the BOTTOM one (SNES B, Xbox "A") goes back.
        EmitButton(buttons, GamepadButtons.B, PadAction.A);
        EmitButton(buttons, GamepadButtons.A, PadAction.B);
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
            raw.GetCurrentReading(rawButtons, rawSwitches, rawAxes);
            connectAxes = (double[])rawAxes.Clone();
            axesArmed = false;
            axisNeutral[0] = axisNeutral[1] = null;
            StartupLog.Write("controller", $"Raw axes at connect: [{string.Join(", ", rawAxes.Select(v => v.ToString("0.000")))}] (ignored until the pad reports)");
            // Generic USB SNES pads ("USB,2-axis 8-button gamepad", iBuffalo/Retrolink 0583:2060 and clones) report raw
            // button 0 = A (right face) and 1 = B (bottom face). Browsers remap these to the standard layout for the web
            // games, but Windows.Gaming.Input hands us the raw order, so confirm/back were swapped in the launcher.
            var snesRaw = raw.HardwareVendorId == 0x0583 || (raw.ButtonCount == 8 && raw.AxisCount == 2 && raw.SwitchCount == 0);
            rawA = snesRaw ? 0 : 1; rawB = snesRaw ? 1 : 0;
            StartupLog.Write("controller",
                $"Raw controller connected: name={raw.DisplayName}; vid=0x{raw.HardwareVendorId:X4}; pid=0x{raw.HardwareProductId:X4}; buttons={raw.ButtonCount}; switches={raw.SwitchCount}; axes={raw.AxisCount}; layout={(snesRaw ? "snes-raw (A=0,B=1)" : "standard (A=1,B=0)")}");
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
            if (!axesArmed)
            {
                var changed = false;
                for (var i = 0; i < rawAxes.Length && i < connectAxes.Length; i++)
                    if (Math.Abs(rawAxes[i] - connectAxes[i]) > 0.001) changed = true;
                for (var i = 0; i < rawButtons.Length; i++)
                    if (rawButtons[i]) changed = true;
                if (changed)
                {
                    axesArmed = true;
                    StartupLog.Write("controller", $"Raw axes live: [{string.Join(", ", rawAxes.Select(v => v.ToString("0.000")))}]");
                }
            }
            if (axesArmed)
            {
                var x = Recentre(0, rawAxes[0]);
                var y = Recentre(1, rawAxes[1]);
                left = LowAxisActive(x, directionHeld[2]);
                right = HighAxisActive(x, directionHeld[3]);
                up = LowAxisActive(y, directionHeld[0]);
                down = HighAxisActive(y, directionHeld[1]);
                var anyButton = rawButtons.Any(b => b);
                left = NotStuck(2, left, rawAxes[0], 0, anyButton);
                right = NotStuck(3, right, rawAxes[0], 0, anyButton);
                up = NotStuck(0, up, rawAxes[1], 1, anyButton);
                down = NotStuck(1, down, rawAxes[1], 1, anyButton);
            }
        }

        EmitDirection(0, up, PadAction.Up);
        EmitDirection(1, down, PadAction.Down);
        EmitDirection(2, left, PadAction.Left);
        EmitDirection(3, right, PadAction.Right);

        // standard-layout indices: 1 = right face button (confirm), 0 = bottom face button (back)
        EmitRawButton(rawA, PadAction.A);
        EmitRawButton(rawB, PadAction.B);
        EmitRawButton(4, PadAction.LB);
        EmitRawButton(5, PadAction.RB);
        UpdateExitCombo((IsPressed(rawButtons, 6) && IsPressed(rawButtons, 7)) || (IsPressed(rawButtons, 8) && IsPressed(rawButtons, 9)));

        for (var i = 0; i < rawButtons.Length; i++)
        {
            if (rawButtons[i] && !previousRawButtons[i])
                StartupLog.Write("controller", $"raw button pressed: index={i}; label={raw.GetButtonLabel(i)}");
        }

        Array.Copy(rawButtons, previousRawButtons, rawButtons.Length);
    }

    // shift a re-centred axis back so its new rest value reads as 0.5
    double Recentre(int axis, double value)
    {
        if (axisNeutral[axis] is not { } n) return value;
        if (Math.Abs(value - n) > 0.2) { axisNeutral[axis] = null; StartupLog.Write("controller", $"Axis {axis} moved off its stuck value; centre restored"); return value; }
        return 0.5;
    }

    bool NotStuck(int dir, bool active, double value, int axis, bool anyButton)
    {
        var now = Environment.TickCount64;
        if (!active) { heldSince[dir] = 0; return false; }
        if (heldSince[dir] == 0 || Math.Abs(value - heldValue[dir]) > 0.001 || anyButton)
        {
            heldSince[dir] = now;
            heldValue[dir] = value;
            return true;
        }
        if (now - heldSince[dir] < StuckMs) return true;
        axisNeutral[axis] = value;
        heldSince[dir] = 0;
        StartupLog.Write("controller", $"Axis {axis} stuck at {value:0.000} for {StuckMs / 1000}s with no other input; treating it as centre");
        return false;
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
            if (now - lastLoggedDirection > 150)
            {
                lastLoggedDirection = now;
                StartupLog.Write("controller", $"dir {action}; axes=[{string.Join(", ", rawAxes.Select(v => v.ToString("0.000")))}]");
            }
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
        Array.Clear(heldSince);
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
