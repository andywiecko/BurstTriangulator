# Burst Triangulator

A **single-file** package which provides simple Delaunay triangulation of the given set of points (`float2`) with mesh refinement.
Implemented triangulation is based on [Bowyer–Watson algorithm][bowyerwatson] and refinement on [Ruppert's algorithm][rupperts].

## Getting started

To use the package choose one of the following:

- Clone or download this repository and then select `package.json` using Package Manager (`Window/Package Manager`).

- Put the file [`Runtime/Triangulator.cs`](Runtime/Triangulator.cs) somewhere in your project to use this independently.

- Use package manager via git install: `https://github.com/andywiecko/BurstTriangulator.git`.

## Usage

Below one can find example usage of the `Triangulator` with input set as four
points that form the unit square:

```csharp
var positions = new[]
{
    math.float2(0, 0),
    math.float2(1, 0),
    math.float2(1, 1),
    math.float2(0, 1)
};

using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent);
using var inputPositions = new NativeArray<float2>(positions, Allocator.Persistent);

triangulator.Schedule(inputPositions.AsReadOnly(), default).Complete();

var outputTriangles = triangulator.Triangles;
var outputPositions = triangulator.Positions;
```

The result of the triangulation procedure will depend on selected settings.
There are a few settings of the triangulation, shortly described below:

```csharp
var settings = triangulator.Settings;

// Triangle is considered as bad if any of its angles is smaller than MinimumAngle. Note: radians.
settings.MinimumAngle = math.radians(33);
// Triangle is not considered as bad if its area is smaller than MinimumArea.
settings.MinimumArea = 0.015f
// Triangle is considered as bad if its area is greater than MaximumArea.
settings.MaximumArea = 0.5f;
// If true, refines mesh using Ruppert's algorithm.
settings.RefineMesh = true;
// Batch count used in parallel job.
settings.BatchCount = 64;
```

## Example result

The boundary of "Nyan Cat" was used as a test specimen:

![nyan-cat](Documentation~/nyan-cat.png)

The result *without* mesh refinement (Delaunay triangulation):

![nyan-cat-without-refinement](Documentation~/nyan-cat-without-refinement.png)

The result *with* mesh refinement:

![nyan-cat-with-refinement](Documentation~/nyan-cat-with-refinement.png)

Note: to obtain the boundary from a texture, the `UnityEngine.PolygonCollider` was used.
Generating the image boundary is certainly a separate task and is not considered in the project.

## Benchmark

The package uses `Burst` compiler, which produces highly optimized native code using LLVM.
Below one can see a log-log plot of elapsed time as a function of the final triangles count after mesh refinement.
Using Burst can provide more or less two order of magnitude faster computation.

![Burst Triangulator benchmark](Documentation~/burst-benchmark.png "Burst Triangulator benchmark")

## Dependencies

- [`Unity.Burst`](https://docs.unity3d.com/Packages/com.unity.burst@1.7/manual/index.html)
- [`Unity.Mathematics`](https://docs.unity3d.com/Packages/com.unity.mathematics@1.2/manual/index.html)
- [`Unity.Collections`](https://docs.unity3d.com/Packages/com.unity.collections@1.1/manual/index.html)
- [`Unity.Jobs`](https://docs.unity3d.com/Packages/com.unity.jobs@0.11/manual/index.html)

## TODO

- [ ] Use bounding volume (or kd) tree to speed up the computation.
- [ ] Add option of preserving the external edges of input points.
- [ ] Add support for "holes".
- [ ] Consider better parallelism.
- [ ] Do some optimization with respect to SIMD architecture.
- [X] ~~CI/CD setup.~~
- [ ] Add more sophisticated tests.

## Contributors

- [Andrzej Więckowski, Ph.D](https://andywiecko.github.io/).

[bowyerwatson]: https://en.wikipedia.org/wiki/Bowyer%E2%80%93Watson_algorithm
[rupperts]: https://en.wikipedia.org/wiki/Delaunay_refinement#Ruppert's_algorithm
