using System.Runtime.InteropServices;

namespace Mnemo;

public sealed class PinnedGCMemorySource : 
    IMemorySource<PinnedGCMemorySource>, IDisposable
{
    private readonly byte[] _buffer;
    private readonly GCHandle _handle;
    private bool disposedValue;
    private bool disposedValue1;

    public PinnedGCMemorySource(nuint size)
    {
        _buffer = new byte[size];
        _handle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
    }

    public static PinnedGCMemorySource Instance => throw new NotImplementedException();

    public static MemorySourceCapabilities Capabilities => throw new NotImplementedException();

    public static Region Allocate(nuint size, nuint alignment = 4096U)
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

    public static bool Free(ref Region region)
    {
        throw new NotImplementedException();
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue1)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue1 = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~PinnedGCMemorySource()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}