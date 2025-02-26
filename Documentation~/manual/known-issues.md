# Known Issues

Below you can find current known issues. If you found a bug, please report it by opening a **[GitHub issue]**.

### [#333]: AtomicSafetyHandle may throw InvalidOperationException

`Unity.Collections@2.2+` has an unresolved bug, as you can read [here](https://docs.unity3d.com/Packages/com.unity.collections@2.2/manual/issues.html):

> [!QUOTE]
> All containers allocated with `Allocator.Temp` on the same thread use a shared `AtomicSafetyHandle` instance rather than each having their own.

This can lead to safety check error throws like this:

```txt
InvalidOperationException: The Unity.Collections.NativeList`1[System.Int32]
has been declared as [WriteOnly] in the job, but you are reading from it.
```

when using [`UnsafeTriangulator`][unsafe-triangulator] with `Allocator.Temp`. Consider the following example:

```csharp
using var positions = new NativeList<double2>(Allocator.Temp);
positions.Add(...);

new UnsafeTriangulator().Triangulate(input: new(){ Positions = positions.AsArray() }, ...);
```

Unfortunately, due to `AsArray()` call, which should be safe, the above example will trigger an exception from the safety handle.
Currently the only way to resolve this issue is:

- Disable safety checks, or
- Call [`ToArray(Allocator)`][to-array]

I recommend the second option, calling [ToArray][to-array], unless you are certain about what you are doing.

```csharp
using var positions = new NativeList<double2>(Allocator.Temp);
positions.Add(...);

using var p = postions.ToArray(Allocator.Temp);
new UnsafeTriangulator().Triangulate(input: new(){ Positions = p }, ...);
```

[GitHub issue]: https://github.com/andywiecko/BurstTriangulator/issues/new?template=Blank+issue
[#333]: https://github.com/andywiecko/BurstTriangulator/issues/333
[unsafe-triangulator]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.UnsafeTriangulator
[to-array]: https://docs.unity3d.com/Packages/com.unity.collections@2.5/api/Unity.Collections.NativeList-1.html#Unity_Collections_NativeList_1_ToArray_Unity_Collections_AllocatorManager_AllocatorHandle_

---

<details>
    <summary>
        <strong>📂 Archived Issues (Click to expand)</strong>
    </summary>

<h2>Archive</h2>

<h3>
    <a href="https://github.com/andywiecko/BurstTriangulator/issues/103">#103</a>:
    Leak Detected Warning in the Console
</h3>

In the Unity Editor, you may encounter the following log message:

```bash
Leak Detected : Persistent allocates 257 individual allocations. To find out more please enable 'Jobs/LeakDetection/Full StackTraces' and reproduce the leak again.
```

Not to worry, this issue is likely related to an internal bug in the `Unity.Collections` or `Unity.Burst` package (related to `NativeQueue<>` allocation).

*Resolved: NativeQueue&lt;T&gt; is no longer used in the package. The log should no longer be visible since v3.6.*

<h3>
    <a href="https://github.com/andywiecko/BurstTriangulator/issues/105">#105</a>,
    <a href="https://github.com/andywiecko/BurstTriangulator/issues/106">#106</a>:
    Incorrect triangulations for complicated input
</h3>

Due to floating-point precision, triangulation may fail for some input. This is often related to single-point precision. Changing coordinates from `float2` to `double2` solves the issue. This will be addressed in the upcoming release. If you want to try it now, there is an experimental branch available [**here**](https://github.com/andywiecko/BurstTriangulator/tree/experimental/double2-coords).

*Resolved: Use `double2` precision (e.g., `Triangulator<double2>`). Fixed since v3.*

</details>
