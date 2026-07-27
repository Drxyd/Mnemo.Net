using System.Runtime.CompilerServices;

namespace Mnemo;

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