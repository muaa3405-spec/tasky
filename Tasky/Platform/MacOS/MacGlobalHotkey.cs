using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;

namespace Tasky.Platform.MacOS;

/// <summary>
/// Control+Option+T through the Carbon hot-key API — the global-shortcut API that
/// needs no Accessibility permission. (Cmd+Shift+T is avoided: browsers use it to reopen tabs.)
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacGlobalHotkey : IGlobalHotkey
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    private const uint KeyCodeT           = 0x11;        // kVK_ANSI_T
    private const uint OptionKey          = 1 << 11;     // optionKey
    private const uint ControlKey         = 1 << 12;     // controlKey
    private const uint KeyboardEventClass = 0x6B657962;  // 'keyb'
    private const uint HotKeyPressedKind  = 5;           // kEventHotKeyPressed
    private const uint Signature          = 0x54534B59;  // 'TSKY'

    [StructLayout(LayoutKind.Sequential)] private struct EventHotKeyID { public uint Signature; public uint Id; }
    [StructLayout(LayoutKind.Sequential)] private struct EventTypeSpec { public uint EventClass; public uint EventKind; }

    private delegate int EventHandler(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);

    [DllImport(Carbon)] private static extern IntPtr GetApplicationEventTarget();
    [DllImport(Carbon)] private static extern int InstallEventHandler(IntPtr target, EventHandler handler, nuint count,
        EventTypeSpec[] types, IntPtr userData, out IntPtr handlerRef);
    [DllImport(Carbon)] private static extern int RemoveEventHandler(IntPtr handlerRef);
    [DllImport(Carbon)] private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyID id,
        IntPtr target, uint options, out IntPtr hotKeyRef);
    [DllImport(Carbon)] private static extern int UnregisterEventHotKey(IntPtr hotKeyRef);

    // Kept in a field so the garbage collector never frees the native callback.
    private readonly EventHandler _handler;
    private IntPtr _handlerRef, _hotKeyRef;
    private Action? _callback;

    public MacGlobalHotkey() => _handler = OnHotKey;

    public string DisplayName => "Control + Option + T";

    public bool Register(Action callback)
    {
        _callback = callback;
        try
        {
            var target = GetApplicationEventTarget();
            var types  = new[] { new EventTypeSpec { EventClass = KeyboardEventClass, EventKind = HotKeyPressedKind } };
            if (InstallEventHandler(target, _handler, 1, types, IntPtr.Zero, out _handlerRef) != 0) return false;
            var id = new EventHotKeyID { Signature = Signature, Id = 1 };
            return RegisterEventHotKey(KeyCodeT, ControlKey | OptionKey, id, target, 0, out _hotKeyRef) == 0;
        }
        catch (Exception ex)
        {
            Services.CrashLog.Write("Could not register the Quick Add shortcut", ex);
            return false;
        }
    }

    private int OnHotKey(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
    {
        Dispatcher.UIThread.Post(() => _callback?.Invoke());
        return 0;   // noErr
    }

    public void Dispose()
    {
        if (_hotKeyRef  != IntPtr.Zero) UnregisterEventHotKey(_hotKeyRef);
        if (_handlerRef != IntPtr.Zero) RemoveEventHandler(_handlerRef);
        _hotKeyRef = _handlerRef = IntPtr.Zero;
    }
}
