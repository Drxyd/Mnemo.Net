namespace Mnemo;

public class LocalSlab<T, M> : IAllocator, IDisposable
    where M : IMemorySource<M>
{
    private bool disposedValue;
    private Region Region;

    public LocalSlab(int block_size, int block_count)
    {
        nuint total_size = (nuint) block_size * (nuint) block_count; // consider allocator structure as well, so add sizeof(AllocatorStructure) to total_size if needed 
        Region = M.Allocate(total_size);

        // Add initialization 
        // Intrusive structures stored within region to manage the blocks, e.g., a free list or bitmap
    }

    protected virtual void Dispose(bool disposing)
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
    // ~LocalSlab()
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

    public static IAllocator Create<S>(params object[] parameters) 
        where S : IMemorySource<S>
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

    public nint Reallocate(nint ptr, nuint oldSize, nuint new_size, nuint alignment = 16U)
    {
        throw new NotImplementedException();
    }
}

#if DEBUG
#endif