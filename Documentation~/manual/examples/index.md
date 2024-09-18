# Example usage

Below is an example usage of the [`Triangulator`][triangulator] with an input set consisting of four points that form the unit square:

```csharp
using var positions = new NativeArray<double2>(new double2[]
{
  new(0, 0), new(1, 0), new(1, 1), new(0, 1),
}, Allocator.Persistent);
using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent)
{
  Input = { Positions = positions }
};

triangulator.Run();

var triangles = triangulator.Output.Triangles;
```

> [!TIP]  
> The [`triangulator.Run()`][run] method runs on the main thread.
> If you want to call this within a jobs pipeline, schedule a job using [`triangulator.Schedule(dependencies)`][schedule].
> Click [**here**](xref:example-unity-jobs) to learn how to use triangulation within a jobs pipeline.

If triangulation fails for some reason, you can catch the information using [`Status`][status-ref]

```csharp
status = triangulator.Output.Status.Value;
if (status != Status.OK) // ERROR!
{
  return;
}
```

> [!WARNING]  
> The [Status][status-ref] can be used to handle various error codes.
> For more information about other enum values, refer to the [API documentation][status].

The result of the triangulation procedure will depend on the selected settings.
There are a few settings for triangulation, which are briefly described in the [API documentation][settings].
Follow the articles in the manual to learn more about settings.
In other examples, the following *cool* guitar was used as an input test case:

![guitar-light](../../images/guitar-light.svg)

[triangulator]: xref:andywiecko.BurstTriangulator.Triangulator
[settings]: xref:andywiecko.BurstTriangulator.TriangulationSettings
[run]: xref:andywiecko.BurstTriangulator.Triangulator.Run
[schedule]: xref:andywiecko.BurstTriangulator.Triangulator.Schedule(Unity.Jobs.JobHandle)
[status-ref]: xref:andywiecko.BurstTriangulator.OutputData`1.Status
[status]: xref:andywiecko.BurstTriangulator.Status
