namespace Mnemo;

//  Memory sources: GC, Pinned GC, Operating system, derivative allocators

public interface IAllocator 
{
    static abstract IAllocator Create<M>(params object[] parameters)
        where M : IMemorySource<M>; // *

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