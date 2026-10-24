using System.Runtime.Versioning;

namespace Tasky.Platform.MacOS;

/// <summary>
/// Plays built-in macOS alert sounds. The tags match the Windows sounds,
/// so the chosen alert carries over when data is migrated between computers.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacSoundPlayer : ISoundPlayer
{
    private const string FallbackSound = "Ping";

    private static readonly SoundOption[] Sounds =
    [
        new("notify",    "Glass"),
        new("nudge",     "Pop"),
        new("calendar",  "Tink"),
        new("email",     "Submarine"),
        new("proximity", "Ping"),
        new("logon",     "Hero"),
    ];

    public IReadOnlyList<SoundOption> Options { get; } =
        [.. Sounds, new SoundOption(SoundTags.None, "No sound")];

    public void Play(string tag)
    {
        if (tag == SoundTags.None) return;
        // The label is the system sound's own name.
        var name = Sounds.FirstOrDefault(s => s.Tag == tag)?.Label ?? FallbackSound;
        try
        {
            ObjC.WithPool(() =>
            {
                var sound = ObjC.Send(ObjC.Class("NSSound"), "soundNamed:", ObjC.NSString(name));
                return sound != IntPtr.Zero && ObjC.SendBool(sound, "play");
            });
        }
        catch { /* a missing sound must never interrupt a reminder */ }
    }
}
