using System.Runtime.InteropServices;

namespace Mnemo.Topology;

[StructLayout(LayoutKind.Sequential)]
public readonly struct RegionSetHandle(nuint key, nuint value)
{
    public readonly nuint Key = key;
    public readonly nuint Value = value;
}
