using Mnemo.Topology;

namespace Mnemo;

public struct AllocatorRecord // Think of a better name
{
    public RegionSet RegionSet;
    public Func<IntPtr, nint> FreeCallback;
    public RuntimeTypeHandle TypeHandle;

    public nuint Start => RegionSet[0].Start;

    public bool Contains(UIntPtr ptr)
        => (nuint)ptr >= Start && (nuint)ptr < Start + RegionSet.CurrentSize;

    public bool RemoveRegion(nuint region_idx)
    {
        throw new NotImplementedException();
    }

    public AllocatorRecord(
        RegionSet region_set,
        Func<IntPtr, nint> free_callback, 
        RuntimeTypeHandle type_handle)
    {
        RegionSet regionSet = region_set;
        FreeCallback = free_callback;
        TypeHandle = type_handle;
    }
}