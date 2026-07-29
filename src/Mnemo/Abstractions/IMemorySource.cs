using System.Runtime.CompilerServices;
using Mnemo.Primitives;

namespace Mnemo;

/// <summary>
/// Allocators that are capable of creating sub-allocators and that support a larger 
/// suite memory.h API's such that they can acquire memory and address space from the 
/// OS and offer that memory back
/// </summary>
public interface IMemorySource<T> 
    where T :  IMemorySource<T>
{
    static abstract T Instance { get; }

    static abstract Region Allocate(nuint size, nuint alignment = 4096);

    static abstract bool Free(ref Region region);

    static abstract void Commit(IntPtr addr, nuint size);

    static abstract void Decommit(IntPtr addr, nuint size);

    static abstract MemorySourceCapabilities Capabilities { get; }
}

[Flags]
public enum MemorySourceCapabilities
{
    None = 0,
    ZeroInitialized = 1 << 0,
    SupportsReallocation = 1 << 1,
    RequiresBulkFree = 1 << 2,
    Pinned = 1 << 3, // This is an attribute, not a capability, but it can be useful to know if the memory source supports pinned memory
    // Add more capabilities as needed
}