# Mnemo

**Manual memory management for .NET — type-safe, policy-driven, and opt-in.**

Mnemo is a research framework for explicit memory management in C#. It provides allocators, owning handles, and OS-level memory source abstractions that cooperate with the .NET GC rather than fighting it: metadata lives on the managed heap; payload lives where you put it.

&gt; **Status: Pre-alpha.** The architecture is stabilizing and core interfaces are being implemented. This repository documents intent and progress. It is not yet ready for use.

---

## Why

.NET's garbage collector is excellent for general-purpose workloads, but there are domains — high-frequency trading, game engines, native interop at scale, language runtimes — where deterministic deallocation, cache-friendly layout, and zero-allocation hot paths matter more than pause-time heuristics. Existing options force you into C++/Rust or into `unsafe` pointer soup with no safety net.

Mnemo attempts a third path: **manual allocation with static and dynamic guards**, staying inside C# and the CLR.

---

## Core Architecture

Memory management is layered by concern, not by convenience:

| Layer | Responsibility |
|-------|---------------|
| **Memory Source** | Acquires address space from the OS, the GC heap, or mapped files. Tracks commit state, protection flags, NUMA topology, and platform capabilities. |
| **Region** | The common currency between layers: a span of address space with both internal (allocator) and external (OS) state. |
| **Allocator** | Carves regions into objects. Slab, bump, arena, and list policies are local instances; globals are thin singleton façades. |
| **Handle** | `NativeBox&lt;T, A&gt;` and `NativeArray&lt;T, A&gt;` are typed, allocator-tagged owning references. The allocator type parameter prevents cross-allocator pollution at compile time. |
| **Registry** | A global spatial index that maps any pointer to its owning allocator for safe `Free` dispatch. |

This separation lets you reason about memory at the right granularity: the source knows about Huge Pages and TLBs; the allocator knows about object size classes; the handle knows about type safety.

### Key Design Decisions

- **The GC is an allocator, not a memory source.** Mnemo wraps `GC.AllocateUninitializedArray` as `GCAllocator`, giving pinned GC memory the same typed-handle interface as OS-backed memory. The GC manages metadata; you manage the payload.
- **Region dual-state tracking.** Every region carries both the allocator's view of its state (reserved, committed, offered) and the OS's actual state. The source acts as a transaction coordinator, keeping them in sync.
- **Fail-fast with rich diagnostics.** Allocation returns structured results, not just `null`. The source reports *why* it failed — fragmentation, alignment, memory pressure — so callers can degrade gracefully.
- **Advanced features are opt-in.** You can use Mnemo as a better `Marshal.AllocHGlobal` or as a kernel-bypass compaction engine. The surface area grows with your requirements.

---

## Capabilities in Development

### Type-Safe Handles
Handles carry the allocator as a phantom type. You cannot pass a slab-allocated pointer to a list allocator's free method — the compiler rejects it. A global registry provides C-style `free(void*)` ergonomics when you need them, while the typed API prevents the common cross-allocator corruption bug.

### Adaptive Radix Trie Indexing
The global registry and memory sources use an adaptive radix trie (ART) for sparse 64-bit address-space indexing. This replaces the naive sorted-array approach and scales to millions of regions without pointer-chasing overhead.

### Heterogeneous Aliased-Interval Allocator (HAIA)
An advanced allocator backed by fine-grained virtual-to-physical remapping. Objects declare a maximum growth bound at allocation time; if they expand within that bound, no `memcpy` occurs and no pointer changes. The OS page table absorbs the growth. This is strictly opt-in and requires platform support (Windows AWE or Linux `memfd` + `MAP_FIXED`).

### Roslyn Ownership Analyzers
Static analysis to catch dropped handles, double-frees, and `ref T` escapes across async boundaries. The analyzers are treated as high-quality lint, not as a proof system — runtime debug wrappers (use-after-free detection, canary poisoning) remain the safety of last resort.

### Platform Deep Dives
`OSVirtualMemorySource` is being engineered to expose capabilities that most managed frameworks ignore:
- Transparent Huge Pages and explicit demotion/promotion
- NUMA-aware allocation
- Memory pressure integration (Windows notifications, Linux cgroup v2)
- Guard-page insertion at the source level

---

## What Works Today

- Core abstractions (`IAllocator`, `IMemorySource`, `Region`, `RegionSet`)
- The `Region` dual-state model (internal vs. external state tracking)
- Eytzinger-array spatial indexer (fallback for small deployments)
- Basic Windows virtual memory interop

## What Is Next

- [ ] Adaptive Radix Trie indexer
- [ ] `OSVirtualMemorySource` completion (reserve, commit, decommit, release, split, coalesce)
- [ ] Slab and bump allocator implementations
- [ ] Debug allocator wrapper (double-free / leak detection)
- [ ] HAIA proof-of-concept on Windows AWE
- [ ] Roslyn analyzer skeleton
- [ ] Benchmark suite vs. `NativeMemory` and `Marshal.AllocHGlobal`

---

## What Is Out of Scope (For Now)

- **Replacing the .NET GC.** Mnemo lives alongside the GC, not underneath it. The standalone GC API (`System.GC.Name`) is a full 88-method runtime replacement contract that leaks every internal assumption of the .NET generational collector. It is a fascinating research direction, but it requires a dedicated team and a fork of the runtime. Not on the roadmap until the project is established.
- **Universal `new` interception.** We do not patch the runtime's allocation path. Source generators and analyzers provide compile-time opt-in. NativeAOT thunks may be explored later for AOT-only deployments.

## Design Philosophy

1. **The GC is not the enemy.** Use it for long-lived metadata, complex graphs, and configuration. Use manual memory for the payload.
2. **Types encode policy.** Allocator capability is visible in generic constraints, not runtime flags.
3. **Fail fast, diagnose richly.** The source reports *why* it failed so callers can degrade gracefully.
4. **Advanced features are opt-in.** The surface area grows with your requirements.

---

## Follow Along

Development is being documented in a series of deep-dive posts covering the architecture, the trade-offs, and the implementation details. Watch this repository for updates.

*This is a research project. APIs will change. Do not use in production.*

---

## License

MIT
