using System.Runtime.InteropServices;
using Binder.Library;
using Binder.Services;

namespace Binder;

/// <summary>
/// Game shortcuts (spec §26): with the panel's shortcut still held, a number plays a game. Windows already uses
/// Win+number with every modifier for the taskbar, so the number can't be a hotkey of its own: right after the
/// shortcut, and only while its modifiers are still down, a low-level keyboard hook watches for 0–9. It lets every
/// other key through, swallows only the number it acts on, and is removed when the modifiers come up, after at most
/// <see cref="NumberWaitMs"/>, or when the game's flash has gone. Letting go without a number opens the panel, as the
/// shortcut always did. While the flash waits (<see cref="CatchEscape"/>) the hook also takes Esc, to cancel. No
/// hook exists otherwise: nothing watches the keyboard while idle or while a game runs (the process is gone then).
/// </summary>
internal sealed unsafe partial class MessageWindow
{
    /// <summary>A number (0–9) was pressed after the shortcut, or again while the flash waits.</summary>
    public event Action<int>? GameKeyPressed;
    /// <summary>Esc while the flash waits.</summary>
    public event Action? GameKeyEscape;

    private const uint NumberWaitTimer = 2, NumberWaitMs = 1000;
    private const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
    private const uint VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_ESCAPE = 0x1B, VK_LWIN = 0x5B, VK_RWIN = 0x5C,
        VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5;

    private static MessageWindow? _hooked; // the hook's callback is static; there is one message window
    private IntPtr _hook;
    private uint _modifiers; // the shortcut's, MOD_*
    private bool _gameKeysOn, _waitingForNumber, _catchEscape;
    private readonly HashSet<uint> _swallowed = []; // keys whose press was taken: their repeats and release are too

    /// <summary>Whether a number after the shortcut plays a game (Settings › Game shortcuts).</summary>
    public bool GameKeysOn
    {
        get => _gameKeysOn;
        set
        {
            _gameKeysOn = value;
            if (!value) EndNumberWait();
        }
    }

    /// <summary>The flash is waiting: Esc cancels it, and another number (modifiers still held) switches.</summary>
    public bool CatchEscape
    {
        get => _catchEscape;
        set
        {
            _catchEscape = value;
            UpdateHook();
        }
    }

    /// <summary>The shortcut was pressed: open the panel now, or first wait for a number while its keys are held.</summary>
    private void OnShortcut()
    {
        if (!_gameKeysOn || !ModifiersHeld())
        {
            HotkeyPressed?.Invoke(); // let go already (or game shortcuts off): the panel, at once
            return;
        }
        _waitingForNumber = true;
        UpdateHook();
        if (_hook == IntPtr.Zero)
        {
            _waitingForNumber = false; // Windows refused the hook: the shortcut still opens the panel
            HotkeyPressed?.Invoke();
            return;
        }
        SetTimer(_hwnd, (UIntPtr)NumberWaitTimer, NumberWaitMs, IntPtr.Zero);
    }

    /// <summary>No number within the wait (keys still held): the panel opens, as without game shortcuts.</summary>
    private void OnNumberWaitOver()
    {
        KillTimer(_hwnd, (UIntPtr)NumberWaitTimer);
        if (!_waitingForNumber) return;
        _waitingForNumber = false;
        UpdateHook();
        HotkeyPressed?.Invoke();
    }

    private void EndNumberWait()
    {
        _waitingForNumber = false;
        KillTimer(_hwnd, (UIntPtr)NumberWaitTimer);
        UpdateHook();
    }

    /// <summary>The hook exists only while a number or Esc is awaited.</summary>
    private void UpdateHook()
    {
        bool needed = _hwnd != IntPtr.Zero && (_waitingForNumber || _catchEscape);
        if (needed && _hook == IntPtr.Zero)
        {
            _hooked = this;
            _hook = SetWindowsHookExW(WH_KEYBOARD_LL, &KeyboardProc, GetModuleHandleW(null), 0);
            if (_hook == IntPtr.Zero) _hooked = null;
        }
        else if (!needed && _hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _hooked = null;
            _swallowed.Clear();
        }
    }

    [UnmanagedCallersOnly]
    private static IntPtr KeyboardProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && _hooked is { } self && lParam != IntPtr.Zero)
        {
            try
            {
                var key = (KBDLLHOOKSTRUCT*)lParam;
                if (self.OnKey((int)wParam is WM_KEYDOWN or WM_SYSKEYDOWN, key->vkCode)) return 1;
            }
            catch (Exception e)
            {
                ErrorLog.Write(e, "game keys"); // an exception must not cross into native code
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    /// <summary>
    /// One key going down or up; true takes it away from every app. Quick and without side effects here: Windows
    /// drops a hook that takes too long, so what the key means is handled after returning.
    /// </summary>
    private bool OnKey(bool down, uint vk)
    {
        if (!down)
        {
            if (_swallowed.Remove(vk)) return true;
            if (_waitingForNumber && IsModifier(vk))
            {
                _waitingForNumber = false; // let go without a number: the panel, as the shortcut always did
                _dispatcher.TryEnqueue(() =>
                {
                    KillTimer(_hwnd, (UIntPtr)NumberWaitTimer);
                    UpdateHook();
                    HotkeyPressed?.Invoke();
                });
            }
            return false;
        }
        if (_swallowed.Contains(vk)) return true; // a held key repeating: once is enough
        if (GameKeys.DigitOf(vk) is { } number && (_waitingForNumber || (_catchEscape && ModifiersHeld())))
        {
            _waitingForNumber = false;
            _swallowed.Add(vk);
            _dispatcher.TryEnqueue(() =>
            {
                KillTimer(_hwnd, (UIntPtr)NumberWaitTimer);
                GameKeyPressed?.Invoke(number);
                UpdateHook();
            });
            return true;
        }
        if (vk == VK_ESCAPE && _catchEscape)
        {
            _swallowed.Add(vk);
            _dispatcher.TryEnqueue(() => GameKeyEscape?.Invoke());
            return true;
        }
        return false;
    }

    /// <summary>All of the shortcut's modifier keys are down right now.</summary>
    private bool ModifiersHeld()
    {
        if (_modifiers == 0) return false;
        static bool Down(uint vk) => (GetAsyncKeyState((int)vk) & 0x8000) != 0;
        if ((_modifiers & MOD_WIN) != 0 && !Down(VK_LWIN) && !Down(VK_RWIN)) return false;
        if ((_modifiers & MOD_SHIFT) != 0 && !Down(VK_SHIFT)) return false;
        if ((_modifiers & MOD_CONTROL) != 0 && !Down(VK_CONTROL)) return false;
        if ((_modifiers & MOD_ALT) != 0 && !Down(VK_MENU)) return false;
        return true;
    }

    private bool IsModifier(uint vk) =>
        ((_modifiers & MOD_WIN) != 0 && vk is VK_LWIN or VK_RWIN) ||
        ((_modifiers & MOD_SHIFT) != 0 && vk is VK_SHIFT or VK_LSHIFT or VK_RSHIFT) ||
        ((_modifiers & MOD_CONTROL) != 0 && vk is VK_CONTROL or VK_LCONTROL or VK_RCONTROL) ||
        ((_modifiers & MOD_ALT) != 0 && vk is VK_MENU or VK_LMENU or VK_RMENU);

    private void StopGameKeys()
    {
        _catchEscape = false;
        _waitingForNumber = false;
        KillTimer(_hwnd, (UIntPtr)NumberWaitTimer);
        UpdateHook();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public UIntPtr dwExtraInfo;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetWindowsHookExW(int hookId, delegate* unmanaged<int, IntPtr, IntPtr, IntPtr> proc, IntPtr module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(IntPtr hook);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int key);
}
