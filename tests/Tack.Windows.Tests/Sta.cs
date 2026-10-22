using System.Runtime.ExceptionServices;

namespace Tack.Windows.Tests;

/// <summary>Runs test code on a thread of its own that is single-threaded for COM, as Tack's UI thread is.</summary>
static class Sta
{
    public static void Run(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
