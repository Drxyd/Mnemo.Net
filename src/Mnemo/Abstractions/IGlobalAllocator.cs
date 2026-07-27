namespace Mnemo;

public interface IGlobalAllocator<Self>
    where Self : IGlobalAllocator<Self>
{
    static abstract Self Instance { get; }
    static abstract IntPtr Allocate(nuint size);
    static abstract void Free(IntPtr ptr);
    static abstract IntPtr Reallocate(IntPtr ptr, nuint new_size);
}