namespace Mnemo.Topology;

public struct SynchronizationTable(nuint table, ushort stateCount)
{
    // Use 2D indexing so we can ask if intervals x and y are synced
    public nuint Table = table;
    public ushort StateCount = stateCount;

    public bool this[int x, int y]
    {
        get
        {
            if (x < 0 || x >= StateCount || y < 0 || y >= StateCount)
                throw new IndexOutOfRangeException();
            return (Table & (1UL << (x * StateCount + y))) != 0;
        }
        set
        {
            if (x < 0 || x >= StateCount || y < 0 || y >= StateCount)
                throw new IndexOutOfRangeException();
            if (value)
                Table |= (nuint)(1UL << (x * StateCount + y));
            else 
                Table &= (nuint)~(1UL << (x * StateCount + y));
        }
    }

    public static SynchronizationTable Zero
        => new SynchronizationTable(0, 8);
}