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
}
