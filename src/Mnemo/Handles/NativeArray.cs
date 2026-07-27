using System.Runtime.InteropServices;

namespace Mnemo;

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