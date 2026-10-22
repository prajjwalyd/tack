using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Tack.Core.Diagnostics;
using Tack.Core.NetBird;

namespace Tack.Windows.NetBird;

/// <summary>
/// This device's NetBird status, from the client's command-line tool
/// (<c>netbird status --json</c>), which talks to the NetBird service for Tack
/// and needs no admin rights. Parsing lives in <see cref="NetBirdStatus"/>.
/// </summary>
public static class NetBirdCli
{
    /// <summary>
    /// The tool answers in well under a second; one that hangs (its service
    /// restarting, say) is given up on rather than holding up the caller.
    /// </summary>
    internal static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    /// <summary>More than any real status, so a misbehaving tool cannot fill memory.</summary>
    internal const int MaxOutput = 4 * 1024 * 1024;

    /// <summary>True when the NetBird client is installed, connected or not.</summary>
    public static bool Installed => Find() != null;

    /// <summary>
    /// This device's NetBird status, or null when NetBird is not installed, not
    /// running, not connected, or too slow to answer. Runs the tool without a
    /// console window; continues on a thread-pool thread.
    /// </summary>
    public static async Task<NetBirdStatus?> StatusAsync()
    {
        if (Find() is not { } cli)
        {
            return null;
        }
        var start = new ProcessStartInfo(cli)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add("status");
        start.ArgumentList.Add("--json");
        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            DevTrace.Write($"netbird: cannot run the tool: {e.Message}");
            return null;
        }
        if (process == null)
        {
            return null;
        }
        using (process)
        {
            process.StandardInput.Close();
            // Stopping the tool at the deadline ends the read and the wait below.
            using var deadline = new CancellationTokenSource(Patience);
            using var stop = deadline.Token.Register(() => Kill(process));
            var output = await CappedOutput.ReadAsync(process.StandardOutput.BaseStream, MaxOutput).ConfigureAwait(false);
            if (output == null)
            {
                Kill(process);
                return null;
            }
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (deadline.IsCancellationRequested || process.ExitCode != 0)
            {
                return null;
            }
            return NetBirdStatus.Parse(Encoding.UTF8.GetString(output));
        }
    }

    /// <summary>
    /// The tool where its installer puts it, and only there: Program Files (asked
    /// of the shell, not read from environment variables, which the user's own
    /// programs can change) is writable by administrators alone, so no program
    /// can plant a fake netbird.exe there to lie about the peers. The tool in
    /// turn checks it talks to the real NetBird service.
    /// </summary>
    static string? Find()
    {
        var cli = PathUnder(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        return cli != null && File.Exists(cli) ? cli : null;
    }

    /// <summary>Where the tool sits under <paramref name="programFiles"/>; null when that folder is unknown.</summary>
    internal static string? PathUnder(string programFiles) =>
        string.IsNullOrWhiteSpace(programFiles) ? null : Path.Combine(programFiles, "NetBird", "netbird.exe");

    static void Kill(Process process)
    {
        try
        {
            process.Kill();
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            // It has already exited.
        }
    }
}
