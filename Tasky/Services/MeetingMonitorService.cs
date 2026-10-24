using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Tasky.Services;

/// <summary>
/// Polls every 30 seconds. A meeting session is recorded only when a known
/// meeting app has an AudioSessionStateActive capture session on the microphone
/// (meaning audio is actually flowing, not just that the app has permission).
/// Falls back to a registry + process check if the audio session API fails.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MeetingMonitorService : IDisposable
{
    private readonly DatabaseService _db;
    private System.Threading.Timer? _timer;
    private bool _inMeeting;
    private DateTime? _sessionStart;
    private int _activeStreak;

    // Substrings matched against process names and audio session identifiers
    private static readonly string[] MeetingSubstrings =
    [
        "teams", "zoom", "skype", "lync", "webex", "slack", "discord",
        "whereby", "ringcentral", "gotomeeting", "googlemeet", "meet",
    ];

    public MeetingMonitorService(DatabaseService db) => _db = db;

    public void Start()
    {
        _timer = new System.Threading.Timer(
            _ => Check(), null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    private void Check()
    {
        // Primary: WASAPI audio session state — truly active audio capture
        // Fallback: registry check if WASAPI is unavailable
        bool detected = IsMeetingAppActivelyCapturingAudio()
                     || IsMeetingAppActivelyUsingCamera();

        if (detected)
        {
            _activeStreak++;
            if (!_inMeeting && _activeStreak >= 2)
            {
                _inMeeting    = true;
                _sessionStart = DateTime.Now.AddSeconds(-60);
            }
        }
        else
        {
            if (_inMeeting && _sessionStart.HasValue)
            {
                try { _db.InsertMeetingSession(_sessionStart.Value, DateTime.Now); }
                catch { }
                _sessionStart = null;
            }
            _inMeeting    = false;
            _activeStreak = 0;
        }
    }

    // ── WASAPI: check if a meeting app has an ACTIVE audio capture session ────

    private static bool IsMeetingAppActivelyCapturingAudio()
    {
        try
        {
            // Create the MMDeviceEnumerator COM object
            var enumeratorType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
            if (enumeratorType is null) return false;
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(enumeratorType)!;

            // eCapture = 1, eCommunications = 1
            if (enumerator.GetDefaultAudioEndpoint(1, 1, out var micDevice) != 0) return false;

            // Activate IAudioSessionManager2 on the capture device
            var asm2Iid = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
            if (micDevice.Activate(ref asm2Iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out var mgrObj) != 0)
                return false;

            var mgr = (IAudioSessionManager2)mgrObj;
            if (mgr.GetSessionEnumerator(out var sessionList) != 0) return false;
            sessionList.GetCount(out int count);

            for (int i = 0; i < count; i++)
            {
                if (sessionList.GetSession(i, out var ctrl) != 0) continue;

                // QI for IAudioSessionControl2 to get state + process id
                if (ctrl is not IAudioSessionControl2 ctrl2) continue;
                if (ctrl2.GetState(out int state) != 0) continue;
                if (state != 1) continue; // AudioSessionStateActive = 1

                if (ctrl2.GetProcessId(out uint pid) != 0) continue;
                try
                {
                    var proc  = Process.GetProcessById((int)pid);
                    var lower = proc.ProcessName.ToLowerInvariant();
                    foreach (var s in MeetingSubstrings)
                        if (lower.Contains(s)) return true;
                }
                catch { }
            }
        }
        catch { }
        return false;
    }

    // ── Registry fallback: camera only (no audio-session API for video) ────────

    private static bool IsMeetingAppActivelyUsingCamera()
        => IsMeetingAppUsingCapabilityRegistry("webcam");

    private static bool IsMeetingAppUsingCapabilityRegistry(string capability)
    {
        try
        {
            // Non-packaged: key name = full exe path with '#' as path separator
            var np = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{capability}\NonPackaged";
            using var npKey = Registry.CurrentUser.OpenSubKey(np);
            if (npKey != null)
                foreach (var name in npKey.GetSubKeyNames())
                {
                    using var sk = npKey.OpenSubKey(name);
                    if (sk?.GetValue("LastUsedTimeStop") is long l && l == 0)
                    {
                        var lower = name.ToLowerInvariant();
                        foreach (var s in MeetingSubstrings)
                            if (lower.Contains(s)) return true;
                    }
                }

            // Packaged: key name = package family name (e.g. MSTeams_8wekyb3d8bbwe)
            var pkg = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{capability}";
            using var pkgKey = Registry.CurrentUser.OpenSubKey(pkg);
            if (pkgKey != null)
                foreach (var name in pkgKey.GetSubKeyNames())
                {
                    if (name == "NonPackaged") continue;
                    using var sk = pkgKey.OpenSubKey(name);
                    if (sk?.GetValue("LastUsedTimeStop") is long l && l == 0)
                    {
                        var lower = name.ToLowerInvariant();
                        foreach (var s in MeetingSubstrings)
                            if (lower.Contains(s)) return true;
                    }
                }
        }
        catch { }
        return false;
    }

    // ── COM interface definitions (exact vtable order is critical) ────────────

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        [PreserveSig] int OpenPropertyStore(uint stgmAccess, out IntPtr ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out uint pdwState);
    }

    // IAudioSessionManager2 inherits IAudioSessionManager (2 methods), adds 5 more
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint flags, out IntPtr ppCtrl);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint flags, out IntPtr ppVol);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator ppSessionList);
        [PreserveSig] int RegisterSessionNotification(IntPtr pNotification);
        [PreserveSig] int UnregisterSessionNotification(IntPtr pNotification);
        [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionID, IntPtr pNotification);
        [PreserveSig] int UnregisterDuckNotification(IntPtr pNotification);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int sessionCount);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl session);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig] int GetState(out int pRetVal);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid ctx);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid ctx);
        [PreserveSig] int GetGroupingParam(out Guid pRetVal);
        [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr pNotification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr pNotification);
    }

    // IAudioSessionControl2 = IAudioSessionControl (9 methods) + 5 more
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int pRetVal);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid ctx);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid ctx);
        [PreserveSig] int GetGroupingParam(out Guid pRetVal);
        [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr pNotification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr pNotification);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
        [PreserveSig] int GetProcessId(out uint pRetVal);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference(bool optOut);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        if (_inMeeting && _sessionStart.HasValue)
        {
            try { _db.InsertMeetingSession(_sessionStart.Value, DateTime.Now); }
            catch { }
        }
    }
}
