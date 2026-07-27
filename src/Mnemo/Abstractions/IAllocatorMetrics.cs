namespace Mnemo;

// Allocator classification matrix
/*          Thread safe | Not thread safe
 * -----------------------------------------
 * Local  |             |                   |
 * -----------------------------------------
 * Global |             |                   |
 * -----------------------------------------
 */



// Global allocators are implemented as singletons


public interface IAllocatorMetrics
{
    nuint TotalAllocatedBytes { get; }   // cumulative bytes allocated since creation
    nuint ActiveAllocations { get; }     // currently allocated blocks
    nuint PeakAllocatedBytes { get; }    // peak total allocated (high‑water mark)
    // Track fragmentation, timing, contention, size distribution etc.
}