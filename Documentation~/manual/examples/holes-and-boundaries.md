---
uid: example-holes-and-boundaries
---

# Holes and boundaries

![guitar-light-cdtrbh](../../images/guitar-light-cdtrbh.svg)

## Restoring boundaries

The package also provides an option for restoring boundaries. In addition to setting the [`RestoreBoundary`][restore-boundary-property] property, one needs to provide edge constraints to restore the boundaries.

```csharp
using var constraintEdges = new NativeArray<int>(..., Allocator.Persistent);
using var positions = new NativeArray<double2>(..., Allocator.Persistent);
using var triangulator = new Triangulator(Allocator.Persistent)
{
  Input = { 
    Positions = positions,
    ConstraintEdges = constraintEdges,
  },
  Settings = {
    RestoreBoundary = true,
  }
};

triangulator.Run();

var triangles = triangulator.Output.Triangles;
```

## Holes supports

The package also provides an option for creating holes.
In addition to setting the [`Input.ConstraintEdges`][input-constraint-edges], a user needs to provide positions of the holes in the same space as the [`Input.Positions`][input-positions]. Enabling the [`RestoreBoundary`][restore-boundary-property] option is not mandatory; holes could be introduced independently of preserving the boundaries.

```csharp
using var constraintEdges = new NativeArray<int>(..., Allocator.Persistent);
using var holes = new NativeArray<double2>(..., Allocator.Persistent);
using var positions = new NativeArray<double2>(..., Allocator.Persistent);
using var triangulator = new Triangulator(Allocator.Persistent)
{
  Input = { 
    Positions = positions,
    ConstraintEdges = constraintEdges,
    HoleSeeds = holes,
  },
  Settings = {
    RestoreBoundary = true, // optional can be set independently
  }
};

triangulator.Run();

var triangles = triangulator.Output.Triangles;
```

## Auto holes and boundary

The package also provides automatic hole detection and restoring boundary. If one sets [`Settings.AutoHolesAndBoundary`][auto-holes-property] to `true`, then holes will be created automatically depending on the provided constraints.

```csharp
using var positions = new NativeArray<double2>(..., Allocator.Persistent);
using var constraintEdges = new NativeArray<int>(..., Allocator.Persistent);
using var triangulator = new Triangulator(Allocator.Persistent)
{
  Input = { 
    Positions = positions,
    ConstraintEdges = constraintEdges,
  },
  Settings = { AutoHolesAndBoundary = true, },
};

triangulator.Run();

var triangles = triangulator.Output.Triangles;
```

> [!WARNING]  
> The current implementation of [`AutoHolesAndBoundary`][auto-holes-property] detects only *1-level islands*.
> It will not detect holes in *solid* meshes inside other holes.

## Ignore constraints for planting seeds

As described in the introduction, the algorithm for triangle removal (and automatic hole detection) is based on a "spreading virus" mechanism, where constrained edges block the propagation. This behavior can be overridden by setting [`IgnoreConstraintForPlantingSeeds`][ignore-constraint] to `true` for a given constraint

```csharp
using var positions = new NativeArray<double2>(..., Allocator.Persistent);
using var constraintEdges = new NativeArray<int>(..., Allocator.Persistent);
using var ignoreConstraint = new NativeArray<bool>(..., Allocator.Persistent);
using var triangulator = new Triangulator(Allocator.Persistent)
{
  Input = {
    Positions = positions,
    ConstraintEdges = constraintEdges,
    IgnoreConstraintForPlantingSeeds = ignoreConstraint,
  },
  Settings = { AutoHolesAndBoundary = true, },
};

triangulator.Run();

var triangles = triangulator.Output.Triangles;
```

This feature is especially useful when the user wants to include a constraint but does not wish to enable hole detection for that edge. Consider the following example input:

<br>
<p align="center"><img src="../../images/manual-ignore-constraint-for-planting-seeds.svg" width="500"/></p>
<br>

In this example, the red constraint is set to `true` in [`IgnoreConstraintForPlantingSeeds`][ignore-constraint]. As a result, a hole is not generated from red constraint, and the edge remains part of the final triangulation.

[restore-boundary-property]: xref:andywiecko.BurstTriangulator.TriangulationSettings.RestoreBoundary
[input-constraint-edges]: xref:andywiecko.BurstTriangulator.InputData`1.ConstraintEdges
[input-positions]: xref:andywiecko.BurstTriangulator.InputData`1.Positions
[auto-holes-property]: xref:andywiecko.BurstTriangulator.TriangulationSettings.AutoHolesAndBoundary
[ignore-constraint]: xref:andywiecko.BurstTriangulator.InputData`1.IgnoreConstraintForPlantingSeeds
