using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GameShelf.Setup;

/// <summary>Windows' own task dialogs: the same look as Windows' built-in prompts, nothing drawn by us.</summary>
internal static unsafe class Dialogs
{
    public const int IdOk = 1, IdCancel = 2, IdRetry = 4, IdClose = 8;
    public const int CommonOk = 0x1, CommonCancel = 0x8, CommonRetry = 0x10, CommonClose = 0x20;

    private const int TDF_ALLOW_DIALOG_CANCELLATION = 0x0008;
    private const int TDF_VERIFICATION_FLAG_CHECKED = 0x0100;
    private const int TDF_SIZE_TO_CONTENT = 0x01000000;
    private const int TDN_CREATED = 0, TDN_VERIFICATION_CLICKED = 8;
    private const int TDM_ENABLE_BUTTON = 0x0400 + 111; // WM_USER + 111
    private const int AppIconId = 32512; // the SDK's resource id for <ApplicationIcon>
    // MAKEINTRESOURCEW(-2) and (-1): the 16-bit values 0xFFFE / 0xFFFF, not a 64-bit -1, which Windows would
    // read as a pointer to text (and crash on).
    private static readonly IntPtr ErrorIcon = new(0xFFFE);   // TD_ERROR_ICON
    private static readonly IntPtr WarningIcon = new(0xFFFF); // TD_WARNING_ICON

    public static string Title => Text.WindowTitle;

    /// <summary>The button pressed, the checkbox, and the chosen radio button (0 when there were none).</summary>
    public sealed record Result(int Button, bool Verified, int Radio = 0);

    /// <summary>
    /// Shows a dialog; <paramref name="buttons"/> are custom (id ≥ 100) and come before the common ones.
    /// <paramref name="details"/> goes in a "Show …" section below the content. With <paramref name="required"/>,
    /// the first custom button stays disabled until the checkbox (<paramref name="verification"/>) is ticked.
    /// </summary>
    public static Result Show(string instruction, string content, (int Id, string Text)[] buttons, int common,
        int defaultButton, string? verification = null, bool verified = false, bool error = false, bool warning = false,
        (string Text, string Show, string Hide)? details = null, bool required = false, (int Id, string Text)[]? radios = null)
    {
        var strings = new List<IntPtr>();
        IntPtr Str(string? s)
        {
            if (s is null) return IntPtr.Zero;
            var p = Marshal.StringToHGlobalUni(s);
            strings.Add(p);
            return p;
        }

        var nativeButtons = new Native.TASKDIALOG_BUTTON[buttons.Length];
        for (int i = 0; i < buttons.Length; i++)
            nativeButtons[i] = new Native.TASKDIALOG_BUTTON { nButtonID = buttons[i].Id, pszButtonText = Str(buttons[i].Text) };
        radios ??= [];
        var nativeRadios = new Native.TASKDIALOG_BUTTON[Math.Max(1, radios.Length)];
        for (int i = 0; i < radios.Length; i++)
            nativeRadios[i] = new Native.TASKDIALOG_BUTTON { nButtonID = radios[i].Id, pszButtonText = Str(radios[i].Text) };
        try
        {
            fixed (Native.TASKDIALOG_BUTTON* pButtons = nativeButtons, pRadios = nativeRadios)
            {
                var config = new Native.TASKDIALOGCONFIG
                {
                    cbSize = (uint)sizeof(Native.TASKDIALOGCONFIG),
                    hInstance = Native.GetModuleHandle(null),
                    dwFlags = TDF_ALLOW_DIALOG_CANCELLATION | TDF_SIZE_TO_CONTENT | (verified ? TDF_VERIFICATION_FLAG_CHECKED : 0),
                    dwCommonButtons = common,
                    pszWindowTitle = Str(Title),
                    hMainIcon = error ? ErrorIcon : warning ? WarningIcon : new IntPtr(AppIconId),
                    pszMainInstruction = Str(instruction),
                    pszContent = Str(content),
                    cButtons = (uint)buttons.Length,
                    pButtons = buttons.Length > 0 ? (IntPtr)pButtons : IntPtr.Zero,
                    nDefaultButton = defaultButton,
                    cRadioButtons = (uint)radios.Length,
                    pRadioButtons = radios.Length > 0 ? (IntPtr)pRadios : IntPtr.Zero,
                    nDefaultRadioButton = radios.Length > 0 ? radios[0].Id : 0, // the first is the default
                    pszVerificationText = Str(verification),
                    pszExpandedInformation = Str(details?.Text),
                    pszCollapsedControlText = Str(details?.Show),
                    pszExpandedControlText = Str(details?.Hide),
                };
                if (required && buttons.Length > 0)
                {
                    config.pfCallback = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr, IntPtr, IntPtr, int>)&GateButton;
                    config.lpCallbackData = buttons[0].Id;
                }
                int button = 0, radio = 0, check = 0;
                Locations.Log($"dialog: {instruction}");
                int hr = Native.TaskDialogIndirect(&config, &button, &radio, &check);
                if (hr < 0) Locations.Log($"dialog failed: 0x{hr:X8}");
                if (hr < 0 && config.hMainIcon == new IntPtr(AppIconId))
                {
                    config.hMainIcon = IntPtr.Zero; // the icon resource wasn't found: show it without one
                    hr = Native.TaskDialogIndirect(&config, &button, &radio, &check);
                }
                if (hr >= 0) return new Result(button, check != 0, radio);
            }
        }
        catch (EntryPointNotFoundException)
        {
            // Common Controls 6 wasn't activated; fall through to a plain message box.
        }
        finally
        {
            foreach (var p in strings) Marshal.FreeHGlobal(p);
        }
        var fallback = Fallback(instruction, required ? content + "\n\nPressing OK means you've read and understood this." : content,
            buttons, common, verified || required);
        return fallback with { Radio = radios is { Length: > 0 } ? radios[0].Id : 0 }; // the default choice
    }

    /// <summary>Enables the gated button (its id is the callback data) only while the checkbox is ticked.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GateButton(IntPtr hwnd, int notification, IntPtr wParam, IntPtr lParam, IntPtr buttonId)
    {
        if (notification == TDN_CREATED)
            Native.SendMessage(hwnd, TDM_ENABLE_BUTTON, buttonId, 0);
        else if (notification == TDN_VERIFICATION_CLICKED)
            Native.SendMessage(hwnd, TDM_ENABLE_BUTTON, buttonId, wParam); // wParam: TRUE when ticked
        return 0; // S_OK
    }

    /// <summary>Plain message box: OK/Cancel maps to the first custom button and Cancel; the checkbox keeps its default.</summary>
    private static Result Fallback(string instruction, string content, (int Id, string Text)[] buttons, int common, bool verified)
    {
        bool canCancel = (common & CommonCancel) != 0 || buttons.Length > 1;
        const uint MB_OKCANCEL = 0x1, MB_ICONINFORMATION = 0x40;
        int r = Native.MessageBox(IntPtr.Zero, instruction + "\n\n" + content, Title, (canCancel ? MB_OKCANCEL : 0) | MB_ICONINFORMATION);
        if (r == IdOk) return new Result(buttons.Length > 0 ? buttons[0].Id : IdOk, verified);
        return new Result(IdCancel, false);
    }

    public static void Error(string instruction, string content) =>
        Show(instruction, content, [], CommonClose, IdClose, error: true);
}
