using static Binder.Elevated.Native;

namespace Binder.Elevated;

/// <summary>
/// Policy-based QoS through WMI (<c>MSFT_NetQosPolicySettingData</c> in root\StandardCimv2, what
/// <c>New-NetQosPolicy</c> uses), called over COM instead of starting PowerShell. Every policy goes into the
/// temporary ActiveStore, which Windows empties on restart, so a crash can't leave one behind for long; the names
/// all start with <see cref="Prefix"/>, and only those are ever removed (compared ignoring case: Windows keeps
/// ActiveStore names lowercased, "binder smooth discord.exe"). The COM calls go through the vtables
/// directly (no interop wrappers to generate).
/// </summary>
internal sealed unsafe class Qos : IDisposable
{
    public const string Prefix = "Binder ";
    private const string ClassName = "MSFT_NetQosPolicySettingData";
    private const int NetworkProfileAll = 7, DefaultPrecedence = 127;
    private const int WBEM_FLAG_CREATE_ONLY = 0x2, WBEM_FLAG_FORWARD_ONLY = 0x20, WBEM_FLAG_RETURN_IMMEDIATELY = 0x10;
    private const int WBEM_INFINITE = -1;

    private static readonly Guid ClsidLocator = new("4590f811-1d3a-11d0-891f-00aa004b2e24");
    private static readonly Guid IidLocator = new("dc12a687-737f-11cf-884d-00aa004b2e24");
    private static readonly Guid ClsidContext = new("674b6698-ee92-11d0-ad71-00c04fd8fdff");
    private static readonly Guid IidContext = new("44aca674-e8fc-11d0-a07c-00c04fb68820");

    private IntPtr _services, _context;

    private Qos(IntPtr services, IntPtr context)
    {
        _services = services;
        _context = context;
    }

    /// <summary>Connects to the QoS provider with the ActiveStore as the policy store; null (and the HRESULT) if it can't.</summary>
    public static Qos? Connect(out int hr)
    {
        hr = CoCreateInstance(in ClsidLocator, IntPtr.Zero, CLSCTX_INPROC_SERVER, in IidLocator, out var locator);
        if (hr < 0) return null;
        IntPtr services = IntPtr.Zero, context = IntPtr.Zero;
        try
        {
            var ns = SysAllocString(@"ROOT\StandardCimv2");
            try
            {
                // IWbemLocator::ConnectServer(resource, user, password, locale, flags, authority, context, out services)
                hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, IntPtr, IntPtr, IntPtr*, int>)Slot(locator, 3))(
                    locator, ns, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, &services);
            }
            finally
            {
                SysFreeString(ns);
            }
            if (hr < 0) return null;
            hr = CoSetProxyBlanket(services, RPC_C_AUTHN_WINNT, RPC_C_AUTHZ_NONE, IntPtr.Zero, RPC_C_AUTHN_LEVEL_CALL,
                RPC_C_IMP_LEVEL_IMPERSONATE, IntPtr.Zero, EOAC_NONE);
            if (hr < 0) return null;

            // The cmdlets' -PolicyStore is an operation option, which WMI passes as a context value.
            hr = CoCreateInstance(in ClsidContext, IntPtr.Zero, CLSCTX_INPROC_SERVER, in IidContext, out context);
            if (hr < 0) return null;
            var store = new Variant { vt = Variant.VT_BSTR, pointer = SysAllocString("ActiveStore") };
            try
            {
                fixed (char* name = "PolicyStore")
                    // IWbemContext::SetValue(name, flags, value)
                    hr = ((delegate* unmanaged[Stdcall]<IntPtr, char*, int, Variant*, int>)Slot(context, 8))(context, name, 0, &store);
            }
            finally
            {
                VariantClear(&store);
            }
            if (hr < 0) return null;

            var qos = new Qos(services, context);
            services = context = IntPtr.Zero; // owned by qos now
            return qos;
        }
        finally
        {
            Release(services);
            Release(context);
            Release(locator);
        }
    }

    /// <summary>
    /// Adds one policy for every program with this exe name: DSCP marking (0–63), or a send limit in bits per
    /// second. Returns the HRESULT.
    /// </summary>
    public int Create(string policyName, string exeName, int? dscp, ulong? throttleBitsPerSecond)
    {
        IntPtr cls = IntPtr.Zero, instance = IntPtr.Zero;
        var path = SysAllocString(ClassName);
        try
        {
            // IWbemServices::GetObject(path, flags, context, out object, callResult)
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr, IntPtr*, IntPtr, int>)Slot(_services, 6))(
                _services, path, 0, _context, &cls, IntPtr.Zero);
            if (hr < 0) return hr;
            // IWbemClassObject::SpawnInstance(flags, out instance)
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)Slot(cls, 15))(cls, 0, &instance);
            if (hr < 0) return hr;

            if ((hr = PutString(instance, "Name", policyName)) < 0) return hr;
            if ((hr = PutString(instance, "AppPathNameMatchCondition", exeName)) < 0) return hr;
            if ((hr = Put(instance, "NetworkProfile", new Variant { vt = Variant.VT_I4, i4 = NetworkProfileAll })) < 0) return hr;
            if ((hr = Put(instance, "Precedence", new Variant { vt = Variant.VT_I4, i4 = DefaultPrecedence })) < 0) return hr;
            // WMI carries sint8 as VT_I2 and uint64 as a decimal string.
            if (dscp is { } d && (hr = Put(instance, "DSCPAction", new Variant { vt = Variant.VT_I2, i2 = (short)d })) < 0) return hr;
            if (throttleBitsPerSecond is { } bps && (hr = PutString(instance, "ThrottleRateAction", bps.ToString())) < 0) return hr;

            // IWbemServices::PutInstance(instance, flags, context, callResult)
            return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr, IntPtr, int>)Slot(_services, 14))(
                _services, instance, WBEM_FLAG_CREATE_ONLY, _context, IntPtr.Zero);
        }
        finally
        {
            SysFreeString(path);
            Release(instance);
            Release(cls);
        }
    }

    /// <summary>Our policies in the ActiveStore now: name and WMI path. Null if they can't be listed.</summary>
    public List<(string Name, string Path)>? Ours()
    {
        IntPtr enumerator = IntPtr.Zero;
        var language = SysAllocString("WQL");
        var query = SysAllocString("SELECT * FROM " + ClassName); // filtered below: nothing from outside goes into WQL
        try
        {
            // IWbemServices::ExecQuery(language, query, flags, context, out enumerator)
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int, IntPtr, IntPtr*, int>)Slot(_services, 20))(
                _services, language, query, WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY, _context, &enumerator);
            if (hr < 0) return null;
            var found = new List<(string, string)>();
            for (int guard = 0; guard < 4096; guard++)
            {
                IntPtr item = IntPtr.Zero;
                uint returned = 0;
                // IEnumWbemClassObject::Next(timeout, count, out objects, out returned)
                hr = ((delegate* unmanaged[Stdcall]<IntPtr, int, uint, IntPtr*, uint*, int>)Slot(enumerator, 4))(
                    enumerator, WBEM_INFINITE, 1, &item, &returned);
                if (hr < 0) return null;
                if (returned == 0) break;
                try
                {
                    if (GetString(item, "Name") is { } name && name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) &&
                        GetString(item, "__RELPATH") is { } relPath)
                        found.Add((name, relPath));
                }
                finally
                {
                    Release(item);
                }
            }
            return found;
        }
        finally
        {
            SysFreeString(language);
            SysFreeString(query);
            Release(enumerator);
        }
    }

    /// <summary>Removes one policy by its WMI path (from <see cref="Ours"/>). Returns the HRESULT.</summary>
    public int Delete(string relPath)
    {
        var path = SysAllocString(relPath);
        try
        {
            // IWbemServices::DeleteInstance(path, flags, context, callResult)
            return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr, IntPtr, int>)Slot(_services, 16))(
                _services, path, 0, _context, IntPtr.Zero);
        }
        finally
        {
            SysFreeString(path);
        }
    }

    public void Dispose()
    {
        Release(_services);
        Release(_context);
        _services = _context = IntPtr.Zero;
    }

    // ---- COM plumbing -------------------------------------------------------------------------------------

    private static int PutString(IntPtr obj, string name, string value) =>
        Put(obj, name, new Variant { vt = Variant.VT_BSTR, pointer = SysAllocString(value) });

    /// <summary>IWbemClassObject::Put; clears the value afterwards.</summary>
    private static int Put(IntPtr obj, string name, Variant value)
    {
        try
        {
            fixed (char* n = name)
                return ((delegate* unmanaged[Stdcall]<IntPtr, char*, int, Variant*, int, int>)Slot(obj, 5))(obj, n, 0, &value, 0);
        }
        finally
        {
            VariantClear(&value);
        }
    }

    /// <summary>IWbemClassObject::Get of a string property, or null.</summary>
    internal static string? GetString(IntPtr obj, string name)
    {
        var value = new Variant();
        try
        {
            int hr;
            fixed (char* n = name)
                hr = ((delegate* unmanaged[Stdcall]<IntPtr, char*, int, Variant*, int*, int*, int>)Slot(obj, 4))(obj, n, 0, &value, null, null);
            return hr >= 0 && value.vt == Variant.VT_BSTR && value.pointer != IntPtr.Zero ? new string((char*)value.pointer) : null;
        }
        finally
        {
            VariantClear(&value);
        }
    }

    internal static void* Slot(IntPtr obj, int index) => (*(void***)obj)[index];

    internal static void Release(IntPtr obj)
    {
        if (obj != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(obj, 2))(obj);
    }
}
