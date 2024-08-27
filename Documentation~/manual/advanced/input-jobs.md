---
uid: example-unity-jobs
---

# Unity.Jobs support

## Add triangulation to job pipeline

This package supports scheduling with Unity.Jobs. You can learn about the job system [**here**](https://docs.unity3d.com/Manual/JobSystem.html). To schedule triangulation, use the [`Schedule`][schedule] method:

```csharp
dependencies = triangulator.Schedule(dependencies);
```

## Catching errors in the job

If the triangulation algorithm fails, checking the status and handling it in the job pipeline can be considered. For example:

```csharp
[BurstCompile]
private struct Job : IJob
{
  NativeReference<Status>.ReadOnly status;

  public Job(Triangulator triangulator)
  {
    status = triangulator.Output.Status.AsReadOnly();
  }

  public void Execute()
  {
    if (status != Status.OK)
    {
      return;
    }

    ...
  }
}

...

var dependencies = default(JobHandle);
dependencies = triangulator.Schedule(dependencies);
dependencies = new Job(triangulator).Schedule(dependencies);

...
```

## Generating input in the job

Input can be generated within a job pipeline. You have to use [deferred arrays][deferred-arrays]. Here's an example snippet:

```csharp
using var positions = new NativeList<double2>(64, Allocator.Persistent);
using var constraints = new NativeList<int>(64, Allocator.Persistent);
using var holes = new NativeList<double2>(64, Allocator.Persistent);
using var triangulator = new Triangulator(64, Allocator.Persistent)
{
  Input = 
  {
    Positions = positions.AsDeferredJobArray(),
    ConstraintEdges = constraints.AsDeferredJobArray(),
    HoleSeeds = holes.AsDeferredJobArray()
  }
}

var dependencies = new JobHandle();
dependencies = new GenerateInputJob(positions, constraints, holes).Schedule(dependencies);
dependencies = triangulator.Schedule(dependencies);
dependencies.Complete();
```

[schedule]: xref:andywiecko.BurstTriangulator.Triangulator.Schedule(Unity.Jobs.JobHandle)
[deferred-arrays]: https://docs.unity3d.com/Packages/com.unity.collections@2.4/api/Unity.Collections.NativeList-1.html#Unity_Collections_NativeList_1_AsDeferredJobArray
