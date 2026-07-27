using System.Runtime.CompilerServices;

namespace Mnemo;

public static unsafe class Mnemo // Allocator registry and library name
{
    private static readonly List<RegisteredRegion> _regions = new();
    private static readonly object _lock = new();

    internal static void Register<A>(A allocator, IntPtr memory, nuint size)
        where A : IAllocator
    {
        lock (_lock)
        {
            _regions.Add(new RegisteredRegion(memory, size, allocator.Free, typeof(A).TypeHandle)); 
            
            // Keep sorted for binary search
            _regions.Sort((a, b) => a.Start.CompareTo(b.Start));
        }
    }

    internal static void Unregister(IntPtr start)
    {
        lock (_lock)
        {
            _regions.RemoveAll(r => r.Start == (nuint) start);
        }
    }

    public static void Free(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return;
        lock (_lock)
        {
            GetRegion(ptr).FreeCallback(ptr);
        }
    }

    internal static RegisteredRegion GetRegion(IntPtr ptr)
    {
        // Binary search for the region containing ptr
        // Would a custom binary search algorithm and a stored tree be more performant?
        int idx = _regions.BinarySearch(new RegisteredRegion(ptr, 0, null!, default), 
            Comparer<RegisteredRegion>.Create((a, b) => a.Start.CompareTo(b.Start)));

        if (idx < 0) idx = ~idx - 1; // Isn't this an error state?
        if (idx >= 0 && idx < _regions.Count && _regions[idx].Contains((nuint)ptr))
        {
            return _regions[idx];
        }
        else throw new IndexOutOfRangeException($"Pointer 0x{ptr:X} does not belong to any registered allocator.");
    }

#if DEBUG
    public static bool ValidateCast<A>(IntPtr ptr) // use ref instead?
        where A : IAllocator
    {
        return GetRegion(ptr).TypeHandle.Value == typeof(A).TypeHandle.Value;
    }
#endif

    internal static IntPtr RefToNint<T>(ref T reference)
    {
        return (IntPtr) Unsafe.AsPointer<T>(ref reference);
    }
}