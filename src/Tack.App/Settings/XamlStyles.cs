using Microsoft.UI.Xaml;
using WinRT;

namespace Tack.App.Settings;

/// <summary>
/// Styles looked up by key from code. A resource comes back as a plain object,
/// and a Native AOT release build may not know it for a <see cref="Style"/>:
/// a C# cast then throws, as it did when the phone card's cast stopped
/// Settings from opening at all. CsWinRT's <c>As</c> asks the object itself,
/// which works in every build.
/// </summary>
static class XamlStyles
{
    /// <summary>A style from the app's resources: Settings' shared ones (SettingsStyles.xaml) or WinUI's own.</summary>
    public static Style App(string key) => In(Application.Current.Resources, key);

    /// <summary>A style from <paramref name="resources"/>, such as a control's own.</summary>
    public static Style In(ResourceDictionary resources, string key) => resources[key].As<Style>();
}
