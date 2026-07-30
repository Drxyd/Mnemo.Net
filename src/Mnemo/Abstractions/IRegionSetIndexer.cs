using Mnemo.Topology;

namespace Mnemo.Abstractions;

// Memory sources might need their own indexer structures if they have novel topology e.g. CXL or anything non-standard. This interface allows for a generic way to index into a set of regions.
public interface IRegionSetIndexer
{
    int Count { get; }
    bool TryGetPredecessor(nuint key, out nuint value);
    void Insert(RegionSetHandle handle);
    void Remove(RegionSetHandle handle);
}