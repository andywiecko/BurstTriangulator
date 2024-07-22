# Managed input support

One can use managed arrays as triangulator input (i.e., standard C# arrays [`T2[]`][array]). Simply use [`new ManagedInput {}`][managed-input] as the input object.

```csharp
using var triangulator = new Triangulator<float2>(Allocator.Persistent)
{
    Input = new ManagedInput<float2>
    {
        Positions = new float2[] { new(0, 0), new(1, 0), new(1, 1), new(0, 1) }
    },
};
```

Alternatively, one can use the [`AsNativeArray`][as-native-array] extension to use a [`NativeArray<T>`][native-array] view for the managed array in place.

```csharp
float2[] positions = { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };
using var triangulator = new Triangulator<float2>(Allocator.Persistent)
{
    Input = { Positions = positions.AsNativeArray() },
};
```

> [!NOTE]  
> [`ManagedInput<T2>`][managed-input] and [`AsNativeArray`][as-native-array] are allocation-free. Both of them will provide native array views for triangulation [`InputData<T2>`][input].

[managed-input]: xref:andywiecko.BurstTriangulator.ManagedInput`1
[input]: xref:andywiecko.BurstTriangulator.InputData`1
[array]: xref:System.Array
[as-native-array]: xref:andywiecko.BurstTriangulator.Extensions.AsNativeArray*
[native-array]: xref:Unity.Collections.NativeArray`1
