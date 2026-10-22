using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Tack.App.Settings.Sections;

/// <summary>One row of the Shortcuts section: what the shortcut does, its field and the line under it.</summary>
/// <param name="Label">What it does, as the row says it: "Show or hide the board".</param>
/// <param name="Use">The same as the end of a sentence, "to show or hide the board", for "Already used …".</param>
/// <param name="Ring">The border round the field, lit while it listens.</param>
/// <param name="StatusLine">The line under the row, shown only when there is something to say.</param>
sealed record ShortcutRow(string Label, string Use, Button Field, Border Ring, TextBlock Status, FrameworkElement StatusLine);
