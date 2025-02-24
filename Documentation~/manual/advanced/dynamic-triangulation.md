---
uid: dynamic-triangulation-manual
---

# Dynamic triangulation

Using the [`UnsafeTriangulator<T>`][unsafe-triangulator] API, you can perform dynamic triangulation.
This feature is especially useful in scenarios like path-finding in RTS games, where recalculating only a small portion of the mesh, instead of the entire mesh, can significantly improve efficiency.

## DynamicInsertPoint

The [DynamicInsertPoint][dynamic-insert-point] method allows you to insert a point into a specified triangle using barycentric coordinates. This method only supports point insertion within the original triangulation domain and cannot be used to insert points outside the existing mesh.

Inserting a point at specific `T2 p` coordinates can be computationally expensive since it requires locating the triangle that contains the point `p`.
This package does not include acceleration structures, as it assumes the user will implement this based on their specific requirements.
It is recommended to use structures such as bounding volume hierarchies, 2D trees, grids, or buckets for efficient point lookup (`p` $\to \triangle$).
The most suitable acceleration structure may vary depending on the use case.

The [DynamicInsertPoint][dynamic-insert-point] method accepts the following parameters (in addition to `output` and `allocator`):

- `tId` the index of the triangle where the point should be inserted.
- `bar` the barycentric coordinates of the point inside triangle `tId`.

> [!IMPORTANT]  
> All $\texttt{bar}$ coordinates must be valid barycentric coordinates *inside* the triangle, meaning
>
> - $\forall_i\,\,\texttt{bar}[i]\in(0, 1)$;
> - $\sum_i \, \texttt{bar}[i] = 1$.

Here is an example of how to use the API:

```csharp
var t = new UnsafeTriangulator<float2>();

using var positions = new NativeArray<float2>(..., Allocator.Persistent);
using var constraints = new NativeArray<int>(..., Allocator.Persistent);
var input = new NativeInputData<float2>
{
    Positions = positions,
    ConstraintEdges = constraints
};

using var outputPositions = new NativeList<float2>(Allocator.Persistent);
using var triangles = new NativeList<int>(Allocator.Persistent);
using var halfedges = new NativeList<int>(Allocator.Persistent);
using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
var output = new NativeOutputData<float2>
{
    Positions = outputPositions,
    Triangles = triangles,
    Halfedges = halfedges,
    ConstrainedHalfedges = constrainedHalfedges
};

t.Triangulate(input, output, args: Args.Default(autoHolesAndBoundary: true), Allocator.Persistent);

// Insert a new point in triangle with index 42 at the center (barycentric coordinates: [⅓, ⅓, ⅓]).
t.DynamicInsertPoint(output, tId: 42, bar: 1f / 3, allocator: Allocator.Persistent);
```

> [!NOTE]
> To quickly calculate the corresponding `bar` for a given `p` inside a triangle `(a, b, c)`, you can use the following utility function (not included in the package):
>
> ```csharp
> static float3 Bar(float2 p, float2 a, float2 b, float2 c)
> {
>   float cross(float2 x, float2 y) => x.x * y.y - x.y * y.x;
>   var (v0, v1, v2) = (b - a, c - a, p - a);
>   var v = cross(v2, v1) / cross(v0, v1);
>   var w = cross(v0, v2) / cross(v0, v1);
>   var u = 1 - v - w;
>   return math.float3(u, v, w);
> }
> ```

## DynamicSplitHalfedge

The [DynamicSplitHalfedge][dynamic-split-halfedge] method allows you to split specified halfedge by inserting a point at a position determined by linear interpolation.
The position is interpolated between the start and end points of the halfedge in the triangulation using $\alpha$ as the interpolation parameter.
This method preserves the "constrained" state of the halfedge, meaning that if the specified halfedge is constrained, the two resulting sub-segments will also be marked as constrained.

The [DynamicSplitHalfedge][dynamic-split-halfedge] method accepts the following parameters (in addition to `output` and `allocator`):

- `he` the index of the halfedge to split.
- `alpha` the interpolation parameter for positioning the new point between the start and end points of the halfedge, where $p = (1 - \alpha) \, \text{start} + \alpha \, \text{end}$.

Here is an example of how to use the API:

```csharp
var t = new UnsafeTriangulator<float2>();

using var positions = new NativeArray<float2>(..., Allocator.Persistent);
using var constraints = new NativeArray<int>(..., Allocator.Persistent);
var input = new NativeInputData<float2>
{
    Positions = positions,
    ConstraintEdges = constraints
};

using var outputPositions = new NativeList<float2>(Allocator.Persistent);
using var triangles = new NativeList<int>(Allocator.Persistent);
using var halfedges = new NativeList<int>(Allocator.Persistent);
using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
var output = new NativeOutputData<float2>
{
    Positions = outputPositions,
    Triangles = triangles,
    Halfedges = halfedges,
    ConstrainedHalfedges = constrainedHalfedges
};

t.Triangulate(input, output, args: Args.Default(autoHolesAndBoundary: true), Allocator.Persistent);

// Iteratively split random halfedges.
var random = new Unity.Mathematics.Random(seed: 42);
for(int i = 0; i < 32; i++)
{
    t.DynamicInsertPoint(output, he: random.NextInt(0, triangles.Length), alpha: 0.5f, allocator: Allocator.Persistent);
}
```

## DynamicRemoveBulkPoint

The [DynamicRemoveBulkPoint][dynamic-remove-point] method allows you to remove bulk points, i.e. points which are not boundary ones, and re-triangulates the affected region to maintain a valid triangulation.
In addition to `output` and `allocator`, the method requires the index of the point to be removed, specified as `pId`.

Below is an example demonstrating how to use the [DynamicRemoveBulkPoint][dynamic-remove-point] API:

```csharp
using var inputPositions = new NativeArray<double2>(..., Allocator.Persistent);
using var outputPositions = new NativeList<double2>(Allocator.Persistent);
using var triangles = new NativeList<int>(Allocator.Persistent);
using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
using var halfedges = new NativeList<int>(Allocator.Persistent);

var t = new UnsafeTriangulator<double2>();
var input = new NativeInputData<double2> { Positions = inputPositions };
var output = new NativeOutputData<double2>
{
    Positions = outputPositions,
    Triangles = triangles,
    ConstrainedHalfedges = constrainedHalfedges,
    Halfedges = halfedges,
};

t.Triangulate(input, output, args: Args.Default(), allocator: Allocator.Persistent);
t.DynamicRemoveBulkPoint(output, pId: 8, allocator: Allocator.Persistent);
```

[unsafe-triangulator]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.UnsafeTriangulator`1
[dynamic-insert-point]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.Extensions.DynamicInsertPoint*
[dynamic-split-halfedge]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.Extensions.DynamicSplitHalfedge*
[dynamic-remove-point]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.Extensions.DynamicRemoveBulkPoint*
