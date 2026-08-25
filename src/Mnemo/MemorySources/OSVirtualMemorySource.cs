using System.ComponentModel;
using System.Runtime.InteropServices;
using Mnemo.Topology;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.SystemInformation;

namespace Mnemo;


// Regions track interval state. Regions are clustered into region sets that contain contiguous regions managed by a common allocator.
// Allocators split/coalesce regions...
// Multiply mapped physical pages require region state synchronization...

// region state synchronization requires region sets to maintain arrays of arrays of region states as well as keep track of their external state
// The rule region sets enforce is that region arrays be the of the same length, the same size and if they occupy the same y coordinate should also have same state

// For starters memory sources will be singletons, later on we'll promote
// them to static classes with attributes that enforce the memory source 
// contract (non-trivial)
public sealed class OSVirtualMemorySource : IMemorySource<OSVirtualMemorySource>, IDisposable
{
    // Memory sources should reserve address space ahead of time, before creating any allocators.


    private Dictionary<UIntPtr, AllocatorRecord> _allocatorRegistrar;
    private readonly EytzingerArray _indexer;
    private readonly List<RegionSet> _freesets;

    public static MemorySourceCapabilities Capabilities
        => throw new NotImplementedException();

    // Plarform Settings
    private readonly nuint _pageSize;
    private readonly nuint _allocGranularity;
    private int _disposed;


    private static readonly object _lock = new();
    private static OSVirtualMemorySource? _instance;
    public static OSVirtualMemorySource Instance { get => _instance!; }


    public OSVirtualMemorySource(int maxDescriptors = 4096)
    {
        if (maxDescriptors > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(maxDescriptors));

        _allocatorRegistrar = new Dictionary<UIntPtr, AllocatorRecord>(maxDescriptors);
        _indexer = new EytzingerArray(maxDescriptors);
        _pageSize = (nuint)Environment.SystemPageSize; // I'd like to get all page sizes
        _freesets = new List<RegionSet>(maxDescriptors / 2);

        // Need to think about cross-platform support. For now, just Windows.
        GetSystemInfo(out SYSTEM_INFO sysInfo);

        _allocGranularity = sysInfo.dwAllocationGranularity;

        _instance = this;
        Mnemo.Register(this, UIntPtr.Zero, nuint.MaxValue); // Must be called in all memory source constructors
    }

    public RegionState ExternalCapabilities()
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        // Release all remaining sets
        foreach (var alloc_rec in _allocatorRegistrar.Values)
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            {
                unsafe
                {
                    PInvoke.VirtualFree((void*)alloc_rec.Start, 0, VIRTUAL_FREE_TYPE.MEM_RELEASE);
                }
            }
        }
        _allocatorRegistrar.Clear();
    }

    #region Allocator Registration

    internal static void RegisterAllocator<A>(A allocator, RegionSet region_set)
        where A : IAllocator
    {
        lock (_lock)
        {
            OSVirtualMemorySource.Instance._allocatorRegistrar[region_set.ID] =
                new AllocatorRecord(region_set, allocator.Free, typeof(A).TypeHandle);
        }
    }

    internal static void UnregisterAllocator(UIntPtr start)
    {
        lock (_lock)
        {
            OSVirtualMemorySource.Instance._allocatorRegistrar.Remove(start);
        }
    }

    public static void Free(UIntPtr alloc_id, IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return;
        lock (_lock)
        {
            OSVirtualMemorySource.Instance._allocatorRegistrar[alloc_id].FreeCallback(ptr);
        }
    }
    #endregion


    #region Helpers

#if DEBUG
    // Answers the question: "Does the allocator that owns this pointer have the type A?"
    public static bool ValidateCast<A>(IntPtr ptr) // use ref instead? // I forgot why this exists
        where A : IAllocator
    {
        UIntPtr idx = GetIndexofAllocator(ptr);
        return OSVirtualMemorySource.Instance._allocatorRegistrar[idx].TypeHandle.Value == typeof(A).TypeHandle.Value;
    }
#endif

    private static UIntPtr GetIndexofAllocator(IntPtr ptr)
    {
        lock (_lock)
        {
            OSVirtualMemorySource.Instance._indexer.TryGetPredecessor((nuint)ptr, out nuint setId);
            return setId; // Fix types to avoid cast
        }
        throw new InvalidOperationException($"Allocator not found for pointer {ptr}");
    }

    private static nuint AlignUp(nuint value, nuint alignment)
        => (value + alignment - 1) & ~(alignment - 1);


    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern void GetSystemInfo(out SYSTEM_INFO lpSystemInfo);
    #endregion


    #region Public API

    public RegionSetHandle Reserve<A>(A allocator, nuint size, nuint vmap_count = 1)
        where A : IAllocator
    {
        size = AlignUp(size, _allocGranularity);
        UIntPtr res_ptr = UIntPtr.Zero;
        unsafe
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            {
                void* ptr = PInvoke.VirtualAlloc(
                    null,
                    size * vmap_count,
                    VIRTUAL_ALLOCATION_TYPE.MEM_RESERVE,
                    PAGE_PROTECTION_FLAGS.PAGE_READWRITE);

                if (ptr == null)
                    return new RegionSetHandle(0, 0); // ERROR

                res_ptr = (UIntPtr)ptr;
            }
            else throw new Exception();
        }
        RegionState external_states = ExternalCapabilities();

        Region initial_region = new Region(
            res_ptr,
            size,
            external_states,
            RegionState.Reserve,
            RegionState.Reserve,
            vmap_count,
            size / vmap_count);

        RegionSet reg_set = new RegionSet(res_ptr, initial_region, size / vmap_count, vmap_count);

        AllocatorRecord alloc_rec = new AllocatorRecord(
            reg_set,
            allocator.Free,
            typeof(A).TypeHandle);

        nuint setId = (nuint)_allocatorRegistrar.Count;
        _allocatorRegistrar[res_ptr] = alloc_rec;

        // Indexer stores: Key = start address, Value = Set ID
        _indexer.Insert(new RegionSetHandle((nuint)res_ptr, (nuint)setId));

        return new RegionSetHandle(0, setId); // Allocator-facing handle
    }

    public bool Commit(RegionSetHandle handle, UIntPtr comm_start, nuint size)
    {
        nuint setId = handle.Value;

        if (!_allocatorRegistrar.ContainsKey(setId))
            return false; // Ownership violation
        if (false)
        {
            // Validate that the commit request is consistent with the owned regionset
        }

        size = AlignUp(size, _pageSize);

        unsafe
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            {
                void* ptr = PInvoke.VirtualAlloc(
                    (void*)comm_start,
                    size,
                    VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT,
                    PAGE_PROTECTION_FLAGS.PAGE_READWRITE);

                if (ptr == null)
                    return false;
            }
        }

        // Sync state: external reality now matches internal intent
        // Update the region's state to reflect that it is now committed
        // Split region to match the committed size

        return true;
    }

    public bool Decommit(RegionSetHandle handle, UIntPtr decomm_start, nuint decomm_size)
    {
        nuint setId = handle.Value;
        if (!_allocatorRegistrar.TryGetValue(setId, out var set) || !set.Contains(decomm_start))
            return false;


        unsafe
        { // Check for page sized alignment, else defer decommit
            if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            {
                BOOL ok = PInvoke.VirtualFree(
                    (void*)decomm_start,
                    decomm_size,
                    VIRTUAL_FREE_TYPE.MEM_DECOMMIT);

                if (!ok) return false;
            }
        }

        // Update region state and coalesce

        return true;
    }

    public bool Release(RegionSetHandle handle, UIntPtr rel_start)
    {
        uint setId = (uint)handle.Value;
        if (!_allocatorRegistrar.TryGetValue(setId, out var set) || !set.Contains(setId))
            return false;

        // Full release (MEM_RELEASE requires size = 0 and base address)
        unsafe
        {
            if(OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            {
                BOOL ok = PInvoke.VirtualFree(
                (void*)rel_start,
                0,
                VIRTUAL_FREE_TYPE.MEM_RELEASE);

                if (!ok) return false;
            }
        }

        // Remove from indexer
        _indexer.Remove(new RegionSetHandle(rel_start, setId));
        // Remove from registrar
        _allocatorRegistrar.Remove(setId);

        return true;
    }
    #endregion


    #region Source internal topology operations

    public (ushort left, ushort right) Split( // Caller should actually specify the state transition
        RegionSetHandle handle,
        UIntPtr sliceAt)
    {
        nuint setId = handle.Value;
        if (!_allocatorRegistrar.TryGetValue(setId, out AllocatorRecord alloc_record))
            throw new InvalidOperationException("Region not owned by this set");
         
        (bool succes, (Region source, nuint region_idx)) = alloc_record.RegionSet.GetRegion(sliceAt);

        sliceAt = AlignUp(sliceAt, _pageSize); 

        if (sliceAt >= source.ExclusiveEnd)
            throw new ArgumentOutOfRangeException(nameof(sliceAt));

        // Remove old region from indexer and alloc_record
        _indexer.Remove(new RegionSetHandle(source.Start, setId));
        alloc_record.RemoveRegion(region_idx);

        _regions[li] = new Region(source.Start, sliceAt, source.InternalStateSpace, source.InternalState)
        {
            ExternalState = source.ExternalState,
            ExternalStateSpace = source.ExternalStateSpace
        };

        _regions[ri] = new Region(source.Start + sliceAt, source.Size - sliceAt, source.InternalStateSpace, source.InternalState)
        {
            ExternalState = source.ExternalState,
            ExternalStateSpace = source.ExternalStateSpace
        };

        // RegisterAllocator children in indexer and alloc_record
        _indexer.Insert(new RegionSetHandle((nuint)_regions[li].Start, (nuint)setId));
        _indexer.Insert(new RegionSetHandle((nuint)_regions[ri].Start, (nuint)setId));
        alloc_record.Add(li);
        alloc_record.Add(ri);

        // Recycle parent descriptor
        FreeDescriptor(sourceIndex);

        return (li, ri);
    }

    public bool TryCoalesce(
        RegionSetHandle handle,
        ushort leftIndex,
        ushort rightIndex,
        out ushort mergedIndex)
    {
        mergedIndex = 0;
        uint setId = (uint)handle.Value;

        if (!_sets.TryGetValue(setId, out var set))
            return false;

        if (!set.Contains(leftIndex) || !set.Contains(rightIndex))
            return false;

        ref Region left = ref _regions[leftIndex];
        ref Region right = ref _regions[rightIndex];

        if (!Region.Coalesce(left, right).success)
            return false;

        // Remove both from indexer and alloc_record
        _indexer.Remove(new RegionSetHandle((nuint)left.Start, (nuint)setId));
        _indexer.Remove(new RegionSetHandle((nuint)right.Start, (nuint)setId));
        set.Remove(leftIndex);
        set.Remove(rightIndex);

        // Create merged region
        mergedIndex = AllocateDescriptor();
        _regions[mergedIndex] = Region.Coalesce(left, right).region;

        _indexer.Insert(new RegionSetHandle((nuint)_regions[mergedIndex].Start, (nuint)setId));
        set.Add(mergedIndex);

        FreeDescriptor(leftIndex);
        FreeDescriptor(rightIndex);
        return true;
    }

    public bool TryCompactRegionSet(RegionSetHandle handle,
        ReadOnlySpan<ushort> survivingRegionIndices,
        out RegionSetHandle newBacking)
    {
        throw new NotImplementedException();
    }
    #endregion

}

public enum PageGranularity : uint
{
    Standard4K = 4096,
    HugePage2M = 2_097_152,
    HugePage1G = 1_073_741_824
}

[StructLayout(LayoutKind.Sequential)]
struct SYSTEM_INFO
{
    public ushort wProcessorArchitecture;
    public ushort wReserved;
    public uint dwPageSize;
    public IntPtr lpMinimumApplicationAddress;
    public IntPtr lpMaximumApplicationAddress;
    public IntPtr dwActiveProcessorMask;
    public uint dwNumberOfProcessors;
    public uint dwProcessorType;
    public uint dwAllocationGranularity; // <-- This is what we need
    public ushort wProcessorLevel;
    public ushort wProcessorRevision;
}

