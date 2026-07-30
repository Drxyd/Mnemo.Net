using System.Numerics;
using System.Runtime.CompilerServices;

using Mnemo.Abstractions;

namespace Mnemo.Topology;

// TODO: Consider thread safety... Interlocked.Exchange?

/// <summary>
/// RegionSet indexer using Eytzinger layout for fast predecessor search. 
/// Meant for memory sources managing a single flat interval of memory.
/// </summary>
internal class EytzingerArray : IRegionSetIndexer
{
    private const uint SmallTierThreshold = 32;

    private RegionSetHandle[] _sortedBuffer;
    private RegionSetHandle[] _eytzingerBuffer;
    private int _count;

    public int Count => _count;

    public EytzingerArray(int initialCapacity = 32)
    {
        _sortedBuffer = new RegionSetHandle[initialCapacity];
        _eytzingerBuffer = Array.Empty<RegionSetHandle>();
        _count = 0;
    }


    #region Public API
    /// <summary>
    /// Looks up a payload by key. 
    /// Returns true if found; otherwise false.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetPredecessor(nuint key, out nuint value)
    {
        if (_count == 0)
        {
            value = 0;
            return false;
        }
        if (_count <= SmallTierThreshold)
            return TrySearchSmallTier(key, out value);
        else
            return TrySearchEytzingerTier(key, out value);
    }

    /// <summary>
    /// Inserts or updates a key-payload pair. 
    /// Rare operation: performs O(N) shift and builds Eytzinger layout if N > 32.
    /// </summary>
    public void Insert(RegionSetHandle handle)
    {
        int index = BinarySearchSortedBuffer(handle.Key);

        if (index >= 0)
        {
            // Key exists: update payload in-place
            _sortedBuffer[index] = handle;
        }
        else
        {
            // Key does not exist: insert into sorted array
            int insertAt = ~index;
            EnsureCapacity(_count + 1);

            Array.Copy(_sortedBuffer, insertAt, _sortedBuffer, insertAt + 1, _count - insertAt);
            _sortedBuffer[insertAt] = handle;
            _count++;
        }
        if (_count > SmallTierThreshold)
            RebuildEytzinger();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="handle"></param>
    /// <exception cref="NotImplementedException"></exception>
    public void Remove(RegionSetHandle handle)
    {
        int index = BinarySearchSortedBuffer(handle.Key);
        if (index < 0)
        {
            return;
        }
        int elementsToShift = _count - index - 1;
        if (elementsToShift > 0)
        {
            Array.Copy(_sortedBuffer, index + 1, _sortedBuffer, index, elementsToShift);
        }
        _count--;
        _sortedBuffer[_count] = default; // Clear stale reference

        if (_count <= SmallTierThreshold)
        {
            _eytzingerBuffer = Array.Empty<RegionSetHandle>();
        }
        else
            RebuildEytzinger();
        // Tombstones: Mark deleted slots with Key = 0 (invalid for a real region start)
        // and skip them during search. Rebuild only when tombstone density exceeds
        // a threshold (e.g. 25%).
    }
    #endregion


    #region Search Implementations

    // Tier 1: Small sets (N <= 32).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TrySearchSmallTier(nuint key, out nuint value)
    {
        int predecessorIdx = -1;
        for (int i = 0; i < _count; i++)
        {
            if (_sortedBuffer[i].Key <= key)
                predecessorIdx = i;
            else break;
        }
        if (predecessorIdx >= 0)
        {
            value = _sortedBuffer[predecessorIdx].Value;
            return true;
        }
        value = default;

        return false;
    }

    // Tier 2: Large sets (32 < N <= 65536).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TrySearchEytzingerTier(nuint key, out nuint value)
    {
        int i = 1; // 1-based root index
        while (i <= _count)
        {
            // Conditional jump: left child = 2i, right child = 2i + 1
            i = (i << 1) | (key >= _eytzingerBuffer[i].Key ? 1 : 0);
        }
        i >>= BitOperations.TrailingZeroCount(~i);

        if (i > 0) // Return nearest inclusive predecessor, not exact matching node
        {
            value = _eytzingerBuffer[i].Value;
            return true;
        }
        value = default;

        return false;
    }
    #endregion


    #region Layout & Maintenance Logic

    // Rebuilds the Eytzinger tree buffer from the sorted array.
    private void RebuildEytzinger()
    {
        if (_eytzingerBuffer.Length <= _count)
        {
            _eytzingerBuffer = new RegionSetHandle[_count + 1];
        }
        int sortedIdx = 0;
        EytzingerInOrder(1, ref sortedIdx);
    }

    // In-order traversal
    private void EytzingerInOrder(int k, ref int sortedIdx)
    {
        if (k > _count) return;

        EytzingerInOrder(2 * k, ref sortedIdx);
        _eytzingerBuffer[k] = _sortedBuffer[sortedIdx++];
        EytzingerInOrder(2 * k + 1, ref sortedIdx);
    }

    private int BinarySearchSortedBuffer(nuint key)
    {
        int low = 0;
        int high = _count - 1;

        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            nuint midKey = _sortedBuffer[mid].Key;

            if (midKey == key) return mid;
            if (midKey < key) low = mid + 1;
            else high = mid - 1;
        }

        return ~low;
    }

    private void EnsureCapacity(int required)
    {
        if (required > _sortedBuffer.Length)
        {
            int newCapacity = Math.Max(_sortedBuffer.Length * 2, required);
            Array.Resize(ref _sortedBuffer, newCapacity);
        }
    }
    #endregion
}

// Only adopt optimizations once tests and benchmarks are available. Simplicity is best for now.

// Future micro-optimization: SIMD vectorized search for small sets (N <= 32) using System.Numerics.Vector<T> for cross-platform acceleration.

/*
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TrySearchSmallTierSIMD(nuint key, out nuint value)
    {
        ref var baseRef = ref MemoryMarshal.GetArrayDataReference(_sortedBuffer);
        int predecessorIdx = -1;

        if (Vector.IsHardwareAccelerated && _count >= Vector<nuint>.Count)
        {
            var searchVec = new Vector<nuint>(key);
            int simdSteps = _count - Vector<nuint>.Count;
            int i = 0;

            // Load 2/4/8 keys directly into SIMD registers depending on AVX2/NEON/Vector length
            while (i <= simdSteps)
            {
                ref var nodeRef = ref Unsafe.Add(ref baseRef, i);
                Vector<nuint> keysVec = ReadKeysVector(ref nodeRef);
                Vector<nuint> cmp = Vector.LessThanOrEqual(keysVec, searchVec);

                if (cmp != Vector<nuint>.Zero)
                {
                    // Update index based on scalar match within vector
                    for (int lane = 0; lane < Vector<nuint>.Count; lane++)
                    {
                        if (cmp[lane] != 0) { predecessorIdx = i + lane; }
                    }
                }
                i += Vector<nuint>.Count;
            }
            
            for (; i < _count; i++) // Clean up remaining tail elements
            {
                if (Unsafe.Add(ref baseRef, i).Key <= key)
                    predecessorIdx = i;
                else break;
            }
        }
        else
        {
            // Scalar fallback for non-SIMD or ultra-small bounds
            for (int i = 0; i < _count; i++)
            {
                if (Unsafe.Add(ref baseRef, i).Key <= key)
                    predecessorIdx = i;
                else break;
            }
        }
        if (predecessorIdx >= 0)
        {
            value = Unsafe.Add(ref baseRef, predecessorIdx).Value;
            return true;
        }

        value = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<nuint> ReadKeysVector(ref RegionSetHandle nodeRef)
    {
        // Extract contiguous keys from RegionSetHandle structs into SIMD vector
        Unsafe.SkipInit(out Vector<nuint> vec);
        ref nuint vecRef = ref Unsafe.As<Vector<nuint>, nuint>(ref vec);

        for (int i = 0; i < Vector<nuint>.Count; i++)
        {
            Unsafe.Add(ref vecRef, i) = Unsafe.Add(ref nodeRef, i).Key;
        }
        return vec;
    }

 */

// Future micro-optimization: Branchless binary search on Eytzinger tree with zero bounds checks via Unsafe.Add for large sets (32 < N <= 65536).
/*
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TrySearchEytzingerTier(nuint key, out nuint value)
    {
        int i = 1; // 1-based root index

        ref var baseRef = ref MemoryMarshal.GetArrayDataReference(_eytzingerBuffer);

        while (i <= _count)
        {
            ref readonly var node = ref Unsafe.Add(ref baseRef, i);
            i = (i << 1) | (key > node.Key ? 1 : 0);
        }
        i >>= BitOperations.TrailingZeroCount(~i);

        if (i > 0)
        {
            value = Unsafe.Add(ref baseRef, i).Value;
            return true;
        }

        value = default;
        return false;
    }
 */