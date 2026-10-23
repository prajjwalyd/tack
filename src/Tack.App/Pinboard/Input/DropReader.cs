using Tack.Core.Captures;
using Tack.Core.Pinboard.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using WinRT;

namespace Tack.App.Pinboard.Input;

/// <summary>
/// Reads what was dropped on the board: the files (name, type, size), a link
/// and text, while the drop's data is still there, then a picture's bytes.
/// Nothing is ever fetched from the web.
/// </summary>
static class DropReader
{
    /// <summary>What the drag carries, by kind only: its data is locked until the drop.</summary>
    public static bool Pinnable(DataPackageView data) =>
        DropRules.Pinnable(
            data.Contains(StandardDataFormats.StorageItems),
            data.Contains(StandardDataFormats.WebLink),
            data.Contains(StandardDataFormats.Text));

    /// <summary>The plan for the drop, with each file to read a picture from (null for a folder).</summary>
    public static async Task<(DropPlan Plan, Dictionary<DroppedFile, StorageFile?> Files)> ReadAsync(DataPackageView data)
    {
        var files = new Dictionary<DroppedFile, StorageFile?>(ReferenceEqualityComparer.Instance);
        if (data.Contains(StandardDataFormats.StorageItems))
        {
            foreach (var item in await data.GetStorageItemsAsync())
            {
                var file = FileOf(item);
                var size = file is null ? 0 : (long)(await file.GetBasicPropertiesAsync()).Size;
                files[new DroppedFile(item.Name, file?.ContentType, size)] = file;
            }
        }
        string? link = null, text = null;
        if (files.Count == 0 && data.Contains(StandardDataFormats.WebLink))
        {
            link = (await data.GetWebLinkAsync())?.OriginalString;
        }
        if (files.Count == 0 && DropRules.FirstAddress(link) is null && data.Contains(StandardDataFormats.Text))
        {
            text = await data.GetTextAsync();
        }
        return (DropRules.Plan([.. files.Keys], link, text), files);
    }

    /// <summary>A dropped picture's bytes, at most 40 MB; null if it cannot be read (a folder, a file gone).</summary>
    public static async Task<byte[]?> BytesAsync(StorageFile? file)
    {
        if (file is null)
        {
            return null;
        }
        try
        {
            using var stream = await file.OpenReadAsync();
            if (DroppedImage.TooLarge((long)stream.Size))
            {
                return null;
            }
            var bytes = new byte[stream.Size];
            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)bytes.Length);
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// The item as a file, or null for a folder. Asked of Windows rather than
    /// cast: under Native AOT the item comes as a bare WinRT object, which a C#
    /// cast to <see cref="StorageFile"/> never matches.
    /// </summary>
    static StorageFile? FileOf(IStorageItem item) => item.IsOfType(StorageItemTypes.File) ? item.As<StorageFile>() : null;
}
