namespace Mnemo;

// Memory sources are registered with Mnemo, which routes Free calls *and other operations to the appropriate memory source. 

public struct MemorySourceRegistration(
    UIntPtr start, 
    nuint size, 
    Func<UIntPtr, nint> free_callback)
{
    public UIntPtr Start = start;
    public nuint Size = size;
    public Func<UIntPtr, nint> FreeCallback = free_callback;

    public bool Contains(UIntPtr ptr) 
        => (nuint)ptr >= (nuint)Start && (nuint)ptr < (nuint)Start + Size;
}