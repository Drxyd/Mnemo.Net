using System.Runtime.CompilerServices;

namespace Mnemo;

public static class Mnemo
{
    private static List<MemorySourceRegistration> _sources;

    private static readonly object _lock = new();

    static Mnemo()
    {
        // Just need a reasonably small default
        _sources = new List<MemorySourceRegistration>(8);
    }

    internal static void Register<M>(M memory_source, UIntPtr base_ptr, nuint size)
        where M : IMemorySource<M>
    {
        lock (_lock)
        {
            _sources.Add(new MemorySourceRegistration(base_ptr, size, memory_source.FreePointer));
            _sources.Sort((a, b) => a.Start.CompareTo(b.Start));
        }
    }

    internal static void Update<M>(M memory_source, UIntPtr base_ptr, nuint size)
    {
        lock (_lock)
        {
            int idx = GetSource(base_ptr);
            MemorySourceRegistration mems_r = _sources[idx];
            mems_r.Size = size;
            mems_r.Start = base_ptr;
            _sources[idx] = mems_r;
            _sources.Sort((a, b) => a.Start.CompareTo(b.Start));
        }
        throw new NotImplementedException();
    }

    internal static void Unregister(UIntPtr ptr)
    {
        lock (_lock)
        {
            int idx = GetSource(ptr);
            if (idx == -1)
                throw new InvalidOperationException($"Memory source containting {ptr} not found");
            _sources.RemoveAt(idx);
            _sources.Sort((a, b) => a.Start.CompareTo(b.Start));
        }
    }

    public static void Free(UIntPtr ptr)
    {
        lock (_lock)
        {
            if (ptr == UIntPtr.Zero) 
                return;
            _sources[GetSource(ptr)].FreeCallback(ptr);
        }
    }

    internal static int GetSource(UIntPtr ptr)
    {
        if (_sources == null || _sources.Count == 0)
            return -1;

        int low = 0;
        int high = _sources.Count - 1;

        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            var source = _sources[mid];

            if (source.Contains(ptr))
                return mid;
            if (source.Start < ptr)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return -1;
    }

    internal static unsafe IntPtr RefToNint<T>(ref T reference)
    {
        return (IntPtr) Unsafe.AsPointer<T>(ref reference);
    }
}