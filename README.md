# Burst Triangulator

[![Editor tests](https://github.com/andywiecko/BurstTriangulator/actions/workflows/test.yml/badge.svg)](https://github.com/andywiecko/BurstTriangulator/actions/workflows/test.yml)

A **single-file** package which provides simple Delaunay triangulation of the given set of points (`float2`) with mesh refinement.
Implemented triangulation is based on [Bowyer–Watson algorithm][bowyerwatson][^bowyer.1981] [^watson.1981] and refinement on [Ruppert's algorithm][rupperts][^ruppert.1995].

The package provides also constraint triangulation (with mesh refinement) which is based on Sloan's algorithm[^sloan.1993].

## Table of contents

- [Burst Triangulator](#burst-triangulator)
  - [Table of contents](#table-of-contents)
  - [Getting started](#getting-started)
  - [Example usage](#example-usage)
    - [Delaunay triangulation](#delaunay-triangulation)
    - [Delaunay triangulation with mesh refinement](#delaunay-triangulation-with-mesh-refinement)
    - [Constraint Delaunay triangulation](#constraint-delaunay-triangulation)
    - [Constraint Delaunay triangulation with mesh refinement](#constraint-delaunay-triangulation-with-mesh-refinement)
    - [Support for holes and boundaries](#support-for-holes-and-boundaries)
    - [Input validation](#input-validation)
  - [Benchmark](#benchmark)
  - [Dependencies](#dependencies)
  - [TODO](#todo)
  - [Contributors](#contributors)
  - [Bibliography](#bibliography)

## Getting started

Install [`Unity.Burst`][burst] and [`Unity.Jobs`][jobs] and then to use the package choose one of the following:

- Clone or download this repository and then select `package.json` using Package Manager (`Window/Package Manager`).

- Put the file [`Runtime/Triangulator.cs`](Runtime/Triangulator.cs) somewhere in your project to use this independently.

- Use package manager via git install: `https://github.com/andywiecko/BurstTriangulator.git`.

## Example usage

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

triangulator.Input.Positions = inputPositions

triangulator.Run();

var outputTriangles = triangulator.Output.Triangles;
var outputPositions = triangulator.Output.Positions;
```

The result of the triangulation procedure will depend on selected settings.
There are a few settings of the triangulation, shortly described below:

```csharp
var settings = triangulator.Settings;

// Batch count used in parallel job.
settings.BatchCount = 64;
// Triangle is considered as bad if any of its angles is smaller than MinimumAngle. Note: radians.
settings.MinimumAngle = math.radians(33);
// Triangle is not considered as bad if its area is smaller than MinimumArea.
settings.MinimumArea = 0.015f
// Triangle is considered as bad if its area is greater than MaximumArea.
settings.MaximumArea = 0.5f;
// If true refines mesh using Ruppert's algorithm.
settings.RefineMesh = true;
// If true constrains edges defined in the Triangulator.Input.ConstraintEdges
settings.ConstrainEdges = false;
// If true and provided Triangulator.Input is not valid, it will throw an exception.
settings.ValidateInput = true;
```

Below one can find the result of the triangulation for different selected options.

**Note:** to obtain the boundary from a texture, the `UnityEngine.PolygonCollider` was used.
Generating the image boundary is certainly a separate task and is not considered in the project.

### Delaunay triangulation

To use *classic* Delaunay triangulation make sure that constraint and refinement are disabled.

```csharp
settings.RefineMesh = false;
settings.ConstrainEdges = false;
```

The result *without* mesh refinement (Delaunay triangulation):

![nyan-cat-without-refinement](Documentation~/nyan-cat-without-refinement.png)

### Delaunay triangulation with mesh refinement

To proceed with triangulation with the mesh refinement one has to set a proper refinement option

```csharp
settings.RefineMesh = true;
settings.ConstrainEdges = false;
```

Users can control the quality of the triangles by these options

```csharp
// Triangle is considered as bad if any of its angles is smaller than MinimumAngle. Note: radians.
settings.MinimumAngle = math.radians(33);
// Triangle is not considered as bad if its area is smaller than MinimumArea.
settings.MinimumArea = 0.015f
// Triangle is considered as bad if its area is greater than MaximumArea.
settings.MaximumArea = 0.5f;
```

The result *with* mesh refinement:

![nyan-cat-with-refinement](Documentation~/nyan-cat-with-refinement.png)

### Constraint Delaunay triangulation

It is not guaranteed that the boundary of the input will be present in the *classic* Delaunay triangulation result.
One needs to specify the constraints to resolve this issue.
To specify the edges which should be present in the final triangulation
provide the additional input data

```csharp
triangulator.Settings.RefineMesh = false;
triangulator.Settings.ConstrainEdges = true;

// Provided input of constraint edges
// (a0, a1), (b0, b1), (c0, c1), ...
// should be in the following form
// constraintEdges elements:
// [0]: a0, [1]: a1, [2]: b0, [3]: b1, ...
using var constraintEdges = new NativeArray<int>(64, Allocator.Persistent);

triangulator.Input.ConstraintEdges = constraintEdges;
```

In the following figure
one can see the non-constraint triangulation result (with yellow),
and user-specified constraints (with red).

![nyan-cat-constraint-disabled](Documentation~/nyan-cat-constraint-disabled.png)

After enabling `Settings.ConstrainEdges = true` and providing the corresponding input, the result of the constraint triangulation fully covers all specified edges by the user 

![nyan-cat-constraint-enabled](Documentation~/nyan-cat-constraint-enabled.png)

### Constraint Delaunay triangulation with mesh refinement

Constraint triangulation can be also refined in the same manner as non-constraint one,
by enabling corresponding options in triangulation settings:

```csharp
triangulator.Settings.RefineMesh = true;
triangulator.Settings.ConstrainEdges = true;
```

In the following figure one can see the non-constraint triangulation result (with yellow), and user-specified constraints (with red) with the refinement.

![nyan-constraint-refinement-disabled](Documentation~/nyan-constraint-refinement-disabled.png)

After enabling the refinement and the constraint and providing the input, the result of the constraint triangulation fully covers all specified edges by the user and the mesh is refined with the given refinement conditions. 

![nyan-constraint-refinement-enabled](Documentation~/nyan-constraint-refinement-enabled.png)

### Support for holes and boundaries

The package provides also an option for restoring the boundaries.
One has to enable corresponding options and provide the constraints

```csharp
settings.RestoreBoundary = true;
settings.ConstraintEdges = true;
```

In the following figure, one can see the constraint triangulation result (with yellow), and user-specified constraints (with red) with the disabled `RestoreBoundary` and refinement enabled.

![nyan-reconstruction-disabled](Documentation~/nyan-reconstruction-disabled.png)

After enabling the `RestoreBoundary` the result of the constraint triangulation fully covers all conditions and all invalid triangles are destroyed.

![nyan-reconstruction-enabled](Documentation~/nyan-reconstruction-enabled.png)

**Hole support:** *Work in progress.*

### Input validation

If `Triangulator.Settings.ValidateInput` is set to true, the provided data will be validated before running the triangulation procedure.
Input positions, as well as input constraints, have a few restrictions:

- Points count must be greater/equal 3.
- Points positions cannot be duplicated.
- Points cannot contain NaNs or infinities.
- Constraint edges cannot intersect with each other.
- Constraint edges cannot be duplicated or swapped duplicated.
- Zero-length constraint edges are forbidden.
- Constraint edges cannot intersect with points other than the points for which they are defined.

**Note:** validation is limited only for Editor.

## Benchmark

The package uses [`Burst`][burst] compiler, which produces highly optimized native code using LLVM.
Below one can see a log-log plot of elapsed time as a function of the final triangles count after mesh refinement.
Using Burst can provide more or less two order of magnitude faster computation.

![Burst Triangulator benchmark](Documentation~/burst-benchmark.png "Burst Triangulator benchmark")

## Dependencies

- [`Unity.Burst`][burst]
- [`Unity.Mathematics`](https://docs.unity3d.com/Packages/com.unity.mathematics@1.2/manual/index.html)
- [`Unity.Collections`](https://docs.unity3d.com/Packages/com.unity.collections@1.1/manual/index.html)
- [`Unity.Jobs`][jobs]

## TODO

- [X] ~~CI/CD setup.~~
- [X] ~~Add more sophisticated tests.~~
- [X] ~~Add option of preserving the external edges of input points.~~
- [X] ~~Refinement for constraint triangulation.~~
- [X] ~~Optimize Bower-Watson with BFS or DFS walk.~~
- [ ] Add support for "holes" as well as mesh boundaries.
- [ ] Use bounding volume (or kd) tree to speed up the computation.
- [ ] Consider better parallelism.
- [ ] Do some optimization with respect to SIMD architecture.
- [ ] Experiment with removing super triangle before refinement.
- [ ] Support for intersecting input constraint edges.
- [ ] Use queues for the refinements.

## Contributors

- [Andrzej Więckowski, Ph.D](https://andywiecko.github.io/).

## Bibliography

[^bowyer.1981]: A. Bowyer. "Computing Dirichlet tessellations". [Comput. J. 24 (2): 162–166 (1981)](https://doi.org/10.1093%2Fcomjnl%2F24.2.162).
[^watson.1981]: D.F. Watson. "Computing the n-dimensional Delaunay tessellation with application to Voronoi polytopes". [Comput. J. 24 (2): 167–172 (1981)](https://doi.org/10.1093%2Fcomjnl%2F24.2.167).
[^sloan.1993]:S.W. Sloan. "A fast algorithm for generating constrained Delaunay triangulations." [Comput. Struct. 47.3:441-450 (1993)](https://doi.org/10.1016/0045-7949(93)90239-A).
[^ruppert.1995]:J. Ruppert. "A Delaunay Refinement Algorithm for Quality 2-Dimensional Mesh Generation". [J. Algorithms 18(3):548-585 (1995)](https://doi.org/10.1006/jagm.1995.1021).

[bowyerwatson]: https://en.wikipedia.org/wiki/Bowyer%E2%80%93Watson_algorithm
[rupperts]: https://en.wikipedia.org/wiki/Delaunay_refinement#Ruppert's_algorithm
[burst]: https://docs.unity3d.com/Packages/com.unity.burst@1.7/manual/index.html
[jobs]: https://docs.unity3d.com/Packages/com.unity.jobs@0.11/manual/index.html