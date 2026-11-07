using GameShelf.Shared;
using static GameShelf.Elevated.Native;

namespace GameShelf.Elevated;

/// <summary>
/// Pauses and restarts the allowlisted services (spec §11, "Exact restore"). Pause, don't reconfigure: a service
/// is only stopped, never given another start type, and only when it's running, accepts a stop and no running
/// service depends on it. Windows has no simple wait for a service's state, so the short stop and start are
/// checked every 250 ms for at most 20 s (only while this short-lived helper runs).
/// </summary>
internal static unsafe class Services
{
    private const int WaitMs = 20_000, StepMs = 250;

    private const uint Access = ServiceState.SERVICE_QUERY_STATUS | ServiceState.SERVICE_QUERY_CONFIG |
                                SERVICE_STOP | SERVICE_START | SERVICE_ENUMERATE_DEPENDENTS;

    /// <summary>Stops one service if all the conditions hold; it's in the journal before it's stopped.</summary>
    public static void Pause(string id, Journal journal)
    {
        if (ElevatedPart.ServiceFor(id) is not { } service) return;
        using var h = Open(service.Service);
        if (h.Handle == IntPtr.Zero)
        {
            Helper.Log($"{service.Name}: not on this PC");
            return;
        }
        var (state, accepted) = ServiceState.Status(h.Handle);
        if (state != ServiceState.SERVICE_RUNNING)
        {
            Helper.Log($"{service.Name}: not running, left as is");
            return;
        }
        if ((accepted & SERVICE_ACCEPT_STOP) == 0)
        {
            Helper.Log($"{service.Name}: doesn't accept a stop now, left running");
            return;
        }
        if (HasRunningDependents(h.Handle))
        {
            Helper.Log($"{service.Name}: other running services depend on it, left running");
            return;
        }

        journal.Services.Add(id);
        journal.Save();
        uint* status = stackalloc uint[7];
        if (!ControlService(h.Handle, SERVICE_CONTROL_STOP, status))
        {
            int error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            journal.Services.Remove(id);
            journal.Save();
            Helper.Log($"{service.Name}: couldn't stop it (error {error}), left running");
            return;
        }
        Helper.Log(WaitFor(h.Handle, ServiceState.SERVICE_STOPPED)
            ? $"{service.Name}: paused"
            : $"{service.Name}: asked to stop, still stopping");
    }

    /// <summary>Starts a service we stopped, unless it runs again already or was disabled meanwhile. False if it didn't start.</summary>
    public static bool Resume(string id)
    {
        if (ElevatedPart.ServiceFor(id) is not { } service) return true;
        using var h = Open(service.Service);
        if (h.Handle == IntPtr.Zero)
        {
            Helper.Log($"{service.Name}: gone, nothing to restart");
            return true;
        }
        var state = ServiceState.CurrentState(h.Handle);
        if (state is ServiceState.SERVICE_RUNNING or SERVICE_START_PENDING)
        {
            Helper.Log($"{service.Name}: already running again");
            return true;
        }
        if (ServiceState.StartType(h.Handle) == ServiceState.SERVICE_DISABLED)
        {
            Helper.Log($"{service.Name}: disabled meanwhile, left as is");
            return true;
        }
        if (state == SERVICE_STOP_PENDING) WaitFor(h.Handle, ServiceState.SERVICE_STOPPED);
        if (!StartServiceW(h.Handle, 0, IntPtr.Zero) &&
            System.Runtime.InteropServices.Marshal.GetLastPInvokeError() is var error && error != ERROR_SERVICE_ALREADY_RUNNING)
        {
            Helper.Log($"{service.Name}: couldn't start it (error {error})");
            return false;
        }
        if (WaitFor(h.Handle, ServiceState.SERVICE_RUNNING))
        {
            Helper.Log($"{service.Name}: running again");
            return true;
        }
        Helper.Log($"{service.Name}: didn't start within 20 s");
        return false;
    }

    private static bool HasRunningDependents(IntPtr service)
    {
        // With no buffer, success means there are none; failing (ERROR_MORE_DATA: there are) counts as "some".
        return !EnumDependentServicesW(service, SERVICE_ACTIVE, IntPtr.Zero, 0, out _, out uint count) || count > 0;
    }

    private static bool WaitFor(IntPtr service, uint wanted)
    {
        long end = Environment.TickCount64 + WaitMs;
        while (true)
        {
            if (ServiceState.CurrentState(service) == wanted) return true;
            if (Environment.TickCount64 >= end) return false;
            Thread.Sleep(StepMs);
        }
    }

    private static ServiceHandle Open(string name)
    {
        var scm = ServiceState.OpenSCManagerW(null, null, ServiceState.SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return new ServiceHandle(IntPtr.Zero, IntPtr.Zero);
        return new ServiceHandle(scm, ServiceState.OpenServiceW(scm, name, Access));
    }

    private readonly record struct ServiceHandle(IntPtr Manager, IntPtr Handle) : IDisposable
    {
        public void Dispose()
        {
            if (Handle != IntPtr.Zero) ServiceState.CloseServiceHandle(Handle);
            if (Manager != IntPtr.Zero) ServiceState.CloseServiceHandle(Manager);
        }
    }
}
