# Input validation

If [`ValidateInput`][validate] is set to true, the provided data will be validated before running the triangulation procedure. Input positions, as well as input constraints, have several restrictions:

- Points count must be greater/equal 3.
- Points positions cannot be duplicated.
- Points cannot contain NaNs or infinities.
- Constraint edges cannot intersect with each other.
- Constraint edges cannot be duplicated or swapped duplicated.
- Zero-length constraint edges are forbidden.
- Constraint edges cannot intersect with points other than the points for which they are defined.

If any of these conditions fail, triangulation will not be calculated. You can catch this as an error by using [`Status`][status] (native, can be used in jobs).

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
[status]: xref:andywiecko.BurstTriangulator.Status
