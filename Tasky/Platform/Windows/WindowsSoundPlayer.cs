using System.IO;
using System.Runtime.Versioning;
using static Tasky.Platform.Windows.NativeMethods;

namespace Tasky.Platform.Windows;

/// <summary>Plays the Windows Media WAV files offered as reminder alerts.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsSoundPlayer : ISoundPlayer
{
    private static readonly string MediaDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    private static readonly (SoundOption Option, string File)[] Sounds =
    [
        (new("notify",    "Windows Notify"),  "Windows Notify.wav"),
        (new("nudge",     "Message Nudge"),   "Windows Message Nudge.wav"),
        (new("calendar",  "Notify Calendar"), "Windows Notify Calendar.wav"),
        (new("email",     "Notify Email"),    "Windows Notify Email.wav"),
        (new("proximity", "Proximity Alert"), "Windows Proximity Notification.wav"),
        (new("logon",     "Windows Logon"),   "Windows Logon.wav"),
    ];

    public IReadOnlyList<SoundOption> Options { get; } =
        [.. Sounds.Select(s => s.Option), new SoundOption(SoundTags.None, "No sound")];

    public void Play(string tag)
    {
        if (tag == SoundTags.None) return;
        try
        {
            var file = Sounds.FirstOrDefault(s => s.Option.Tag == tag).File;
            var path = file is null ? null : Path.Combine(MediaDir, file);
            if (path is not null && File.Exists(path))
                PlaySound(path, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT);
            else
                PlaySound("SystemExclamation", IntPtr.Zero, SND_ALIAS | SND_ASYNC);   // WAV missing
        }
        catch { /* a missing sound must never interrupt a reminder */ }
    }
}
