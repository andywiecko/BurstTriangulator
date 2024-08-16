# Generic coordinates

The package supports generic coordinates.
Currently, support is provided for [`float2`][float2] and [`double2`][double2] types.
Support for [`int2`][int2] is planned for the future.

By default, the [`Triangulator`][triangulator] class is based on the [`double2`][double2] type. To manually select the desired coordinate type, use the generic version, namely, [`Triangulator<T2>`][triangulatorT2].

```csharp
using var positions = new NativeArray<float2>(..., Allocator.Persistent);
using var triangulator = new Triangulator<float2>(Allocator.Persistent)
{
  Input = { Positions = positions },
};

triangulator.Run();
```

[`Triangulator<T2>`][triangulatorT2] has the same API (through [Extensions][extensions]), as [`Triangulator`][triangulator].
The only difference is that the input/output types are the same as `T2`.

See benchmark for the generic coordinates [**here**][benchmark].

<br>

| type                 | delaunay | constraints | holes      | refinement | preprocessors     | notes                       |
| :------------------: | :------: | :---------: | :--------: | :--------: | :---------------: | :-------------------------: |
| [`float2`][float2]   | ✔️       | ✔️         | ✔️         | ✔️         |✔️                |                             |
| [`double2`][double2] | ✔️       | ✔️         | ✔️         | ✔️         |✔️                |                             |
| [`int2`][int2]       | ✔️       | ✔️         | 🟡[^holes] | ❌         |🟡[^preprocessors] | Support up to $\sim 2^{20}$ |

[^holes]: In the current implementation, holes are fully supported with [`Settings.AutoHolesAndBoundary`][auto]. However, manual holes with [`int2`][int2] coordinates may not guarantee that the given hole can be created. An additional extension is planned in the future to support holes with manual floating-point precision for [`int2`][int2].
[^preprocessors]: Support for [`Preprocessor.COM`][com] with translation only is available.

[auto]: xref:andywiecko.BurstTriangulator.TriangulationSettings.AutoHolesAndBoundary
[com]: xref:andywiecko.BurstTriangulator.Preprocessor.COM
[triangulator]: xref:andywiecko.BurstTriangulator.Triangulator
[triangulatorT2]: xref:andywiecko.BurstTriangulator.Triangulator`1
[extensions]: xref:andywiecko.BurstTriangulator.Extensions
[float2]: xref:Unity.Mathematics.float2
[double2]: xref:Unity.Mathematics.double2
[int2]: xref:Unity.Mathematics.int2
[benchmark]: xref:benchmark-md#generic-coordinates
