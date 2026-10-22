using Tack.Core.Diagnostics;

namespace Tack.App;

/// <summary>
/// Slow work started and left to run on the thread pool: decoding, recycling,
/// waiting out a grace period. Nobody awaits it, so a failure is traced here
/// rather than lost.
/// </summary>
static class Background
{
    /// <summary>Runs <paramref name="work"/> on the thread pool; <paramref name="what"/> names it in the trace if it fails.</summary>
    public static void Run(string what, Func<Task> work) => _ = Task.Run(async () =>
    {
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            DevTrace.Write($"{what} failed: {e}");
        }
    });

    /// <summary>As <see cref="Run(string, Func{Task})"/>, for work that does not wait on anything.</summary>
    public static void Run(string what, Action work) => Run(what, () =>
    {
        work();
        return Task.CompletedTask;
    });
}
