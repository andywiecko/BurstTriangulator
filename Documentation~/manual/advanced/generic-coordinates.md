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

| type                 | implemented? | delaunay | constraints | refinement | preprocessors |
| :------------------: | :----------: | :------: | :---------: | :--------: | :-----------: |
| [`float2`][float2]   | ✔️          | ✔️       | ✔️         | ✔️         |✔️            |
| [`double2`][double2] | ✔️          | ✔️       | ✔️         | ✔️         |✔️            |
| [`int2`][int2]       | ❌          |          |            |             |               |

[triangulator]: xref:andywiecko.BurstTriangulator.Triangulator
[triangulatorT2]: xref:andywiecko.BurstTriangulator.Triangulator`1
[extensions]: xref:andywiecko.BurstTriangulator.Extensions
[float2]: xref:Unity.Mathematics.float2
[double2]: xref:Unity.Mathematics.double2
[int2]: xref:Unity.Mathematics.int2
