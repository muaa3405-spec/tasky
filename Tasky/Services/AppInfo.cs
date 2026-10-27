using System.Reflection;

namespace Tasky.Services;

/// <summary>Facts about this build of Tasky and where it is published.</summary>
public static class AppInfo
{
    public const string RepositoryUrl   = "https://github.com/muaa3405-spec/tasky";
    public const string ReleasesPageUrl = RepositoryUrl + "/releases/latest";

    /// <summary>GitHub's public API for the newest published release (no sign-in needed).</summary>
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/muaa3405-spec/tasky/releases/latest";

    /// <summary>The version in Tasky.csproj, e.g. 1.2.0.</summary>
    public static Version Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(0, 0, 0);

    public static string VersionText => Version.ToString(3);
}
