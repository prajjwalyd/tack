namespace Tack.Core.Notes;

/// <summary>What a note shows.</summary>
/// <param name="Text">The text, at most 20 KiB of UTF-8, with \n line breaks.</param>
/// <param name="Link">The URL, when the whole note is one web link (http or https).</param>
/// <param name="Domain">That link's host without "www.", for the note's caption.</param>
/// <param name="Truncated">The text was longer than the limit and was cut there.</param>
public sealed record NoteBody(string Text, string? Link, string? Domain, bool Truncated);
