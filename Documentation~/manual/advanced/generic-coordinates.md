# Generic coordinates

The package supports generic coordinates.
Currently, support is provided for [`float2`][float2], [`double2`][double2], [`Vector2`][Vector2], [`fp2`][fp2] and [`int2`][int2].

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

| feature         | [`float2`][float2] | [`Vector2`][Vector2][^note1] | [`double2`][double2] | [`fp2`][fp2][^note2] | [`int2`][int2][^note3] |
| :-------------: | :----------------: | :--------------------------: | :------------------: | :-------------------:| :--------------------: |
| delaunay        | ✔️                 | ✔️                          | ✔️                   | ✔️                  | ✔️                     |
| constraints     | ✔️                 | ✔️                          | ✔️                   | ✔️                  | ✔️                     |
| holes           | ✔️                 | ✔️                          | ✔️                   | ✔️                  | 🟡[^note4]             |
| refinement      | ✔️                 | ✔️                          | ✔️                   | ✔️                  | ❌                     |
| preprocessors   | ✔️                 | ✔️                          | ✔️                   | ✔️                  | 🟡[^note5]             |
| dynamic[^note6] | ✔️                 | ✔️                          | ✔️                   | ✔️                  | ❌                     |
| $\alpha$-shape  | ✔️                 | ✔️                          | ✔️                   | ✔️                  | ✔️                     |

[^note1]: Via [float2] reinterpret.
[^note2]: This feature is available through an optional dependency. Users must install [`com.danielmansson.mathematics.fixedpoint`][fp2]. See how to install it [**here**](xref:getting-started-md#optional-dependencies).
[^note3]: Support up to $\sim 2^{20}$.
[^note4]: In the current implementation, holes are fully supported with [`Settings.AutoHolesAndBoundary`][auto]. However, manual holes with [`int2`][int2] coordinates may not guarantee that the given hole can be created. An additional extension is planned in the future to support holes with manual floating-point precision for [`int2`][int2].
[^note5]: Support for [`Preprocessor.COM`][com] with translation only is available.
[^note6]: Available only through [`UnsafeTriangulator<T>`][unsafe-triangulator] API.

[auto]: xref:andywiecko.BurstTriangulator.TriangulationSettings.AutoHolesAndBoundary
[com]: xref:andywiecko.BurstTriangulator.Preprocessor.COM
[triangulator]: xref:andywiecko.BurstTriangulator.Triangulator
[triangulatorT2]: xref:andywiecko.BurstTriangulator.Triangulator`1
[unsafe-triangulator]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.UnsafeTriangulator`1
[extensions]: xref:andywiecko.BurstTriangulator.Extensions
[float2]: xref:Unity.Mathematics.float2
[Vector2]: xref:UnityEngine.Vector2
[double2]: xref:Unity.Mathematics.double2
[fp2]: https://github.com/danielmansson/Unity.Mathematics.FixedPoint
[int2]: xref:Unity.Mathematics.int2
[benchmark]: xref:benchmark-md#generic-coordinates
