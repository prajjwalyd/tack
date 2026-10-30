using Tack.Core.Files;
using Tack.Core.Notes;
using Tack.Core.Prints;

namespace Tack.Core.Storage;

/// <summary>Which of last session's prints are put back at startup.</summary>
public static class RestoreCheck
{
    /// <summary>
    /// An entry Tack itself could have written (a picture for a print, a text
    /// file directly in <paramref name="notesFolder"/> for a note, a file or
    /// folder on a drive letter for a linked file) whose file is still there
    /// is tried; one whose file is gone, or that is not what its kind says, is
    /// dropped.
    /// </summary>
    public static Restoring Of(SavedPrint print, string notesFolder)
    {
        var tackWouldPin = print.Kind switch
        {
            Kind.Note => NoteFiles.IsIn(print.Path, notesFolder),
            Kind.File => PathRules.IsOnDrive(print.Path),
            _ => PathRules.IsImage(print.Path),
        };
        if (!tackWouldPin)
        {
            return Restoring.Invalid;
        }
        try
        {
            var folder = File.GetAttributes(print.Path).HasFlag(FileAttributes.Directory);
            return folder && print.Kind != Kind.File ? Restoring.Invalid : Restoring.Try;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return Restoring.Gone;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // No access, or a drive not ready: reading it says more.
            return Restoring.Try;
        }
    }
}
