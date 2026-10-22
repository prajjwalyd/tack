using System.Reflection;

namespace Tack.App;

/// <summary>Facts fixed when Tack is built.</summary>
static class BuildInfo
{
    /// <summary>
    /// A debug build: it keeps its data in a scratch folder and runs under
    /// names of its own, so it never touches the installed Tack.
    /// </summary>
#if DEBUG
    public const bool IsDevelopment = true;
#else
    public const bool IsDevelopment = false;
#endif

    /// <summary>This version of Tack, as "0.3.0": the build's version, without any source revision after a "+".</summary>
    public static string Version { get; } =
        (typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "")
            .Split('+')[0];
}
