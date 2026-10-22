using System.Collections.Concurrent;
using Tack.Windows.Screenshots;

namespace Tack.Windows.Tests.Screenshots;

/// <summary>
/// What a folder watch reported, collected from its thread-pool threads.
/// Never disposed: a late report from a watch being stopped must not land in a disposed collection.
/// </summary>
sealed class Reports
{
    readonly BlockingCollection<(FolderChange Change, string Path)> _seen = [];

    public Reports(FolderWatch watch)
    {
        watch.Changed += (change, path) => _seen.Add((change, path));
    }

    /// <summary>Waits for <paramref name="change"/> on <paramref name="path"/>, skipping whatever else is reported.</summary>
    public void Expect(FolderChange change, string path, int seconds = 5)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (end > DateTime.UtcNow)
        {
            if (_seen.TryTake(out var got, end - DateTime.UtcNow) && got == (change, path))
            {
                return;
            }
        }
        Assert.Fail($"no {change} for {path}");
    }

    /// <summary>The next report within <paramref name="within"/>, or null when there is none.</summary>
    public (FolderChange Change, string Path)? Next(TimeSpan within) =>
        _seen.TryTake(out var got, within) ? got : null;
}
