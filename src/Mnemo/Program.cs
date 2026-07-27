using System.Runtime.Intrinsics.X86;

namespace Mnemo;


internal class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
    }
}

public enum AllocationResult // Reasons for failed allocation
{
    Success, OutOfMemory, InsufficientSpace
}
