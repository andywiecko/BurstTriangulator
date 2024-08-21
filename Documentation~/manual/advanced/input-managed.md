# Managed input support

This package provides an extension for easily adapting managed arrays (i.e., standard C# arrays [`T2[]`][array]) for use as input in triangulation. To achieve this, you can use the [`AsNativeArray`][as-native-array]extension. However, the user must manually release the data from the garbage collector by calling [`Handle.Free()`][free].
This step ensures that the garbage collector does not prematurely release memory, preventing accidental memory issues.

```csharp
using var triangulator = new Triangulator<float2>(Allocator.Persistent)
{
    Input = new ManagedInput<float2>
    {
        Positions = new float2[] { new(0, 0), new(1, 0), new(1, 1), new(0, 1) }.AsNativeArray(out var handle)
    },
};
...
handle.Free();
```

> [!NOTE]  
> [`AsNativeArray`][as-native-array] is allocation-free. It provides native array views for triangulation without additional memory allocation.

[array]: xref:System.Array
[free]: xref:andywiecko.BurstTriangulator.Handle.Free*
[as-native-array]: xref:andywiecko.BurstTriangulator.Extensions.AsNativeArray*
