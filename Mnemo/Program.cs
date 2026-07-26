using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;

namespace Mnemo;

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

internal class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
        Span<string> s;
    }
}

public static unsafe class Mnemo // Allocator registry and library name
{
    private static readonly List<RegisteredRegion> _regions = new();
    private static readonly object _lock = new();

    internal static void Register<A>(A allocator, IntPtr memory, nuint size)
        where A : IAllocator
    {
        lock (_lock)
        {
            _regions.Add(new RegisteredRegion(memory, size, allocator.Free, typeof(A).TypeHandle)); 
            
            // Keep sorted for binary search
            _regions.Sort((a, b) => a.Start.CompareTo(b.Start));
        }
    }

    internal static void Unregister(IntPtr start)
    {
        lock (_lock)
        {
            _regions.RemoveAll(r => r.Start == (nuint) start);
        }
    }

    public static void Free(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return;
        lock (_lock)
        {
            GetRegion(ptr).FreeCallback(ptr);
        }
    }

    internal static RegisteredRegion GetRegion(IntPtr ptr)
    {
        // Binary search for the region containing ptr
        // Would a custom binary search algorithm and a stored tree be more performant?
        int idx = _regions.BinarySearch(new RegisteredRegion(ptr, 0, null!, default), 
            Comparer<RegisteredRegion>.Create((a, b) => a.Start.CompareTo(b.Start)));

        if (idx < 0) idx = ~idx - 1; // Isn't this an error state?
        if (idx >= 0 && idx < _regions.Count && _regions[idx].Contains((nuint)ptr))
        {
            return _regions[idx];
        }
        else throw new IndexOutOfRangeException($"Pointer 0x{ptr:X} does not belong to any registered allocator.");
    }

#if DEBUG
    public static bool ValidateCast<A>(IntPtr ptr) // use ref instead?
        where A : IAllocator
    {
        return GetRegion(ptr).TypeHandle.Value == typeof(A).TypeHandle.Value;
    }
#endif

    internal static IntPtr RefToNint<T>(ref T reference)
    {
        return (IntPtr) Unsafe.AsPointer<T>(ref reference);
    }
}

public struct RegisteredRegion // Think of a better name
{
    public nuint Start;
    public nuint End;
    public Func<IntPtr, nint> FreeCallback;
    public RuntimeTypeHandle TypeHandle;
    public bool Contains(nuint ptr) => ptr >= Start && ptr < End;

    public RegisteredRegion(
        IntPtr start, 
        nuint size, 
        Func<IntPtr, nint> free_callback, 
        RuntimeTypeHandle type_handle)
    {
        Start = (nuint) start; 
        End = (nuint) start + size; 
        FreeCallback = free_callback;
        TypeHandle = type_handle;
    }
}

/// <summary>
/// Allocators that are capable of creating sub-allocators and that support a larger 
/// suite memory.h API's such that they can acquire memory and address space from the 
/// OS and offer that memory back
/// </summary>
public interface IMemorySource // Always static, local allocators can't create sub-allocators
{
    static abstract IntPtr Allocate(nuint size);

    static abstract IntPtr Free(IntPtr ptr);

    static abstract void Commit(IntPtr addr, nuint size);

    static abstract void Decommit(IntPtr addr, nuint size);
}

//  Memory sources: GC, Pinned GC, Operating system, derivative allocators

public interface IAllocator 
{
    static abstract IAllocator Create<M>(params object[] parameters)
        where M : IMemorySource; // *

    IntPtr Allocate(nuint size, nuint alignment = 16);
    IntPtr Free(IntPtr ptr);
    IntPtr Reallocate(IntPtr ptr, nuint oldSize, nuint new_size, nuint alignment = 16);

    /*
     IntPtr Reallocate(IntPtr ptr, nuint oldSize, nuint newSize, nuint alignment = 16)
    {
        // default fallback
        IntPtr newPtr = Allocate(newSize, alignment);
        if (ptr != IntPtr.Zero)
        {
            nuint copySize = oldSize < newSize ? oldSize : newSize;
            Unsafe.CopyBlock((void*)newPtr, (void*)ptr, copySize);
            Free(ptr);
        }
        return newPtr;
    }
     */
}

public interface IConcurrentAllocator : IAllocator { }
public interface IThreadLocalAllocator : IAllocator { }

public interface IGlobalAllocator<Self>
    where Self : IGlobalAllocator<Self>
{
    static abstract Self Instance { get; }
    static abstract IntPtr Allocate(nuint size);
    static abstract void Free(IntPtr ptr);
    static abstract IntPtr Reallocate(IntPtr ptr, nuint new_size);
}


// Allocator classification matrix
/*          Thread safe | Not thread safe
 * -----------------------------------------
 * Local  |             |                   |
 * -----------------------------------------
 * Global |             |                   |
 * -----------------------------------------
 */

interface ISlabAllocator<T> : IAllocator { }
interface IListAllocator : IAllocator { }
interface IBumpAllocator : IAllocator { }


// Global allocators are implemented as singletons


public interface IAllocatorMetrics
{
    nuint TotalAllocatedBytes { get; }   // cumulative bytes allocated since creation
    nuint ActiveAllocations { get; }     // currently allocated blocks
    nuint PeakAllocatedBytes { get; }    // peak total allocated (high‑water mark)
    // Track fragmentation, timing, contention, size distribution etc.
}

public enum AllocationResult // Reasons for failed allocation
{
    Success, OutOfMemory, InsufficientSpace
}

public struct Ref<T>
{
    public IntPtr ptr;

    public unsafe ref T ManagedReference => ref Unsafe.AsRef<T>((void*)ptr);

    public static implicit operator T(Ref<T> reference)
    {
        // Return a copy of the object
        throw new NotImplementedException();
    }
}

public readonly ref struct NativeBox<T, A>
    where T : unmanaged
    where A : IAllocator
{
    private readonly IntPtr _ptr;   // no allocator field

    internal NativeBox(IntPtr ptr) 
        => _ptr = ptr;

    internal NativeBox(ref T reference)
    {
        this = NativeBox<T, A>.AsBox(ref reference);
    }

    public bool IsAllocated 
        => _ptr != IntPtr.Zero;

    public unsafe ref T Asref
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref Unsafe.AsRef<T>((void*)_ptr);
    }

    public static unsafe NativeBox<T, A> AsBox(ref T reference)
    {
        IntPtr ptr = Mnemo.RefToNint(ref reference);

#if DEBUG
        // Ask Mnemo: Does the allocator containing this reference have the type A?
        bool answer = Mnemo.ValidateCast<A>(ptr);
        if (answer)
        {
            return new NativeBox<T, A>(ptr);
        }
        else throw new InvalidCastException();
#else 
        return new NativeBox<T, A>(ptr);
#endif
    }

    // we don't call NativeBox<T>.Free but rather pass the box into Mnemo.Free or IAllocator.Free
    // This usage pattern supports non-copying semantics
}

public readonly ref struct NativeArray<T, A>
    where T : unmanaged
    where A : IAllocator
{
    private readonly IntPtr _ptr;
    private readonly int _length;

    public T this[int idx]
    {
        get => this.AsSpan()[idx];
        set => this.AsSpan()[idx] = value;
    }

    internal NativeArray(IntPtr ptr, int length) 
    { 
        _ptr = ptr; 
        _length = length; 
    }

    public bool IsAllocated 
        => _ptr != IntPtr.Zero;

    public int Length 
        => _length;

    // We also need to cast back
    public static implicit operator Span<T>(NativeArray<T, A> arr) 
        => arr.AsSpan();

    public static explicit operator NativeArray<T, A>(Span<T> span)
    {
        // Ask Mnemo: Does the allocator containing this reference have the type A?
        
        ref T reference = ref MemoryMarshal.GetReference(span);
        IntPtr ptr = Mnemo.RefToNint(ref reference);
#if DEBUG
        bool answer = Mnemo.ValidateCast<A>(ptr);
        if (answer)
        {
            return new NativeArray<T, A>( ptr, span.Length );
        }
        else throw new InvalidCastException();
#else
        return new NativeArray<T, A>( ptr, span.Length );
#endif
    }

    private unsafe Span<T> AsSpan() => new Span<T>((void*)_ptr, _length);
}

public static class AllocatorExtensions
{
    public static NativeBox<T, A> Allocate <T, A> (this A allocator, nuint alignment = 16)
        where T : unmanaged
        where A : IAllocator
    {
        // Set alignment, decide between calculated alignment, default and user value
        // (raise warning in debug mode if user value is problematic)
        nuint a = AlignOf<T>();
        IntPtr ptr = allocator.Allocate((nuint)Unsafe.SizeOf<T>(), alignment);
        // Optionally zero memory, guard canary, etc.

        return new NativeBox<T, A>(ptr);
    }

    public static NativeBox<T, A> AllocateInitialized <T, A, S>(
        this A allocator,
        S state,
        Func<S, T> factory,
        nuint alignment = 16)
        where T : unmanaged
        where A: IAllocator
    {
        NativeBox<T, A> box = allocator.Allocate<T, A>(alignment: alignment);
        if (box.IsAllocated) // Instead of IsAllocated we need to check for error states
        {
            box.Asref = factory(state); // If factory throws we need to free memory and return diagnostic
            return box;
        }
        else throw new Exception(); // Need to retrieve reasoning for error from allocator
        // Instead of a sole IsAllocated flag, have an enum of possible error states
    }

    public static unsafe NativeBox<T, A> AllocateInitialized<T, A, S>(
        this A allocator,
        S state,
        delegate*<S, T> factory,
        nuint alignment = 16)
        where T : unmanaged
        where A : IAllocator
    {
        NativeBox<T, A> box = allocator.Allocate<T, A>(alignment: alignment);
        if (box.IsAllocated) // Instead of IsAllocated we need to check for error states
        {
            box.Asref = factory(state);
            return box;
        }
        else throw new Exception(); // Need to retrieve reasoning for error from allocator
        // Instead of a sole IsAllocated flag, have an enum of possible error states
    }


    public static NativeArray<T, A> AllocateSpan<T, A>(
        this A allocator, 
        int count,
        nuint alignment = 16)
        where T : unmanaged
        where A: IAllocator
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        checked
        {
            // Function for object alignment? Default to safe values (platform default)
            nuint size = (nuint)count * (nuint)Unsafe.SizeOf<T>();
            IntPtr ptr = allocator.Allocate(size, alignment);

            return new NativeArray<T, A>(ptr, count);
        }
    }

    public static NativeArray<T, TAllocator> AllocateInitialized<T, TAllocator>(
        this TAllocator allocator,
        int count,
        Func<int, T> factory,
        nuint alignment = 16)
        where T : unmanaged
        where TAllocator : IAllocator
    {
        NativeArray<T, TAllocator> span = allocator.AllocateSpan<T, TAllocator>(count, alignment);
        if (span.IsAllocated)
        {
            for (int i = 0; i < count; i++)
            {
                span[i] = factory(i); // What if factory throws? We need to free memory and return diagnostic
            }
            return span;
        }
        else throw new Exception();
    }

    public static NativeArray<T, TAllocator> AllocateFilled<T, TAllocator>(
        this TAllocator allocator,
        int count,
        T value,
        nuint alignment = 16)
        where T : unmanaged
        where TAllocator : IAllocator
    {
        Span<T> span = allocator.AllocateSpan<T, TAllocator>(count, alignment);
        if (true) // check span.IsAllocated
        {
            span.Fill(value);
            return (NativeArray<T, TAllocator>) span;// write explicit cast
        }
        else throw new Exception();
        
    }

    public static readonly nint Alignment = GetPlatformDefaultAlignment();

    private static nint GetPlatformDefaultAlignment()
    {
        // Check for ultra-wide SIMD register support first
        if (System.Runtime.Intrinsics.X86.Avx2.IsSupported) return 64;
        if (System.Runtime.Intrinsics.X86.Avx.IsSupported) return 32;

        // Fall back to standard CPU word rules
        return Environment.Is64BitProcess ? 16 : 8;
    }

    // Helper structure that forces memory alignment padding
    private struct AlignmentBuffer<T> where T : unmanaged
    {
        public byte Header;
        public T Target;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static nuint AlignOf<T>() where T : unmanaged
    {
        // Calculate the byte offset of 'Target' relative to 'Header' *
        return (nuint)Unsafe.ByteOffset(
            ref Unsafe.As<AlignmentBuffer<T>, byte>(ref Unsafe.NullRef<AlignmentBuffer<T>>()),
            ref Unsafe.As<T, byte>(ref Unsafe.NullRef<AlignmentBuffer<T>>().Target)
        );
    }
}

// For starters memory sources will be singletons, later on we'll promote
// them to static classes with attributes that enforce the memory source 
// contract
public sealed class OSVirtualMemorySource : IMemorySource, IDisposable
{
    private int _disposed;

    // Optional: a static singleton for convenience
    public static readonly OSVirtualMemorySource Instance = new();

    public OSVirtualMemorySource() { }

    public static IntPtr Allocate(nuint size)
    {
        // Round up to page size? VirtualAlloc already aligns to page boundaries,
        // but we can ensure we ask for at least one page.
        nuint allocationSize = Math.Max(size, (nuint)Environment.SystemPageSize);

        unsafe
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            {
                void* mem = PInvoke.VirtualAlloc(
                null,
                allocationSize,
                VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT | VIRTUAL_ALLOCATION_TYPE.MEM_RESERVE,
                PAGE_PROTECTION_FLAGS.PAGE_READWRITE);

                if (mem == null)
                {
                    throw new OutOfMemoryException($"VirtualAlloc failed for {size} bytes.");
                }
                return (IntPtr)mem;
            }
            else throw new Exception();
        }
    }

    public static nint Free(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return -1;

        unsafe
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
            {
                BOOL result = PInvoke.VirtualFree(
                (void*)ptr,
                0,                       // release entire region
                VIRTUAL_FREE_TYPE.MEM_RELEASE);
                if (!result)
                {
                    // Log or handle? In release, we might just ignore or throw.
                    // For a robust library, throwing is safer.
                    throw new InvalidOperationException("VirtualFree failed.");
                }
                return 0;
            }
            else throw new Exception();   
        }
    }

    public static void Commit(nint addr, nuint size)
    {
        throw new NotImplementedException();
    }

    public static void Decommit(nint addr, nuint size)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            // Nothing to clean up in this simple source,
            // but we set the flag to reject further operations.
        }
    }
}

public sealed class PinnedGCMemorySource : IMemorySource, IDisposable
{
    private readonly byte[] _buffer;
    private readonly GCHandle _handle;
    private bool disposedValue;

    nint IMemorySource.Allocate(nuint size)
    {
        throw new NotImplementedException();
    }

    void IMemorySource.Commit(nint addr, nuint size)
    {
        throw new NotImplementedException();
    }

    void IMemorySource.Decommit(nint addr, nuint size)
    {
        throw new NotImplementedException();
    }

    void IMemorySource.Free(nint ptr)
    {
        throw new NotImplementedException();
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~PinnedGCMemorySource()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

public class LocalSlab<T> : ISlabAllocator<T>, IDisposable
{
    private bool disposedValue;
    private readonly IMemorySource _source;

    public LocalSlab(int block_size, int block_count, IMemorySource source =  null)
    {
        _source = source ?? OSVirtualMemorySource.Instance;
        nuint total_size = (nuint) block_size * (nuint) block_count;
        IntPtr ptr = _source.Allocate(total_size);

        // Add initialization
    }

    nint IAllocator.Allocate(nuint size)
    {
        throw new NotImplementedException();
    }

    void IAllocator.Free(nint ptr)
    {
        throw new NotImplementedException();
    }

    nint IAllocator.Reallocate(nint ptr, nuint new_size)
    {
        throw new NotImplementedException();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~LocalSlab()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    void IDisposable.Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public static IAllocator Create<M>(params object[] parameters) where M : IMemorySource
    {
        throw new NotImplementedException();
    }

    public nint Allocate(nuint size, nuint alignment = 16U)
    {
        throw new NotImplementedException();
    }

    public nint Free(nint ptr)
    {
        throw new NotImplementedException();
    }
}

#if DEBUG
public sealed class DebugAllocator<TAllocator> : IAllocator, IAllocatorMetrics, IDisposable
    where TAllocator : IAllocator
{
    private readonly TAllocator _inner;
    private long _allocatedBytes;
    private long _activeAllocs;
    private long _peakBytes;
    private bool disposedValue;
    private readonly HashSet<IntPtr> _livePointers = new(); // double‑free check

    public nuint TotalAllocatedBytes => throw new NotImplementedException();

    public nuint ActiveAllocations => throw new NotImplementedException();

    public nuint PeakAllocatedBytes => throw new NotImplementedException();

    public static IAllocator Create<M>(params object[] parameters) where M : IMemorySource
    {
        throw new NotImplementedException();
    }

    public nint Allocate(nuint size, nuint alignment = 16U)
    {
        throw new NotImplementedException();
    }

    public nint Free(nint ptr)
    {
        throw new NotImplementedException();
    }

    public nint Reallocate(nint ptr, nuint new_size)
    {
        throw new NotImplementedException();
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            disposedValue = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~DebugAllocator()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    void IDisposable.Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    // … implement Allocate / Free with tracking …
    // on Dispose, check _livePointers.Count == 0 (leak detection)
}
#endif