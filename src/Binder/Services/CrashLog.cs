using System.Globalization;
using System.Runtime.InteropServices;
using Binder.Library;

namespace Binder.Services;

/// <summary>
/// Reads the Windows event logs for a session's time window (milestone 11): Application Error (1000) and
/// Application Hang (1002) from the Application log, display driver resets (4101) from the System log. Both
/// logs are readable without admin rights. Only called for sessions that ended with an error, once each.
/// </summary>
internal static partial class CrashLog
{
    private const int MaxEvents = 200;

    public static List<WindowsEvent> Read(DateTimeOffset from, DateTimeOffset to)
    {
        var events = new List<WindowsEvent>();
        var window = $"TimeCreated[@SystemTime>='{Utc(from)}' and @SystemTime<='{Utc(to)}']";
        Query("Application", $"*[System[(EventID={CrashAnalysis.ApplicationError} or EventID={CrashAnalysis.ApplicationHang}) and {window}]]", events);
        Query("System", $"*[System[EventID={CrashAnalysis.DisplayReset} and {window}]]", events);
        return events;
    }

    private static string Utc(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static void Query(string log, string xpath, List<WindowsEvent> into)
    {
        var query = EvtQuery(IntPtr.Zero, log, xpath, EvtQueryChannelPath | EvtQueryForwardDirection);
        if (query == IntPtr.Zero) return; // log not readable here: no events, not an error
        try
        {
            var batch = new IntPtr[16];
            var buffer = new byte[16384];
            while (into.Count < MaxEvents && EvtNext(query, batch.Length, batch, 1000, 0, out int returned) && returned > 0)
            {
                for (int i = 0; i < returned; i++)
                {
                    try
                    {
                        if (Render(batch[i], ref buffer) is { } xml && CrashAnalysis.ParseEventXml(xml) is { } e) into.Add(e);
                    }
                    finally
                    {
                        EvtClose(batch[i]);
                    }
                }
            }
        }
        finally
        {
            EvtClose(query);
        }
    }

    private static string? Render(IntPtr evt, ref byte[] buffer)
    {
        if (!EvtRender(IntPtr.Zero, evt, EvtRenderEventXml, buffer.Length, buffer, out int used, out _))
        {
            if (Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer || used > 128 * 1024) return null;
            buffer = new byte[used];
            if (!EvtRender(IntPtr.Zero, evt, EvtRenderEventXml, buffer.Length, buffer, out used, out _)) return null;
        }
        int bytes = Math.Clamp(used - 2, 0, buffer.Length); // UTF-16, without the terminating null
        return System.Text.Encoding.Unicode.GetString(buffer, 0, bytes & ~1);
    }

    private const int EvtQueryChannelPath = 0x1, EvtQueryForwardDirection = 0x100, EvtRenderEventXml = 1;
    private const int ErrorInsufficientBuffer = 122;

    [LibraryImport("wevtapi.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr EvtQuery(IntPtr session, string path, string query, int flags);

    [LibraryImport("wevtapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EvtNext(IntPtr resultSet, int eventsSize, [Out] IntPtr[] events, int timeout, int flags, out int returned);

    [LibraryImport("wevtapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EvtRender(IntPtr context, IntPtr fragment, int flags, int bufferSize, [Out] byte[] buffer, out int bufferUsed, out int propertyCount);

    [LibraryImport("wevtapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EvtClose(IntPtr handle);
}
