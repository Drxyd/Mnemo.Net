namespace Mnemo.Topology;

// Funny... it makes sense for region sets to also support splitting and coalescing, but not states...there might be a design issue here but for now I really think that RegionSet and Region are playing distinct roles where RegionSet is really about Region management whilst Region is about state tracking.

// Remote memory support relies on virtually mapping foreign addresses

// max length = 4, worst case in external state: {committed, locked, onOffer, reserved}
// Internal/External State Rules:
/* 
 * 1. If any region in the set is committed, that region must have the lowest y-index and no other region in that interval can be committed.
 * 2. Only committed regions can be locked, and only if they are the lowest y-index region in the set.
 * 3. Only commited regions can be onOffer and must be preceded either by a committed region in the set or have the lowest y-index in the set.
 * 4. Reserved regions must have the highest y-index in the set.
 * 5. Neighbouring regions with the same state are eagerly coalesced.
 * 6. Region arrays must remain synchronized
 * 
 * The purpose of the above rules is to avoid state fragmentation. For external states the rule set has the added caveat that region states must remain AllocGranularity-aligned, thus internal states are only promoted to external states if they are appropriately scaled and aligned, this occurs at the discretion of the memory source.
 * 
 * Future work should adjust the ruleset to apply at the page level rather than the region level.
*/

public struct RegionSet
{
    public UIntPtr ID;
    private nuint _initialSize;
    private nuint _initialVMapCount;
    private Region[] Regions;

    public nuint InitialSize 
        => _initialSize;

    public nuint CurrentSize
    {
        get
        {
            nuint acc = 0;
            for (int i = 0; i < Regions.Length; i++)
            {
                acc += Regions[i].Size;
            }
            return acc;
        }
    }

    public nuint MaxSize 
        => _initialSize * _initialVMapCount;

    public nuint RegionCount
    {
        get
        {
            nuint acc = 0;
            for(int i = 0; i < Regions.Length; i++)
            {
                if (Regions[i].Start != UIntPtr.Zero)
                    acc++;
            }
            return acc;
        }
    }

    public Region this[uint x]
    {
        get => Regions[x];
    }

    public RegionSet(
        UIntPtr ID, 
        Region initial_region,
        nuint initial_region_size, 
        nuint vmap_count) // Add capabilities and default state parameters
    {
        this.ID = ID;
        Regions = new Region[vmap_count * 4];
        Regions[0] = initial_region;
        _initialSize = initial_region_size;
        _initialVMapCount = vmap_count;
    }

    internal (bool success, (Region region, nuint region_idx) result) GetRegion(nuint sliceAt)
    {
        (Region, nuint) result = (new Region(), 0);
        for(nuint i = 0; i < (nuint)Regions.Length; i++)
        {
            var reg = Regions[i];
            bool contained = reg.Contains(sliceAt);
            if(contained)
                result = (reg, i);
        }
        return (true, result);
    }

    internal enum UpdateRegionResult
    {
        Success,
        RegionNotFound,
        SplitFailed,
        NotEnoughRoomToSplit
    }

    internal bool UpdateRegionState(UIntPtr updateAt, RegionState new_state)
    {
        (bool success, (Region region, nuint region_idx) result) get_region_result = GetRegion(updateAt);

        if (!get_region_result.success)
            return false;

        Region reg = get_region_result.result.region;
        nuint idx = get_region_result.result.region_idx;

        var result = Region.Split(reg, updateAt);
        if(result.success)
        {
            var (left, right) = result.result;
            right.InternalState = new_state;

            // Check if array has room for new region
            if (!(RegionCount + 1 < (nuint)Regions.Length))
                return false; // Not enough room to split
            // Shift regions starting from idx to make room for the new region
            Region[] replacement = Regions;
            for(nuint i = idx; i < (nuint) Regions.Length; i++)
            {
                replacement[i + 1] = Regions[i];
            }
            Regions = replacement;
            return true;
        }
        return false;
    }
} 