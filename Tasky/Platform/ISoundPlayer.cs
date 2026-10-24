namespace Tasky.Platform;

/// <summary>Plays the reminder alert sounds offered in Settings.</summary>
public interface ISoundPlayer
{
    /// <summary>The sounds this OS offers, in display order. Tags are shared across OSes.</summary>
    IReadOnlyList<SoundOption> Options { get; }

    void Play(string tag);
}

public sealed record SoundOption(string Tag, string Label);

public static class SoundTags
{
    public const string None    = "none";
    public const string Default = "proximity";
}
