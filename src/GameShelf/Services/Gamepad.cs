using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Input;

namespace GameShelf.Services;

/// <summary>The buttons the panel reacts to (spec §4).</summary>
public enum PadButton { A, B, X, Y, LeftShoulder, RightShoulder, Menu }

/// <summary>
/// Controller input through XInput (spec §4): Xbox pads and anything Steam Input or DS4Windows presents as one.
/// Costs nothing while idle: it reads the pads only while the panel is the active window and a pad is connected,
/// at 60 Hz for 5 s after the last input and 10 Hz after that, and stops completely when the panel is deactivated.
/// Pads are looked for when the panel is activated and when Windows reports a device change (no polling for them).
/// </summary>
public sealed partial class Gamepad : IDisposable
{
    public event Action<PadButton>? Pressed;
    public event Action<FocusNavigationDirection>? Moved;
    /// <summary>Right stick: pixels to scroll this tick (positive = down).</summary>
    public event Action<double>? Scrolled;

    private static readonly TimeSpan FastInterval = TimeSpan.FromMilliseconds(16), SlowInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan FastFor = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(400), RepeatEvery = TimeSpan.FromMilliseconds(80);
    private const short LeftDeadZone = 7849, RightDeadZone = 8689; // XInput's own XINPUT_GAMEPAD_*_THUMB_DEADZONE
    private const short StickMove = 16384; // half-way: a deliberate push moves focus, a resting stick doesn't
    private const double ScrollPerTick = 28; // at full tilt, in pixels per 16 ms tick (~1700 px/s)

    private readonly DispatcherQueue _dispatcher;
    private readonly bool[] _connected = new bool[MaxPads];
    private readonly ushort[] _buttons = new ushort[MaxPads];
    private DispatcherQueueTimer? _timer;
    private DispatcherQueueTimer? _recheck;
    private bool _active, _disposed;
    private long _lastInput;
    private FocusNavigationDirection? _held;
    private long _repeatAt;

    public Gamepad(DispatcherQueue dispatcher) => _dispatcher = dispatcher;

    /// <summary>Panel activated: look for pads and read them while it stays active.</summary>
    public void Activate()
    {
        if (_disposed) return;
        _active = true;
        Probe();
    }

    /// <summary>Panel deactivated, hidden or closing: stop reading.</summary>
    public void Deactivate()
    {
        _active = false;
        StopTimer();
        _recheck?.Stop();
    }

    /// <summary>
    /// WM_DEVICECHANGE (sent to every top-level window when a device node changes): look again, and once more a
    /// moment later, since XInput may see a newly paired pad a little after Windows reports it.
    /// </summary>
    public void DevicesChanged()
    {
        if (!_active || _disposed) return;
        Probe();
        if (_recheck is null)
        {
            _recheck = _dispatcher.CreateTimer();
            _recheck.IsRepeating = false;
            _recheck.Interval = TimeSpan.FromSeconds(1.5);
            _recheck.Tick += (_, _) => { if (_active) Probe(); };
        }
        _recheck.Stop();
        _recheck.Start();
    }

    private void Probe()
    {
        bool any = false;
        for (uint slot = 0; slot < MaxPads; slot++)
        {
            bool now = XInputGetState(slot, out var state) == 0;
            if (now && !_connected[slot]) _buttons[slot] = state.Gamepad.wButtons; // held when found: not a press
            if (!now) _buttons[slot] = 0;
            _connected[slot] = now;
            any |= now;
        }
        if (any) StartTimer();
        else StopTimer();
    }

    private void StartTimer()
    {
        if (_timer is null)
        {
            _timer = _dispatcher.CreateTimer();
            _timer.IsRepeating = true;
            _timer.Tick += (_, _) => Poll();
        }
        _lastInput = Environment.TickCount64;
        _timer.Interval = FastInterval;
        if (!_timer.IsRunning) _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _held = null;
    }

    private void Poll()
    {
        if (!_active) { StopTimer(); return; }
        long now = Environment.TickCount64;
        bool any = false, input = false;
        ushort pressed = 0, held = 0;
        short lx = 0, ly = 0, ry = 0;
        for (uint slot = 0; slot < MaxPads; slot++)
        {
            if (!_connected[slot]) continue;
            if (XInputGetState(slot, out var state) != 0)
            {
                _connected[slot] = false; // unplugged: no longer read
                _buttons[slot] = 0;
                continue;
            }
            any = true;
            var pad = state.Gamepad;
            pressed |= (ushort)(pad.wButtons & ~_buttons[slot]);
            held |= pad.wButtons;
            _buttons[slot] = pad.wButtons;
            if (Math.Abs((int)pad.sThumbLX) > Math.Abs((int)lx)) lx = pad.sThumbLX;
            if (Math.Abs((int)pad.sThumbLY) > Math.Abs((int)ly)) ly = pad.sThumbLY;
            if (Math.Abs((int)pad.sThumbRY) > Math.Abs((int)ry)) ry = pad.sThumbRY;
            input |= pad.wButtons != 0 || Math.Abs((int)pad.sThumbLX) > LeftDeadZone || Math.Abs((int)pad.sThumbLY) > LeftDeadZone ||
                Math.Abs((int)pad.sThumbRY) > RightDeadZone || Math.Abs((int)pad.sThumbRX) > RightDeadZone;
        }
        if (!any) { StopTimer(); return; }

        if (input) _lastInput = now;
        var interval = now - _lastInput < FastFor.TotalMilliseconds ? FastInterval : SlowInterval;
        if (_timer!.Interval != interval) _timer.Interval = interval;
        double ticks = interval.TotalMilliseconds / FastInterval.TotalMilliseconds;

        // Direction: the D-pad, else the left stick pushed past half-way along its main axis.
        FocusNavigationDirection? direction =
            (held & DpadUp) != 0 ? FocusNavigationDirection.Up :
            (held & DpadDown) != 0 ? FocusNavigationDirection.Down :
            (held & DpadLeft) != 0 ? FocusNavigationDirection.Left :
            (held & DpadRight) != 0 ? FocusNavigationDirection.Right :
            Math.Abs((int)lx) >= Math.Abs((int)ly)
                ? (lx >= StickMove ? FocusNavigationDirection.Right : lx <= -StickMove ? FocusNavigationDirection.Left : null)
                : (ly >= StickMove ? FocusNavigationDirection.Up : ly <= -StickMove ? FocusNavigationDirection.Down : null);
        if (direction != _held)
        {
            _held = direction;
            if (direction is { } first)
            {
                _repeatAt = now + (long)RepeatDelay.TotalMilliseconds;
                Moved?.Invoke(first);
            }
        }
        else if (direction is { } repeat && now >= _repeatAt)
        {
            _repeatAt = now + (long)RepeatEvery.TotalMilliseconds;
            Moved?.Invoke(repeat);
        }

        if ((pressed & ButtonA) != 0) Pressed?.Invoke(PadButton.A);
        if ((pressed & ButtonB) != 0) Pressed?.Invoke(PadButton.B);
        if ((pressed & ButtonX) != 0) Pressed?.Invoke(PadButton.X);
        if ((pressed & ButtonY) != 0) Pressed?.Invoke(PadButton.Y);
        if ((pressed & LeftShoulder) != 0) Pressed?.Invoke(PadButton.LeftShoulder);
        if ((pressed & RightShoulder) != 0) Pressed?.Invoke(PadButton.RightShoulder);
        if ((pressed & Start) != 0) Pressed?.Invoke(PadButton.Menu);

        if (_active && Math.Abs((int)ry) > RightDeadZone) // a press above may have closed the panel
        {
            // Scaled from the dead zone's edge, squared for fine control near the centre.
            double tilt = (Math.Abs((int)ry) - RightDeadZone) / (32767.0 - RightDeadZone);
            Scrolled?.Invoke(-Math.Sign(ry) * tilt * tilt * ScrollPerTick * ticks);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Deactivate();
        _timer = null;
        _recheck = null;
    }

    private const uint MaxPads = 4;
    private const ushort DpadUp = 0x0001, DpadDown = 0x0002, DpadLeft = 0x0004, DpadRight = 0x0008, Start = 0x0010;
    private const ushort LeftShoulder = 0x0100, RightShoulder = 0x0200, ButtonA = 0x1000, ButtonB = 0x2000, ButtonX = 0x4000, ButtonY = 0x8000;

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger, bRightTrigger;
        public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint dwPacketNumber;
        public XInputGamepad Gamepad;
    }

    /// <summary>0 when the pad in that slot is connected (ERROR_DEVICE_NOT_CONNECTED otherwise).</summary>
    [LibraryImport("xinput1_4.dll")]
    private static partial uint XInputGetState(uint userIndex, out XInputState state);
}
