using System.Runtime.InteropServices;
using GameShelf.Library;
using GameShelf.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace GameShelf;

/// <summary>
/// Entry point. The single-instance check runs before any UI loads (spec §5): a second start (taskbar pin,
/// Start menu, autostart, or the watcher bringing the widget back) forwards its arguments to the running
/// instance's message window and exits right away.
/// </summary>
internal static partial class Program
{
    private const string InstanceMutexName = @"Local\GameShelf.Instance";

    // Held for the process lifetime; the OS releases it when the process exits.
    private static Mutex? _instanceMutex;

    [STAThread]
    private static int Main(string[] args)
    {
        _instanceMutex = new Mutex(true, InstanceMutexName, out bool firstInstance);

        // Used by the uninstaller, after it has quit the running instance: no UI.
        if (args is ["--restore-shortcuts"]) return firstInstance ? RestoreShortcuts() : 2;

        if (!firstInstance)
        {
            // Forward to the running instance. It may still be starting (no window yet), or just exiting (Stay ready
            // off, the installer's --quit): then its mutex comes free and this start carries on as the instance,
            // instead of the click being lost.
            long giveUp = Environment.TickCount64 + 10_000;
            while (true)
            {
                if (Forward(args)) return 0;
                if (TakeOver(_instanceMutex)) break;
                if (Environment.TickCount64 > giveUp) return 1;
            }
        }

        if (args.Contains("--quit") || args.Contains("--memory-report")) return 0; // nothing running to ask
        // "Game mode is on" is only for a widget that is still up: never start WinUI for it while a game runs.
        if (args.Contains("--applied")) return 0;

        App.StartArgs = args;
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }

    /// <summary>Waits a moment for the instance mutex; true once this process holds it (the other one has exited).</summary>
    private static bool TakeOver(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(150);
        }
        catch (AbandonedMutexException)
        {
            return true; // its owner ended without releasing it: ours now
        }
    }

    /// <summary>
    /// Puts every imported shortcut back on the desktop and removes the games made from them, like Settings →
    /// "Restore shortcuts to desktop". Exit code 0 when all are back, 3 when some are still in our folder.
    /// </summary>
    private static int RestoreShortcuts()
    {
        var games = Storage.LoadLibrary();
        var (restored, failed) = ShortcutImporter.RestoreAll(games);
        if (restored > 0)
        {
            try { Storage.SaveLibrary(games); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return failed == 0 ? 0 : 3;
    }

    /// <summary>Sends the arguments (joined with NUL) to the running instance. Empty = "toggle the panel".</summary>
    private static bool Forward(string[] args)
    {
        var target = FindWindowW(MessageWindow.ClassName, null);
        if (target == IntPtr.Zero) return false;
        var text = string.Join('\0', args);
        var bytes = System.Text.Encoding.Unicode.GetBytes(text);
        var data = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
        try
        {
            Marshal.Copy(bytes, 0, data, bytes.Length);
            var copy = new COPYDATASTRUCT { dwData = MessageWindow.CopyDataCommand, cbData = bytes.Length, lpData = data };
            // The running instance may need the foreground to show its window.
            AllowSetForegroundWindow(ASFW_ANY);
            return SendMessageTimeoutW(target, MessageWindow.WM_COPYDATA, IntPtr.Zero, ref copy, SMTO_ABORTIFHUNG, 5000, out _) != IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint ASFW_ANY = unchecked((uint)-1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowW(string className, string? windowName);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessageTimeoutW(IntPtr hwnd, int msg, IntPtr wParam, ref COPYDATASTRUCT data,
        uint flags, uint timeout, out IntPtr result);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);
}
