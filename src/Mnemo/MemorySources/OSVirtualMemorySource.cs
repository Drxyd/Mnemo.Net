using Mnemo.Primitives;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;

namespace Mnemo;

// For starters memory sources will be singletons, later on we'll promote
// them to static classes with attributes that enforce the memory source 
// contract
public sealed class OSVirtualMemorySource : IMemorySource<OSVirtualMemorySource>, IDisposable
{
    private int _disposed;

    // Optional: a static singleton for convenience
    public static OSVirtualMemorySource Instance { get; }

    public static MemorySourceCapabilities Capabilities => throw new NotImplementedException();

    public OSVirtualMemorySource() { }

    public static Region Allocate(nuint size, nuint alignment)
    {
        // Round up to page size? VirtualAlloc already aligns to page boundaries,
        // but we can ensure we ask for at least one page.
        nuint allocationSize = Math.Max(size, (nuint)Environment.SystemPageSize);

        unsafe
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            {
                void* mem = PInvoke.VirtualAlloc(
                null,
                allocationSize,
                VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT | VIRTUAL_ALLOCATION_TYPE.MEM_RESERVE,
                PAGE_PROTECTION_FLAGS.PAGE_READWRITE);

                if (mem == null)
                {
                    throw new OutOfMemoryException($"VirtualAlloc failed for {size} bytes.");
                }
                return new Region((IntPtr) mem, size);
            }
            else throw new Exception();
        }
    }

    public static nint Free(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return -1;

        unsafe
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            {
                BOOL result = PInvoke.VirtualFree(
                (void*)ptr,
                0,                       // release entire region
                VIRTUAL_FREE_TYPE.MEM_RELEASE);
                if (!result)
                {
                    // Log or handle? In release, we might just ignore or throw.
                    // For a robust library, throwing is safer.
                    throw new InvalidOperationException("VirtualFree failed.");
                }
                return 0;
            }
            else throw new Exception();   
        }
    }

    public static bool Free(ref Region region)
    {
        throw new NotImplementedException();
    }

    public static void Commit(nint addr, nuint size)
    {
        throw new NotImplementedException();
    }

    public static void Decommit(nint addr, nuint size)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            // Nothing to clean up in this simple source,
            // but we set the flag to reject further operations.
        }
    }
}