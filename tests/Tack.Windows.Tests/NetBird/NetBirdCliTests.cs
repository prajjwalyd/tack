using Tack.Windows.NetBird;

namespace Tack.Windows.Tests.NetBird;

/// <summary>NetBird itself is never run here: only the rules around running it.</summary>
public sealed class NetBirdCliTests
{
    [Fact]
    public void NetBird_runs_only_from_its_folder_in_Program_Files()
    {
        Assert.Equal(@"C:\Program Files\NetBird\netbird.exe", NetBirdCli.PathUnder(@"C:\Program Files"));
        Assert.Equal(@"D:\Apps\NetBird\netbird.exe", NetBirdCli.PathUnder(@"D:\Apps"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_Program_Files_there_is_no_NetBird(string programFiles)
    {
        Assert.Null(NetBirdCli.PathUnder(programFiles));
    }

    [Fact]
    public void Output_within_the_cap_is_read_whole()
    {
        var bytes = Enumerable.Range(0, 100_000).Select(i => (byte)i).ToArray();

        Assert.Equal(bytes, Read(bytes, cap: bytes.Length));
    }

    [Fact]
    public void Output_past_the_cap_is_refused()
    {
        Assert.Null(Read(new byte[1001], cap: 1000));
    }

    [Fact]
    public void No_output_is_empty()
    {
        var output = Read([], cap: 1000);
        Assert.NotNull(output);
        Assert.Empty(output);
    }

    static byte[]? Read(byte[] bytes, int cap)
    {
        using var stream = new MemoryStream(bytes);
        return CappedOutput.ReadAsync(stream, cap).GetAwaiter().GetResult();
    }
}
