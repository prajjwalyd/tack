using Tack.Core.Captures;
using Tack.Core.Pinboard.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

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

    /// <summary>The plan for the drop, with each file's item to read a picture from.</summary>
    public static async Task<(DropPlan Plan, Dictionary<DroppedFile, IStorageItem> Items)> ReadAsync(DataPackageView data)
    {
        var items = new Dictionary<DroppedFile, IStorageItem>(ReferenceEqualityComparer.Instance);
        if (data.Contains(StandardDataFormats.StorageItems))
        {
            foreach (var item in await data.GetStorageItemsAsync())
            {
                var file = item as StorageFile;
                var size = file is null ? 0 : (long)(await file.GetBasicPropertiesAsync()).Size;
                items[new DroppedFile(item.Name, file?.ContentType, size)] = item;
            }
        }
        string? link = null, text = null;
        if (items.Count == 0 && data.Contains(StandardDataFormats.WebLink))
        {
            link = (await data.GetWebLinkAsync())?.OriginalString;
        }
        if (items.Count == 0 && DropRules.FirstAddress(link) is null && data.Contains(StandardDataFormats.Text))
        {
            text = await data.GetTextAsync();
        }
        return (DropRules.Plan([.. items.Keys], link, text), items);
    }

    /// <summary>A dropped picture's bytes, at most 40 MB; null if it cannot be read (a folder, a file gone).</summary>
    public static async Task<byte[]?> BytesAsync(IStorageItem item)
    {
        if (item is not StorageFile file)
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
}
