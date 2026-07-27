using System.Runtime.CompilerServices;

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

public enum MemorySourceCapabilities
{
    None = 0,
    ZeroInitialized = 1 << 0,
    SupportsReallocation = 1 << 1,
    RequiresBulkFree = 1 << 2,
    Pinned = 1 << 3,
    // Add more capabilities as needed
}

public struct Region(nuint start, nuint size, RegionState state)
{
    public nuint Start = start;
    public nuint Size = size;
    public RegionState State = state; // Replace with index into capability set or a bitmask representing capabilities
    // Add capability set

    public nuint end_idx => (Start + Size);

    public unsafe Span<byte> this[nuint start, uint size]
    {
        get
        {
            if ((start + size > end_idx) || (start >= Start && start < end_idx))
                throw new ArgumentOutOfRangeException();

            return new Span<byte>((void*)start, (int)size);
        }
    }

    public static (Region left, Region right) Split(ref Region source, nuint slice_At)
    {
        throw new NotImplementedException();
    }

    public static Region Coalesce(Region left, Region right)
    {
        throw new NotImplementedException();
    }

    public Region()
        : this(UIntPtr.Zero, UIntPtr.Zero, RegionState.Null) { }

    public Region(nint intPtr, nuint size) : this()
    {
        Size = size;
    }
}

public enum RegionState // Replace with region capabilities
{
    Null, Reserved, Committed, OnOffer, Locked
}