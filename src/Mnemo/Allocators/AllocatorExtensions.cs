using System.Runtime.CompilerServices;

namespace Mnemo;

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

#if DEBUG
#endif