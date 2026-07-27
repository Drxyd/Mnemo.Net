namespace Mnemo;

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