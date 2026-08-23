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


public class HexCube<T>
{
    private HexCube<T>[]? _children;
    private T? _value;

    // ReadOnlySpan prevents allocating new arrays when slicing
    public T this[ReadOnlySpan<byte> idx]
    {
        get
        {
            if (idx.Length == 0) return _value!;
            if (_children == null) throw new IndexOutOfRangeException();

            int childIndex = idx[0];
            // Ensure the byte is actually a hex value (0-15)
            if (childIndex >= 16) throw new IndexOutOfRangeException("Values must be 0-15");
            if (_children[childIndex] == null) throw new IndexOutOfRangeException();

            return _children[childIndex][idx[1..]];
        }
        set
        {
            if (idx.Length == 0)
            {
                _value = value;
                return;
            }

            _children ??= new HexCube<T>[16];

            int childIndex = idx[0];
            if (childIndex >= 16) throw new IndexOutOfRangeException("Values must be 0-15");

            _children[childIndex] ??= new HexCube<T>();
            _children[childIndex][idx[1..]] = value;
        }
    }
}