---
uid: utilities-md
---

# Utilities

The package also provides several utilities related to triangulations.
These utilities can be found in the [Utilities] and [Extensions] classes.

## GenerateHalfedges

The method [GenerateHalfedges(Span&lt;int&gt; halfedges, ReadOnlySpan&lt;int&gt; triangles, Allocator allocator)][generate-halfedges] can be used to generate halfedges based on the given triangles. The provided `allocator` is used for temporary data allocation.
This function is especially useful when you have mesh data but need to use halfedges, for example, when iterating through the mesh.
Learn more about halfedges [here](advanced/output-halfedges.md).

An example usage is shown below:

```csharp
Mesh mesh = ...
var triangles = mesh.triangles;
var halfedges = new int[triangles.Length];

Utilities.GenerateHalfedges(halfdeges, triangles, Allocator.Persistent);
```

## GeneratePointTriangleCount

The method [GeneratePointTriangleCount][generate-point-triangle-count] populates the `pointTriangleCount` buffer with the number of triangles each vertex index is part of, based on the provided triangles index buffer.
The provided `pointTriangleCount` buffer for counting triangles must be large enough to accommodate the highest index in `triangles`.
An example usage is shown below:

```csharp
var pointTriangleCount = new int[pointsCount];
Utilities.GeneratePointTriangleCount(pointTriangleCount, triangles);
```

## GenerateTriangleColors

The method [GenerateTriangleColors][generate-triangle-colors] can be used to generate triangle colors using the provided halfedges.
Triangles that share a common edge are assigned the same color index.
The resulting colors contains values in the range $[0, \,\mathtt{colorsCount})$.
Below is an illustration of an example coloring result, where colors represent actual visual colors rather than ids.
Please note that coloring is based on shared edges, i.e. triangles that share only a single vertex may not receive the same color.

<br>
<p align="center"><img src="../images/utilities-triangle-coloring.svg" width="500"/></p>
<br>

Example usage:

```csharp
var halfedges = new int[] {...};
var colors = new int[halfedges.Length / 3];

Utilities.GenerateTriangleColors(colors, halfedges, out var colorsCount, Allocator.Persistent);
```

## InsertSubMesh

The [InsertSubMesh][insert-submesh] utility can be used to combine meshes by inserting a sub-mesh into the *main* mesh.
In such cases, triangle indices must be adjusted accordingly, and this method handles that for you.

Example usage:

```csharp
using var positions = new NativeList<float2>(..., Allocator);
using var triangles = new NativeList<int>(..., Allocator);

var subpositions = new float2[]
{
    math.float2(0, 0), math.float2(1, 0), math.float2(1, 1)
};
var subtriangles = new[] { 0, 1, 2 };

Utilities.InsertSubMesh(positions, triangles, subpositions, subtriangles);
```

## NextHalfedge

The [NextHalfedge(int he)][next-halfedge] method is a simple yet powerful utility.
As its name suggests, it returns the index of the next halfedge.
This is particularly useful when iterating through a triangular mesh.
Learn more about halfedges [here](advanced/output-halfedges.md).

The example below iterates over all edges in a mesh, ensuring each edge is visited only once before drawing the corresponding edge:

```csharp
var triangles = ...
var halfedges = ...
var positions = ...

for (int he = 0; he < halfedges.Length; he++)
{
    if (he > halfedges[he])
    {
        var e0 = triangles[he];
        var e1 = triangles[NextHalfedge(he)];
        var p0 = math.float3((float2)positions[e0], 0);
        var p1 = math.float3((float2)positions[e1], 0);
        Gizmos.DrawLine(p0, p1);
    }
}
```

## Retriangulate

This package provides a utility for [Mesh](xref:UnityEngine.Mesh) retriangulation.
To retriangulate a mesh, use [Retriangulate] extension method.
This method supports non-uniform meshes, including those with *windmill*-like connections between triangles (e.g., the red and blue triangles in the figure in [GenerateTriangleColors](#generatetrianglecolors)).
See the example below:

```csharp
mesh.Retriangulate(
    settings: new()
    {
        AutoHolesAndBoundary = true,
        RefineMesh = true,
        RefinementThresholds = { Angle = 0, Area = 1e-2f }
    },
    axisInput: Axis.XZ,
    uvMap: UVMap.Planar,
);
```

> [!NOTE]
> Refer to the [Retriangulate] API documentation for additional settings.

For more advanced use cases, [`Utilities.RetriangulateMeshJob`][retrianglate-mesh-job] provides greater customization.
This job can be executed on the main thread, scheduled within the Job System, or run inside another job.
The user must allocate all required output containers before running the job. See the example below:

```csharp
using var outputPositions = new NativeList<float3>(Allocator.Persistent);
using var outputTriangles = new NativeList<int>(Allocator.Persistent);
using var meshDataArray = Mesh.AcquireReadOnlyMeshData(mesh);
var meshData = meshDataArray[0];

new RetriangulateMeshJob(meshData, outputPositions, outputTriangles,
    args: Args.Default(autoHolesAndBoundary: true),
    axisInput: Axis.XY
).Schedule(dependencies)

// ...

dependencies.Complete();
```

[Utilities]: xref:andywiecko.BurstTriangulator.Utilities
[Extensions]: xref:andywiecko.BurstTriangulator.Extensions
[generate-halfedges]: xref:andywiecko.BurstTriangulator.Utilities.GenerateHalfedges*
[generate-point-triangle-count]: xref:andywiecko.BurstTriangulator.Utilities.GeneratePointTriangleCount*
[generate-triangle-colors]: xref:andywiecko.BurstTriangulator.Utilities.GenerateTriangleColors*
[insert-submesh]: xref:andywiecko.BurstTriangulator.Utilities.InsertSubMesh*
[next-halfedge]: xref:andywiecko.BurstTriangulator.Utilities.NextHalfedge*
[Retriangulate]: xref:andywiecko.BurstTriangulator.Extensions.Retriangulate*
[retrianglate-mesh-job]: xref:andywiecko.BurstTriangulator.Utilities.RetriangulateMeshJob
