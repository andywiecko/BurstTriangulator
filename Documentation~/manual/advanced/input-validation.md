# Input validation

If [`ValidateInput`][validate] set to true, the provided [`InputData<T2>`][input-data] and [`TriangulationSettings`][settings] will be validated before executing the triangulation procedure.
The input
  [`InputData<T2>.Positions`][positions],
  [`InputData<T2>.ConstraintEdges`][constraints],
  [`InputData<T2>.HoleSeeds`][holes],
  and [`InputData<T2>.IgnoreConstraintForPlantingSeeds`][ignore-constraint] have certain restrictions:

- [Positions][positions] count must be greater or equal 3.
- [Positions][positions] and [HoleSeeds][holes] cannot contain NaNs or infinities.
- [Positions][positions] cannot be duplicated.
- [ConstraintEdges][constraints] should have length multiple of 2.
- [ConstraintEdges][constraints] should have constraints defined in [Positions][positions] range.
- [ConstraintEdges][constraints] cannot be duplicated, swapped duplicated, or create self-loop.
- [ConstraintEdges][constraints] cannot intersect with each other.
- [ConstraintEdges][constraints] cannot be collinear with positions which is not included in the constraint.
- [IgnoreConstraintForPlantingSeeds][ignore-constraint] must have a length equal to half the length of [ConstraintEdges][constraints].

If any of these conditions fail, triangulation will not be calculated. You can catch this as an error by using [`Status`][status-ref] (native reference, can be used in jobs).
For more details about error codes, refer to the [Status][status] API documentation.

```csharp
using var triangulator = new Triangulator(Allocator.Persistent)
{
  Input = { ... },
  Settings = {
    ValidateInput = true
  },
};

triangulator.Run();

var status = triangulator.Output.Status.Value;
```

> [!WARNING]  
> Input validation can be expensive. If you are certain of your input, consider disabling this option for additional performance.

[validate]: xref:andywiecko.BurstTriangulator.TriangulationSettings.ValidateInput
[input-data]: xref:andywiecko.BurstTriangulator.InputData`1
[settings]: xref:andywiecko.BurstTriangulator.TriangulationSettings
[positions]: xref:andywiecko.BurstTriangulator.InputData`1.Positions
[constraints]: xref:andywiecko.BurstTriangulator.InputData`1.ConstraintEdges
[holes]: xref:andywiecko.BurstTriangulator.InputData`1.HoleSeeds
[ignore-constraint]: xref:andywiecko.BurstTriangulator.InputData`1.IgnoreConstraintForPlantingSeeds
[status-ref]: xref:andywiecko.BurstTriangulator.OutputData`1.Status
[status]: xref:andywiecko.BurstTriangulator.Status
