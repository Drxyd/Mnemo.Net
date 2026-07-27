namespace Mnemo;
#if DEBUG
public sealed class DebugAllocator<TAllocator> : IAllocator, IAllocatorMetrics, IDisposable
    where TAllocator : IAllocator
{
    private readonly TAllocator _inner;
    private long _allocatedBytes;
    private long _activeAllocs;
    private long _peakBytes;
    private bool disposedValue;
    private readonly HashSet<IntPtr> _livePointers = new(); // double‑free check

    public nuint TotalAllocatedBytes => throw new NotImplementedException();

    public nuint ActiveAllocations => throw new NotImplementedException();

    public nuint PeakAllocatedBytes => throw new NotImplementedException();

    public static IAllocator Create<M>(params object[] parameters) where M : IMemorySource<M>
    {
        throw new NotImplementedException();
    }

    public nint Allocate(nuint size, nuint alignment = 16U)
    {
        throw new NotImplementedException();
    }

    public nint Free(nint ptr)
    {
        throw new NotImplementedException();
    }

    public nint Reallocate(IntPtr ptr, nuint oldSize, nuint new_size, nuint alignment = 16)
    {
        throw new NotImplementedException();
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~DebugAllocator()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    void IDisposable.Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    // … implement Allocate / Free with tracking …
    // on Dispose, check _livePointers.Count == 0 (leak detection)
}
#endif