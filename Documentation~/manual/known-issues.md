# Known Issues

Currently, there are no known issues. If you found a bug, please report it by opening a **[GitHub issue]**.

[GitHub issue]: https://github.com/andywiecko/BurstTriangulator/issues/new?template=Blank+issue

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
