# Managed input support

This package provides extensions for easily adapting managed arrays (i.e., standard C# arrays [`T2[]`][array]) for use as input in triangulation. Two extensions are available:

- [`AsNativeArray(out GCHandle)`][as-native-array],
- [`UnsafeAsNativeArray()`][unsafe-as-native-array].

## GCHandle

When using the [`AsNativeArray`][as-native-array] extension, the user must manually release the data from the garbage collector by calling [`GCHandle.Free()`][free].
This extension ensures that the garbage collector does not prematurely release memory, preventing accidental memory issues.

```csharp
var positions = new float2[] { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };
using var triangulator = new Triangulator<float2>(Allocator.Persistent)
{
    Input = new ManagedInput<float2>
    {
        Positions = positions.AsNativeArray(out handle)
    },
};
...
handle.Free();
```

## Unsafe

If the user is certain that the data is properly cached and that the garbage collector will not deallocate it, or if the user prefers to manage garbage collection manually, they can use the [`UnsafeAsNativeArray`][unsafe-as-native-array]

```csharp
using andywiecko.BurstTriangulator.LowLevel.Unsafe;
...
var positions = new float2[] { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };
using var triangulator = new Triangulator<float2>(Allocator.Persistent)
{
    Input = new ManagedInput<float2>
    {
        Positions = positions.UnsafeAsNativeArray()
    },
};
```

> [!NOTE]  
> [`AsNativeArray`][as-native-array] and [`UnsafeAsNativeArray`][unsafe-as-native-array] are allocation-free. They provide native array views for triangulation without additional memory allocation.

[array]: xref:System.Array
[free]: xref:System.Runtime.InteropServices.GCHandle.Free*
[as-native-array]: xref:andywiecko.BurstTriangulator.Extensions.AsNativeArray*
[unsafe-as-native-array]: xref:andywiecko.BurstTriangulator.LowLevel.Unsafe.Extensions.UnsafeAsNativeArray*
