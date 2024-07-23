# Unsafe Triangulator

Package provides also low level API through [`UnsafeTriangulator<T>`][unsafe-triangulator].
To use this, similar as in `Unity.Collections`, one has to load low level namespace

```csharp
using andywiecko.BurstTriangulator.LowLevel.Unsafe;
```

[`UnsafeTriangulator<T>`][unsafe-triangulator] is an empty readonly struct which provide API which can be used directly in native context in the jobs pipeline.
Additionally this struct allows much more customization with respect to managed [`Triangulator<T>`][triangulator],
however, the user is responsible to data allocation, including output data.
Below one can find minimal working example using [`UnsafeTriangulator<T>`][unsafe-triangulator]

```csharp
using var positions = new NativeArray<float2>(..., Allocator.Temp);
using var triangles = new NativeArray<int>(64, Allocator.Temp);
new UnsafeTriangulator<float2>().Triangulate(
    input: new() { Positions = positions },
    output: new() { Triangles = triangles },
    args: Args.Default(),
    allocator: Allocator.Persistent
);
```

this corresponds to the following managed approach with [`Triangulator<T>`][triangulator]

```csharp
using var triangulator = new Triangulator(Allocator.Persistent)
{
    Input = { Positions = new float2[]{ ... }.AsNativeArray() }
};
triangulator.Run();
var triangles = triangulator.Output.Triangles;
```

## Parameters

All extension methods related to [`UnsafeTriangulator<T>`][unsafe-triangulator] API except standard additional parameters,
include custom struct parameters:

- [`LowLevel.Unsafe.InputData<T>`][n-input-data],
- [`LowLevel.Unsafe.OutputData<T>`][n-output-data],
- [`LowLevel.Unsafe.Args`][n-args].

The first two structs are the same as managed types [`InputData<T>`][m-input-data] and [`OutputData<T>`][m-output-data], respectively. They have the same fields/properties.
Similar situation is with [`LowLevel.Unsafe.Args`][n-args] and managed corresponding [`TriangulationSettings`][m-settings], however the first, one is readonly struct and cannot be easily modified.

Due to current C# lang version restriction used in Unity structs cannot have default values for field and properties. To create [`Args`][n-args] manually use [`Args.Default()`][args-default] method and pass parameter which you want to change from default value, e.g.

```csharp
var args = Args.Default(refineMesh: true);
```

To "modify" the [LowLevel.Unsafe.Args][n-args], one can use the [With][args-with] method. This method returns new args with the selected parameters replaced. For example:

```csharp
var args = Args.Default();
args = args.With(refineMesh: true);
```

[`LowLevel.Unsafe.Args`][n-args] can be implicitly converted from [`TriangulationSettings`][m-settings]. So one can store [`TriangulationSettings`][m-settings] as a serialized field in a [`MonoBehaviour`][monobehaviour] and cast to args if necessary

```csharp
var settings = new TriangulationSettings();
Args args = settings;
```

[triangulator]: xref:andywiecko.BurstTriangulator.Triangulator`1
[unsafe-triangulator]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.UnsafeTriangulator`1
[n-input-data]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.InputData`1
[n-output-data]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.InputData`1
[n-args]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.InputData`1
[m-input-data]: xref:andywiecko.BurstTriangulator.InputData`1
[m-output-data]: xref:andywiecko.BurstTriangulator.InputData`1
[m-settings]: xref:andywiecko.BurstTriangulator.TriangulationSettings
[monobehaviour]: xref:UnityEngine.MonoBehaviour
[args-default]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.Args.Default*
[args-with]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.Args.With*
