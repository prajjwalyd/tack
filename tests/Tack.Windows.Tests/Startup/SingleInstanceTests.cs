using System.Runtime.InteropServices;
using Tack.Windows.Startup;

namespace Tack.Windows.Tests.Startup;

public unsafe partial class SingleInstanceTests
{
    [Fact]
    public void A_second_claim_on_the_same_name_is_refused()
    {
        var name = @"Local\tack-test-" + Guid.NewGuid().ToString("N");
        using var first = SingleInstance.TryClaim(name);
        Assert.NotNull(first);
        Assert.Null(SingleInstance.TryClaim(name));
    }

    [Fact]
    public void A_mutex_tack_may_not_open_counts_as_another_tack_running()
    {
        var name = @"Local\tack-test-" + Guid.NewGuid().ToString("N");
        // Everyone denied everything: how an elevated Tack's mutex looks to one that is not.
        Assert.True(ConvertStringSecurityDescriptorToSecurityDescriptorW("D:(D;;GA;;;WD)", 1, out var descriptor, 0));
        var attributes = new SecurityAttributes { Length = sizeof(SecurityAttributes), Descriptor = descriptor };
        var mutex = CreateMutexW(&attributes, 0, name);
        try
        {
            Assert.NotEqual(0, mutex);
            Assert.Null(SingleInstance.TryClaim(name));
        }
        finally
        {
            CloseHandle(mutex);
            LocalFree(descriptor);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SecurityAttributes
    {
        public int Length;
        public nint Descriptor;
        public int InheritHandle;
    }

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string text, uint revision, out nint descriptor, nint size);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateMutexW(SecurityAttributes* attributes, int initialOwner, string name);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
