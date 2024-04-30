# Known Issues

## [#103]: Leak Detected Warning in the Console

In the Unity Editor, you may encounter the following log message:  

```bash
Leak Detected : Persistent allocates 257 individual allocations. To find out more please enable 'Jobs/LeakDetection/Full StackTraces' and reproduce the leak again.
```  

Not to worry, this issue is likely related to an internal bug in the `Unity.Collections` or `Unity.Burst` package (related to `NativeQueue<>` allocation).

## [#105], [#106]: Incorrect triangulations for complicated input
  
Due to floating-point precision, triangulation may fail for some input. This is often related to single-point precision. Changing coordinates from `float2` to `double2` solves the issue. This will be addressed in the upcoming release. If you want to try it now, there is an experimental branch available [**here**](https://github.com/andywiecko/BurstTriangulator/tree/experimental/double2-coords).

[#103]: https://github.com/andywiecko/BurstTriangulator/issues/103
[#105]: https://github.com/andywiecko/BurstTriangulator/issues/105
[#106]: https://github.com/andywiecko/BurstTriangulator/issues/106
