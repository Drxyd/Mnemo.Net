using System.Runtime.InteropServices;

namespace Mnemo.Topology;


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
/// <param name="state_space"></param>
/// <param name="state"></param>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Region(
    UIntPtr start, 
    nuint size,

    RegionState state,
    RegionState state_space,
    RegionState default_state,

    nuint v_map_count = 1, 
    nuint v_map_spacing = 0)
{
    public UIntPtr Start = start;
    public nuint Size = size;
    public nuint VMapCount = v_map_count;
    public nuint VMapSpacing = v_map_spacing ;

    public UIntPtr ExclusiveEnd // Not good enough, virtual mapping means multiple ends and starts
        => Size > 0 ? Start + Size : Start;

    // Internal state represents the managing allocators POV
    public RegionState InternalState = state;
    public RegionState InternalStateSpace = state_space;
    // External state represents the memory sources POV (e.g. GC or OS)
    public RegionState ExternalState = RegionState.None;
    public RegionState ExternalStateSpace = RegionState.None;

    public readonly RegionState DefaultState = default_state;

    /// <summary>
    /// Packs the 4 RegionState fields into a single 32-bit integer for fast single-instruction comparison.
    /// </summary>
    public readonly uint RawState
    {
        get
        {
            return ( (uint) InternalState)
                 | ( (uint) InternalStateSpace << 8)
                 | ( (uint) ExternalState << 16)
                 | ( (uint) ExternalStateSpace << 24);
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
        bool hasRequired = (InternalStateSpace & required) == required;
        bool hasForbidden = (InternalStateSpace & forbidden) == RegionState.None;

        return hasRequired && hasForbidden;
    }

    /// <summary>
    /// Splits a region into two regions at the specified slice point. The left region will have the specified size, and the right region will have the remaining size. If the slice point is greater than the size of the source region, both returned regions will be empty.
    /// </summary>
    /// <param name="source"></param>
    /// <param name="slice_At"></param>
    /// <returns></returns>
    public static (bool success, (Region left, Region right) result) Split(Region source, nuint slice_At )
    {
        if (slice_At > source.Size)
            return (false, (new Region(), new Region()));

        Region left = source;
        left.Size = slice_At;

        Region right = source;
        right.Start = source.Start + slice_At;
        right.Size = ((nuint)source.Size) - slice_At;

        return (true, (left, right));
    }

    /// <summary>
    /// Coalesces two adjacent regions into a single region if they are contiguous and have the same state. If the regions cannot be coalesced, returns false and an empty region.
    /// </summary>
    /// <param name="left"></param>
    /// <param name="right"></param>
    /// <returns></returns>
    public static (bool success, Region result) Coalesce( Region left, Region right )
    {
        ulong left_end = left.Start + left.Size;

        if (left.ExclusiveEnd != right.Start)
            return (false, new Region());

        if (!left.HasSameState(right))
            return (false, new Region());

        if (! (left.VMapCount != right.VMapCount &&
               left.VMapSpacing != right.VMapSpacing))
            return (false, new Region());

        Region coalesced = left;
        coalesced.Size += right
            .Size;

        return (true, coalesced);
    }

    /// <summary>
    /// Used by memory source to remap virtual memory, will be called through RegionSet.Pop().
    /// </summary>
    public static (bool success, (Region bottom, Region top) result) Pop(Region target, nuint depth)
    {
        if (depth >= target.VMapCount || depth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "Depth must be less than the VMapCount of the target region and non-negative.");
            // return (false, (target, new Region()));
        }

        nuint p_vmap_count = target.VMapCount - depth;
        nuint p_start = target.Start + target.VMapSpacing * p_vmap_count;
        Region top = new Region(
            p_start,
            target.Size,
            target.InternalStateSpace,
            target.DefaultState,
            target.DefaultState,
            target.VMapSpacing,
            p_vmap_count);

        return (true, (target, top));
    }

    public bool Contains(nuint address)
    {
        bool acc = false;
        for(nuint i =  0; i < VMapCount; i++)
        {
            nuint vmap_start = (nuint)Start + (VMapSpacing * i);
            nuint vmap_end = vmap_start + Size;
            if (address >= vmap_start && address < vmap_end)
            {
                acc = true;
                break;
            }
        }
        return acc;
    }

    public Region()
        : this(UIntPtr.Zero, UIntPtr.Zero, RegionState.None, RegionState.None, RegionState.None) { }
}

/// <summary>
/// Represents the state of a contiguous region of memory. Each state is represented by a bit flag, allowing for combinations of states to be represented. The states can be used to track the allocation, commitment, and other properties of memory regions.
/// </summary>
[Flags]
public enum RegionState : byte
{
    None = 0,
    Free = 1 << 1,
    Reserved = 1 << 2,
    Commited = 1 << 3,
    OnOffer = 1 << 4, 
    Locked = 1 << 5
}
