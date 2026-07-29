using System.Runtime.InteropServices;

namespace Mnemo.Primitives;


/*
 * NOTES:
 * 1. Allocators don't access the external state of a region
 * 2. Memory sources are responsible for recoalescing or caching split regions and decising whether
 * to release them back to the OS or GC.
 * 3. Memory sources are responsible for keeping internal and external state in sync by processing 
 * allocator external state change requests.
 * 4. Memory source keeps regions in an interval tree, allocators can maintain references to RegionSets,
 *  a region set is just a collection of indices into the memory source's interval tree. 
 */

/// <summary>
/// Book keeping mechanism that keeps track of the state of a contiguous region of memory.
/// </summary>
/// <param name="start"></param>
/// <param name="size"></param>
/// <param name="state_set"></param>
/// <param name="state"></param>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Region(UIntPtr start, nuint size, RegionState state_set, RegionState state)
{
    public UIntPtr Start = start;
    public nuint Size = size;

    public UIntPtr ExclusiveEnd
        => Size > 0 ? Start + Size : Start;

    // Internal state represents the managing allocators POV
    public RegionState InternalState = state; // Singleton subset of InternalStateSet
    public RegionState InternalStateSet = state_set; // Non-exclusive
    // External state represents the memory sources POV (e.g. GC or OS)
    public RegionState ExternalState = RegionState.None; // Singleton subset of ExternalStateSet
    public RegionState ExternalStateSet = RegionState.None;
    
    /// <summary>
    /// Packs the 4 RegionState fields into a single 32-bit integer for fast single-instruction comparison.
    /// </summary>
    public readonly uint RawState
    {
        get
        {
            return (uint)InternalState
                 | ((uint)InternalStateSet << 8)
                 | ((uint)ExternalState << 16)
                 | ((uint)ExternalStateSet << 24);
        }
    }

    /// <summary>
    /// Compares all internal and external state bits in a single uint comparison.
    /// </summary>
    public readonly bool HasSameState(Region other)
    {
        return this.RawState == other.RawState;
    }

    /// <summary>
    /// Used by allocators to check if a region has the required internal state bits and does not have any forbidden internal state bits.
    /// </summary>
    /// <param name="required"></param>
    /// <param name="forbidden"></param>
    /// <returns></returns>
    public bool CheckInternalPossibleStates(RegionState required, RegionState forbidden)
    {
        bool hasRequired = (InternalStateSet & required) == required;
        bool hasForbidden = (InternalStateSet & forbidden) == RegionState.None;

        return hasRequired && hasForbidden;
    }

    /// <summary>
    /// Splits a region into two regions at the specified slice point. The left region will have the specified size, and the right region will have the remaining size. If the slice point is greater than the size of the source region, both returned regions will be empty.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="slice_At"></param>
    /// <returns></returns>
    public static (Region left, Region right) Split( Region source, nuint slice_At )
    {
        if (slice_At > source.Size)
            return (new Region(), new Region());

        Region left = source;
        left.Size = slice_At;

        Region right = source;
        right.Start = source.Start + slice_At;
        right.Size = ((nuint)source.Size) - slice_At;

        return (left, right);
    }

    /// <summary>
    /// Coalesces two adjacent regions into a single region if they are contiguous and have the same state. If the regions cannot be coalesced, returns false and an empty region.
    /// </summary>
    /// <param name="left"></param>
    /// <param name="right"></param>
    /// <returns></returns>
    public static (bool success, Region region) Coalesce( Region left, Region right )
    {
        ulong left_end = left.Start + left.Size;

        if (left.ExclusiveEnd != right.Start)
            return (false, new Region());

        if (!left.HasSameState(right))
            return (false, new Region());

        Region coalesced = left;
        coalesced.Size += right.Size;

        return (true, coalesced);
    }

    public Region()
        : this(UIntPtr.Zero, UIntPtr.Zero, RegionState.None, RegionState.None) { }
}

/// <summary>
/// Represents the state of a contiguous region of memory. Each state is represented by a bit flag, allowing for combinations of states to be represented. The states can be used to track the allocation, commitment, and other properties of memory regions.
/// </summary>
[Flags]
public enum RegionState : byte
{
    None = 0, 
    Reserve = 1 << 0,
    Commit = 1 << 1,
    Offer = 1 << 2, 
    Lock = 1 << 3
}
