# Known Issues

## [#103]: Leak Detected Warning in the Console

In the Unity Editor, you may encounter the following log message:

```bash
Leak Detected : Persistent allocates 257 individual allocations. To find out more please enable 'Jobs/LeakDetection/Full StackTraces' and reproduce the leak again.
```

Not to worry, this issue is likely related to an internal bug in the `Unity.Collections` or `Unity.Burst` package (related to `NativeQueue<>` allocation).

[#103]: https://github.com/andywiecko/BurstTriangulator/issues/103
