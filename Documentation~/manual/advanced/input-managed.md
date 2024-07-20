# Managed input support

One can also use managed arrays as triangulator input (i.e., standard C# arrays [`T2[]`][array]). Just use [`new ManagedInput {}`][managed-input] as the input object.

```csharp
using var triangulator = new Triangulator<float2>(Allocator.Persistent)
{
    Input = new ManagedInput<float2>
    {
        Positions = new float2[] { new(0, 0), new(1, 0), new(1, 1), new(0, 1) }
    },
};
```

> [!NOTE]  
> [`ManagedInput<T2>`][managed-input] is allocation-free. It will convert to [`InputData<T2>`][input] without any additional allocation!

[managed-input]: xref:andywiecko.BurstTriangulator.ManagedInput`1
[input]: xref:andywiecko.BurstTriangulator.InputData`1
[array]: xref:System.Array
