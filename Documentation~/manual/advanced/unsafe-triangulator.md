# Unsafe Triangulator

> [!CAUTION]  
> Use the unsafe context with caution! Ensure that you fully understand what you are doing. It is recommended to first familiarize yourself with the managed [`Triangulator`][triangulator]. Using the unsafe triangulator can lead to unexpected behavior if not used correctly.
> *Unsafe* in this context indicates that this API may be challenging for beginner users.
> The user is responsible for managing data allocation (both input and output).
> Note that some permutations of method calls may not be supported.
> The term *unsafe* does **not** refer to memory safety.

The package also provides a low-level API through [`UnsafeTriangulator<T2>`][unsafe-triangulator].
This can be used for customization of the triangulation and for use in a native context.
To use this, similar to `Unity.Collections`, you need to load the low-level namespace:

```csharp
using andywiecko.BurstTriangulator.LowLevel.Unsafe;
```

[`UnsafeTriangulator<T2>`][unsafe-triangulator] is an empty readonly struct which provide API which can be used directly in native context in the jobs pipeline.
Additionally this struct allows much more customization with respect to managed [`Triangulator<T2>`][triangulator],
however, the user is responsible to data allocation, including output data.
Below one can find minimal working example using [`UnsafeTriangulator<T2>`][unsafe-triangulator]

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

this corresponds to the following managed approach with [`Triangulator<T2>`][triangulator]

```csharp
using var triangulator = new Triangulator(Allocator.Persistent)
{
    Input = { Positions = new float2[]{ ... }.AsNativeArray() }
};
triangulator.Run();
var triangles = triangulator.Output.Triangles;
```

Learn more in the [Parameters](#parameters) section about how to set up the triangulation and the [Extensions](#extensions) section to learn about other customization steps for triangulation.

## Parameters

All extension methods related to [`UnsafeTriangulator<T2>`][unsafe-triangulator] API except standard additional parameters,
include custom struct parameters:

- [`LowLevel.Unsafe.InputData<T2>`][n-input-data],
- [`LowLevel.Unsafe.OutputData<T2>`][n-output-data],
- [`LowLevel.Unsafe.Args`][n-args].

The first two structs are the same as managed types [`InputData<T2>`][m-input-data] and [`OutputData<T2>`][m-output-data], respectively. They have the same fields/properties.
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

## Extensions

Below, you can find extensions (with descriptions and examples) that can be used with [`UnsafeTriangulator`][unsafe-triangulator]. Check out the unit tests for additional use cases.

### `Triangulate`

The extension [`Triangulate(input, output, args, allocator)`][triangulate] is the simplest option to use. The action of this extension essentially produces the same result as the [`Run`][run] method for the managed [`Triangulator`][triangulator]. It can be useful when triangulation is done with [`Allocator.Temp`][allocator-temp] in a single job or to combine this with different extensions.

```csharp
using var positions = new NativeArray<float2>(..., Allocator.Persistent);
using var constraints = new NativeArray<int>(..., Allocator.Persistent);
using var holesSeeds = new NativeArray<float2>(..., Allocator.Persistent);
using var triangles = new NativeList<int>(64, Allocator.Persistent);
new UnsafeTriangulator<float2>().Triangulate(
    input: new() { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holesSeeds },
    output: new() { Triangles = triangles },
    args: Args.Default(refineMesh: true, restoreBoundary: true),
    allocator: Allocator.Persistent
);
```

### `PlantHoleSeeds`

The extension [`PlantHoleSeeds(input, output, args, allocator)`][plant-seeds] is particularly useful when the user requires mesh data *without* removed triangles and additional mesh copy *with* removed triangles. In this case, the triangulation is performed once, which is generally a more expensive operation. Below is an example usage with the `autoHolesAndBoundary` option selected:

```csharp
var input = new LowLevel.Unsafe.InputData<double2>
{
    Positions = ...,
    ConstraintEdges = ...,
};
using var triangles = new NativeList<int>(Allocator.Persistent);
using var halfedges = new NativeList<int>(Allocator.Persistent);
using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
var output = new LowLevel.Unsafe.OutputData<double2>
{
    Triangles = triangles,
    Halfedges = halfedges,
    ConstrainedHalfedges = constrainedHalfedges,
};
var args = Args.Default();
var t = new UnsafeTriangulator<double2>();
t.Triangulate(input, output, args), Allocator.Persistent);
t.PlantHoleSeeds(input, output, args.With(autoHolesAndBoundary: true), Allocator.Persistent);
```

> [!NOTE]  
> Depending on the options, some of the buffers may not be required for [`PlantHoleSeeds`][plant-seeds]. For example, when the user provides `HoleSeeds` in [`InputData<T2>`][n-output-data], `Positions` in [`OutputData<T2>`][n-output-data] must be provided. However, in other cases, it may not be required.

### `RefineMesh`

The extension [`RefineMesh(output, allocator, areaThreshold?, angleThreshold?, concentricShells?, constrainBoundary?)`][refine-mesh] can be used to refine any triangulation mesh, even an already refined one. Please note that both the managed [`TriangulationSettings`][m-settings] and native [`Args`][n-args] provide refinement parameter setups only for [`float`][float] precision. This extension allows you to provide these parameters with the selected precision type `T` in generics. These parameters have the following default values (in the given precision type `T`, if this extension is available for `T`):

- `areaThreshold = 1`;
- `angleThreshold = math.radians(5)`
- `concentricShells = 0.001`

The last optional parameter, `constrainBoundary`, is used to constrain boundary halfedges. Since the refinement algorithm (whether for constrained triangulation or not) requires constrained halfedges at the boundary, not setting this option may cause unexpected behavior, especially when the `restoreBoundary` option is disabled.

Below is an example of refinement in the unsafe context:

```csharp
using var triangles = new NativeList<int>(Allocator.Persistent);
using var halfedges = new NativeList<int>(Allocator.Persistent);
using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
using var outputPositions = new NativeList<float2>(Allocator.Persistent);
var input = new LowLevel.Unsafe.InputData<float2>
{
    Positions = ...,
    ConstraintEdges = ...,
};
var output = new LowLevel.Unsafe.OutputData<float2>
{
    Triangles = triangles,
    Halfedges = halfedges,
    Positions = outputPositions,
    ConstrainedHalfedges = constrainedHalfedges
};
var t = new UnsafeTriangulator<float2>();
t.Triangulate(input, output, Args.Default(restoreBoundary: true), Allocator.Persistent);
t.RefineMesh(output, Allocator.Persistent, areaThreshold: 1, angleThreshold: 0.5f, constrainBoundary: false);
```

### Dynamic triangulation

The [`UnsafeTriangulator<T>`][unsafe-triangulator] API also offers an option for dynamic triangulation. For more details, refer to the [manual](xref:dynamic-triangulation-manual).

[triangulator]: xref:andywiecko.BurstTriangulator.Triangulator`1
[unsafe-triangulator]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.UnsafeTriangulator`1
[n-input-data]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.InputData`1
[n-output-data]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.OutputData`1
[n-args]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.Args
[m-input-data]: xref:andywiecko.BurstTriangulator.InputData`1
[m-output-data]: xref:andywiecko.BurstTriangulator.OutputData`1
[m-settings]: xref:andywiecko.BurstTriangulator.TriangulationSettings
[monobehaviour]: xref:UnityEngine.MonoBehaviour
[args-default]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.Args.Default*
[args-with]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.Args.With*
[triangulate]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.Extensions.Triangulate*
[run]: xref:andywiecko.BurstTriangulator.Extensions.Run*
[plant-seeds]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.Extensions.PlantHoleSeeds*
[refine-mesh]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.Extensions.RefineMesh*
[float]: xref:System.Single
[allocator-temp]: https://docs.unity3d.com/Packages/com.unity.collections@2.2/manual/allocator-overview.html#allocatortemp
