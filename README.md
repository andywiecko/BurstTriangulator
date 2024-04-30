
<p align="center"><img src="Documentation~/images/burst-triangulator-logo-light-mode.svg#gh-light-mode-only"/></p>
<p align="center"><img src="Documentation~/images/burst-triangulator-logo-dark-mode.svg#gh-dark-mode-only"/></p>

[![Build](https://github.com/andywiecko/BurstTriangulator/actions/workflows/build.yml/badge.svg)](https://github.com/andywiecko/BurstTriangulator/actions/workflows/build.yml)
[![Tests](https://github.com/andywiecko/BurstTriangulator/actions/workflows/test.yml/badge.svg)](https://github.com/andywiecko/BurstTriangulator/actions/workflows/test.yml)
[![openupm](https://img.shields.io/npm/v/com.andywiecko.burst.triangulator?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.andywiecko.burst.triangulator/)

A single-file package which provides Delaunay triangulation of the given set of points with constraints and mesh refinement.

## Supported Features

- **Delaunay triangulation**
- **Constrained triangulation**
- **Mesh refinement** (angle and area parameters)
- **Restoring boundary**
- **Holes**
- **Support for `Unity.Jobs` pipeline**
- **Input preprocessors**
- **Input validation**

To view the documentation for the manual and scripting API access it online [**here**][manual] or navigate to `Documentation~/` and build this using `docfx.json`.

## Example results

As an illustrative example, we present the triangulation of Lake Superior with various refinement parameters. The top-left image shows the result without any refinement.

![lake-preview-light](Documentation~/images/lake-preview-light.png#gh-light-mode-only)
![lake-preview-dark](Documentation~/images/lake-preview-dark.png#gh-dark-mode-only)

## Benchmark

The package utilizes the [`Burst`][burst] compiler, which generates highly optimized native code using LLVM.
Below, you'll find a performance comparison for *classic* Delaunay triangulation (without refinement or constraints).
between this package and a few alternatives:

- [`delaunator-sharp`][delaunator-sharp]
- [`CGALDotNet`][cgaldotnet]
- [`Triangle.NET`][triangle-net]

To see more benchmarks visit the [documentation][benchmark].

![Delaunay Benchmark](Documentation~/images/benchmark.png)

## Quick start

Install the package and add `using` in your code

```csharp
using andywiecko.BurstTriangulator;
```

and to triangulate unit box $[(0, 0), (1, 0), (1, 1), (0, 1)]$:

```csharp
using var positions = new NativeArray<float2>(new[]
{ 
    new(0, 0), new(1, 0), new(1, 1), new(0, 1) 
}, Allocator.Persistent);
using var triangulator = new Triangulator(Allocator.Persistent)
{
    Input = { Positions = positions }
};

triangulator.Run();

var triangles = triangulator.Output.Triangles;
```

## Dependencies

- [`Unity.Burst@1.8.7`][burst]
- [`Unity.Collections@2.2.0`][collections]

## Contributions

Found a bug? Please open an issue. You can find a list of known issues [**here**][issues]. Interested in contributing to the project? Feel free to open an issue or submit a pull request. For updates on current and future work related to this package, check out the package [project].

[manual]: https://andywiecko.github.io/BurstTriangulator
[issues]: https://andywiecko.github.io/BurstTriangulator/manual/known-issues.html
[benchmark]: https://andywiecko.github.io/BurstTriangulator/manual/benchmark.html
[project]: https://github.com/andywiecko/BurstTriangulator/projects
[burst]: https://docs.unity3d.com/Packages/com.unity.burst@1.8/
[delaunator-sharp]: https://github.com/nol1fe/delaunator-sharp/
[cgaldotnet]: https://github.com/Scrawk/CGALDotNet
[triangle-net]: https://github.com/wo80/Triangle.NET
[collections]: https://docs.unity3d.com/Packages/com.unity.collections@2.2
