

// Orthogonal concerns:
// Allocator scope: Static (global), instance (local), persistant instance (storable)
// Allocator kind: List, Buddy, Slab, Bump, Arena, Mark and sweep GC, Ring
// Allocator concurrency: Threadsafe, Unsafe
// Memory source: C# GC, OS, Mmap files, Parent Allocator
// Resource error detection: Sliding Canaray, Substructural flow analysis, Type encoded allocation strategy, * guard‑page protection for buffer overruns
// Memory compaction

/*
 * 
┌─────────────────┐      ┌──────────────────┐
│   IAllocator    │      │ IGlobalAllocator │  (instance vs. static access)
│  + Allocate()   │      │  static Allocate │
│  + Free()       │      │  static Free     │
└────────┬────────┘      └────────┬─────────┘
         │                        │
   (marker interfaces)      (marker interfaces)
 ┌───────┴────────┐        ┌───────┴──────────────┐
 │ ISlabAllocator │        │ IGlobalSlabAllocator |
 │ IListAllocator │        │ IGlobalListAllocator |
 └────────────────┘        └──────────────────────┘

             ┌───────────────┐
             │ IMemorySource │  (backing memory acquisition)
             └───────────────┘
                    │
    ┌───────────────┼───────────────────┐
    │               │                   │
OSVirtualMemory  PinnedGCMemory   MemoryMappedFile
   (default)       (optional)      (optional)


            Owning Handles (end user)
 ┌─────────────────────────────────────────────┐
 │ NativeBox<T, TAllocator> : IDisposable      │
 │   - ref T Value                             │
 │   - void Dispose()                          │
 │                                             │
 │ NativeArray<T, TAllocator> : IDisposable    │
 │   - Span<T> Span                            │
 │   - void Dispose()                          │
 └─────────────────────────────────────────────┘

               Diagnostics
 ┌─────────────────────────────────────────────┐
 │ IAllocMetrics (optional on allocators)      │
 │  - TotalAllocatedBytes, ActiveAllocations,  │
 │    PeakAllocatedBytes                       │
 │                                             │
 │ DebugAllocator<T>  (conditional wrapper)    │
 │  - double‑free detection, leak tracking     │
 │  - implements IAllocMetrics                 │
 └─────────────────────────────────────────────┘
*
 */

 // Mental models and formalism:
 // Regions are abstract objects captured {s, e, T, C} where s = start, e = end, 
 // and T the state e.g. reserved, locked, committed, pinned etc. C is the set of capabilities
 // from which T takes values.
 // Regions are converted into allocators that exist intrusively within them and manage 
 // allocations. It's better to think of regions as existing in the virtual address space as
 // a region doesn't need to be repressenting allocated memory inorder to exist.
 // This abstraction allows for allocators to be more powerful in that they can leverage the 
 // capabilities of the memory source that provided the region. For example, a memory source 
 // that supports memory mapping can provide a region that is backed by a file and the allocator 
 // can then leverage this to provide persistence. Or a memory source that interacts with the OS
 // can allow allocators to free memory back to the OS and allow for memory compaction.


// Some TODOs:
// Exceptions as values with analyzer backing to enforce compliance or just throw?
// Add detailed error i.e. numeric overflow, zero division etc.
// Not all allocators support reallocation/resizing, since this is static information
// flag static error via Roslyn.
// Make diagnostics thread safe i.e. HashSet<IntPtr> and counters need to be locked.
// Q: In a single threaded context are concurrent constructs meaningfully slower? For extreme use cases (HFT), yes.
// NativeArray needs to support indexing so that Span isn't needed aside for interop
// Throw ObjectDisposedException on detected double frees.
// What if allocator is disposed before children resulting in dangling pointers?
// What if we had a wrapper that made allocators thread safe?

// Some Roslyn analyzer notes:
// If a handle like NativeBox<T, A> is cast into a ref T then either Free must be called on the reference
// or it must be cast back into NativeBox<T, A>.
// If Free is called on a NativeBox<T, A> then it must also be deleted from its source (field/DS).
// If a NativeBox<T, A> taken from an array or other DS is freed, a Roslyn analyzer might not be able to
// enforce that it be deleted from said array.
// If a NativeBox<T, A> is cast into a ref T then the Roslyn analyzer must ensure that it is cast back into a 
// NativeBox<T, A> by memorizing the allocator type and raising an appropriate error (This is still compatible with var).