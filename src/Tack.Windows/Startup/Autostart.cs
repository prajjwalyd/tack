using System.Security;
using Microsoft.Win32;
using Tack.Core;
using Tack.Core.Diagnostics;

namespace Tack.Windows.Startup;

/// <summary>
/// Start with Windows: a value under the current user's Run key holding this
/// executable's quoted path. Per user, so it never needs elevation.
/// </summary>
/// <param name="ValueName">The value's name: Tack's own for a release build, another for a development build.</param>
/// <param name="ExePath">The executable Windows starts.</param>
/// <param name="Key">The key under HKEY_CURRENT_USER holding the value; tests point it elsewhere.</param>
public sealed record Autostart(string ValueName, string ExePath, string Key = Autostart.RunKey)
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The Run value for this build of Tack and this executable.</summary>
    public static Autostart For(AppIdentity identity) => new(identity.RunValueName, Environment.ProcessPath ?? "");

    /// <summary>Whether Tack starts with Windows.</summary>
    public bool Enabled => Command() is not null;

    /// <summary>The command the Run value holds, or null when there is none.</summary>
    string? Command()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(ValueName) as string;
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            DevTrace.Write($"autostart: cannot read the Run key: {e.Message}");
            return null;
        }
    }

    /// <summary>Adds or removes the value. Adding it again rewrites the path, which follows a moved executable.</summary>
    public void Set(bool on)
    {
        try
        {
            if (on)
            {
                if (ExePath.Length == 0)
                {
                    return;
                }
                using var key = Registry.CurrentUser.CreateSubKey(Key);
                key.SetValue(ValueName, $"\"{ExePath}\"", RegistryValueKind.String);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(Key, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            DevTrace.Write($"autostart: cannot write the Run key: {e.Message}");
        }
    }

    /// <summary>
    /// At startup: if Tack starts with Windows from somewhere else, points the
    /// value at this executable. It is written only when it differs, so a
    /// normal start never touches the Run key: an unsigned app rewriting its
    /// Run value at every start is what antivirus flags as persistence.
    /// </summary>
    public void Refresh()
    {
        if (Command() is { } command && !string.Equals(command, $"\"{ExePath}\"", StringComparison.OrdinalIgnoreCase))
        {
            Set(true);
        }
    }
}
