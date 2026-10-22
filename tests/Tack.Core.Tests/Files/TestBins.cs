using Tack.Core.Files;

namespace Tack.Core.Tests.Files;

/// <summary>Stands in for the Recycle Bin: notes each file it is handed and leaves the file on disk.</summary>
sealed class NotingBin : IRecycler
{
    public List<string> Taken { get; } = [];

    public void Recycle(string path) => Taken.Add(path);
}

/// <summary>A Recycle Bin that refuses everything.</summary>
sealed class BrokenBin : IRecycler
{
    public void Recycle(string path) => throw new IOException("no bin here");
}
