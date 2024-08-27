using andywiecko.BurstTriangulator.LowLevel.Unsafe;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
#if UNITY_MATHEMATICS_FIXEDPOINT
using Unity.Mathematics.FixedPoint;
#endif

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    [BurstCompile]
    public static class BurstCompileStaticMethodsTests
    {
        [BurstCompile]
        public static void ArgsBlitableTest(ref Args args) { }
    }

    public class UnsafeTriangulatorEditorTests
    {
        [Test]
        public void ArgsDefaultTest()
        {
            var settings = new TriangulationSettings();
            var args = Args.Default();

            Assert.That(args.Preprocessor, Is.EqualTo(settings.Preprocessor));
            Assert.That(args.SloanMaxIters, Is.EqualTo(settings.SloanMaxIters));
            Assert.That(args.AutoHolesAndBoundary, Is.EqualTo(settings.AutoHolesAndBoundary));
            Assert.That(args.RefineMesh, Is.EqualTo(settings.RefineMesh));
            Assert.That(args.RestoreBoundary, Is.EqualTo(settings.RestoreBoundary));
            Assert.That(args.ValidateInput, Is.EqualTo(settings.ValidateInput));
            Assert.That(args.Verbose, Is.EqualTo(settings.Verbose));
            Assert.That(args.ConcentricShellsParameter, Is.EqualTo(settings.ConcentricShellsParameter));
            Assert.That(args.RefinementThresholdAngle, Is.EqualTo(settings.RefinementThresholds.Angle));
            Assert.That(args.RefinementThresholdArea, Is.EqualTo(settings.RefinementThresholds.Area));
        }

        [Test] public void ArgsImplicitSettingsCastTest() => Assert.That((Args)new TriangulationSettings(), Is.EqualTo(Args.Default()));

        [Test] public void ArgsWithTest([Values] bool value) => Assert.That(Args.Default().With(autoHolesAndBoundary: value).AutoHolesAndBoundary, Is.EqualTo(value));

        [BurstCompile]
        private struct ArgsWithJob : IJob
        {
            public NativeReference<Args> argsRef;
            public void Execute() => argsRef.Value = argsRef.Value.With(autoHolesAndBoundary: true);
        }

        [Test]
        public void ArgsWithJobTest()
        {
            using var argsRef = new NativeReference<Args>(Args.Default(autoHolesAndBoundary: false), Allocator.Persistent);
            new ArgsWithJob { argsRef = argsRef }.Run();
            Assert.That(argsRef.Value.AutoHolesAndBoundary, Is.True);
        }

        [BurstCompile]
        private struct UnsafeTriangulatorWithTempAllocatorJob : IJob
        {
            public NativeArray<float2> positions;
            public NativeArray<int> constraints;
            public NativeList<int> triangles;

            public void Execute()
            {
                LowLevel.Unsafe.Extensions.Triangulate(new UnsafeTriangulator<float2>(),
                    input: new() { Positions = positions, ConstraintEdges = constraints },
                    output: new() { Triangles = triangles },
                    args: Args.Default(),
                    allocator: Allocator.Temp
                );
            }
        }

        [Test]
        public void UsingTempAllocatorInJobTest()
        {
            // When using Temp allocation e.g. for native it can throw exception:
            //
            // ```
            // InvalidOperationException: The Unity.Collections.NativeList`1[System.Int32]
            // has been declared as [WriteOnly] in the job, but you are reading from it.
            // ```
            //
            // This seems to be a known issue in current Unity.Collections package
            // https://docs.unity3d.com/Packages/com.unity.collections@2.2/manual/issues.html
            //
            // ```
            // All containers allocated with Allocator.Temp on the same thread use a shared
            // AtomicSafetyHandle instance rather than each having their own. Most of the time,
            // this isn't an issue because you can't pass Temp allocated collections into a job.
            // 
            // However, when you use Native*HashMap, NativeParallelMultiHashMap, Native*HashSet,
            // and NativeList together with their secondary safety handle, this shared AtomicSafetyHandle
            // instance is a problem.
            //
            // A secondary safety handle ensures that a NativeArray which aliases a NativeList
            // is invalidated when the NativeList is reallocated due to resizing
            // ```
            using var positions = new NativeArray<float2>(new float2[] { new(0, 0), new(1, 0), new(1, 1), new(0, 1) }, Allocator.Persistent);
            using var constraints = new NativeArray<int>(new[] { 0, 1, 1, 2, 2, 3, 3, 0 }, Allocator.Persistent);
            using var triangles = new NativeList<int>(Allocator.Persistent);
            new UnsafeTriangulatorWithTempAllocatorJob { positions = positions, constraints = constraints, triangles = triangles }.Run();
        }
    }

    [TestFixture(typeof(float2))]
    [TestFixture(typeof(Vector2))]
    [TestFixture(typeof(double2))]
    [TestFixture(typeof(int2))]
#if UNITY_MATHEMATICS_FIXEDPOINT
    [TestFixture(typeof(fp2))]
#endif
    public class UnsafeTriangulatorEditorTests<T> where T : unmanaged
    {
        [Test]
        public void UnsafeTriangulatorOutputPositionsTest()
        {
            using var positions = new NativeArray<T>(LakeSuperior.Points.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);
            using var holesSeeds = new NativeArray<T>(LakeSuperior.Holes.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>(), Allocator.Persistent);
            using var triangles = new NativeList<int>(64, Allocator.Persistent);
            using var outputPositions = new NativeList<T>(64, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holesSeeds },
                Settings = { RestoreBoundary = true },
            };

            new UnsafeTriangulator<T>().Triangulate(
                input: new() { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holesSeeds },
                output: new() { Triangles = triangles, Positions = outputPositions },
                args: Args.Default(restoreBoundary: true),
                allocator: Allocator.Persistent
            );
            triangulator.Run();

            Assert.That(outputPositions.AsArray().ToArray(), Is.EqualTo(triangulator.Output.Positions.AsArray().ToArray()));
        }

        [Test]
        public void UnsafeTriangulatorOutputHalfedgesTest()
        {
            using var positions = new NativeArray<T>(LakeSuperior.Points.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);
            using var holesSeeds = new NativeArray<T>(LakeSuperior.Holes.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>(), Allocator.Persistent);
            using var triangles = new NativeList<int>(64, Allocator.Persistent);
            using var halfedges = new NativeList<int>(64, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holesSeeds },
                Settings = { RestoreBoundary = true },
            };

            new UnsafeTriangulator<T>().Triangulate(
                input: new() { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holesSeeds },
                output: new() { Triangles = triangles, Halfedges = halfedges },
                args: Args.Default(restoreBoundary: true),
                allocator: Allocator.Persistent
            );
            triangulator.Run();

            Assert.That(halfedges.AsArray().ToArray(), Is.EqualTo(triangulator.Output.Halfedges.AsArray().ToArray()));
        }

        [Test]
        public void UnsafeTriangulatorOutputConstrainedHalfedgesTest()
        {
            using var positions = new NativeArray<T>(LakeSuperior.Points.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);
            using var holesSeeds = new NativeArray<T>(LakeSuperior.Holes.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>(), Allocator.Persistent);
            using var triangles = new NativeList<int>(64, Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(64, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holesSeeds },
                Settings = { RestoreBoundary = true },
            };

            new UnsafeTriangulator<T>().Triangulate(
                input: new() { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holesSeeds },
                output: new() { Triangles = triangles, ConstrainedHalfedges = constrainedHalfedges },
                args: Args.Default(restoreBoundary: true),
                allocator: Allocator.Persistent
            );
            triangulator.Run();

            Assert.That(constrainedHalfedges.AsArray().ToArray(), Is.EqualTo(triangulator.Output.ConstrainedHalfedges.AsArray().ToArray()));
        }

        [Test]
        public void UnsafeTriangulatorOutputStatusTest()
        {
            using var positions = new NativeArray<T>(LakeSuperior.Points.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);
            using var holesSeeds = new NativeArray<T>(LakeSuperior.Holes.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>(), Allocator.Persistent);
            using var triangles = new NativeList<int>(64, Allocator.Persistent);
            using var status = new NativeReference<Status>(Allocator.Persistent);
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holesSeeds },
                Settings = { RestoreBoundary = true },
            };

            new UnsafeTriangulator<T>().Triangulate(
                input: new() { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holesSeeds },
                output: new() { Triangles = triangles, Status = status },
                args: Args.Default(restoreBoundary: true),
                allocator: Allocator.Persistent
            );
            triangulator.Run();

            Assert.That(status.Value, Is.EqualTo(triangulator.Output.Status.Value));
        }

        [Test]
        public void UnsafeTriangulatorPlantHoleSeedsAutoTest()
        {
            var t = new UnsafeTriangulator<T>();
            var input = new LowLevel.Unsafe.InputData<T>()
            {
                Positions = LakeSuperior.Points.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>().AsNativeArray(out var h1),
                ConstraintEdges = LakeSuperior.Constraints.AsNativeArray(out var h2),
            };
            var args = Args.Default(validateInput: false);

            using var triangles1 = new NativeList<int>(Allocator.Persistent);
            t.Triangulate(input, new() { Triangles = triangles1 }, args.With(autoHolesAndBoundary: true), Allocator.Persistent);

            using var triangles2 = new NativeList<int>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);

            t.Triangulate(input, new() { Triangles = triangles2, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges }, args.With(autoHolesAndBoundary: false), Allocator.Persistent);
            t.PlantHoleSeeds(input, new() { Triangles = triangles2, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges }, args.With(autoHolesAndBoundary: true), Allocator.Persistent);

            h1.Free();
            h2.Free();
            Assert.That(triangles1.AsArray(), Is.EqualTo(triangles2.AsArray().ToArray()));
        }

        [Test]
        public void UnsafeTriangulatorPlantHoleSeedsRestoreBoundaryTest()
        {
            var t = new UnsafeTriangulator<T>();
            var input = new LowLevel.Unsafe.InputData<T>()
            {
                Positions = LakeSuperior.Points.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>().AsNativeArray(out var h1),
                ConstraintEdges = LakeSuperior.Constraints.AsNativeArray(out var h2),
            };
            var args = Args.Default(validateInput: false);

            using var triangles1 = new NativeList<int>(Allocator.Persistent);
            t.Triangulate(input, new() { Triangles = triangles1 }, args.With(restoreBoundary: true), Allocator.Persistent);

            using var triangles2 = new NativeList<int>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);

            t.Triangulate(input, new() { Triangles = triangles2, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges }, args.With(restoreBoundary: false), Allocator.Persistent);
            t.PlantHoleSeeds(input, new() { Triangles = triangles2, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges }, args.With(restoreBoundary: true), Allocator.Persistent);

            h1.Free();
            h2.Free();
            Assert.That(triangles1.AsArray(), Is.EqualTo(triangles2.AsArray().ToArray()));
        }

        [Test]
        public void UnsafeTriangulatorPlantHoleSeedsHolesTest()
        {
            var t = new UnsafeTriangulator<T>();
            var inputWithHoles = new LowLevel.Unsafe.InputData<T>()
            {
                Positions = LakeSuperior.Points.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>().AsNativeArray(out var h1),
                ConstraintEdges = LakeSuperior.Constraints.AsNativeArray(out var h2),
                HoleSeeds = LakeSuperior.Holes.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>().AsNativeArray(out var h3),
            };
            var inputWithoutHoles = inputWithHoles;
            inputWithoutHoles.HoleSeeds = default;
            var args = Args.Default(validateInput: false);

            using var triangles1 = new NativeList<int>(Allocator.Persistent);
            t.Triangulate(inputWithHoles, new() { Triangles = triangles1 }, args, Allocator.Persistent);

            using var triangles2 = new NativeList<int>(Allocator.Persistent);
            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);

            t.Triangulate(inputWithoutHoles, new() { Triangles = triangles2, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges, Positions = outputPositions }, args, Allocator.Persistent);
            t.PlantHoleSeeds(inputWithHoles, new() { Triangles = triangles2, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges, Positions = outputPositions }, args, Allocator.Persistent);

            h1.Free();
            h2.Free();
            h3.Free();
            Assert.That(triangles1.AsArray(), Is.EqualTo(triangles2.AsArray().ToArray()));
        }
    }

    [TestFixture(typeof(float2))]
    [TestFixture(typeof(Vector2))]
    [TestFixture(typeof(double2))]
#if UNITY_MATHEMATICS_FIXEDPOINT
    [TestFixture(typeof(fp2))]
#endif
    public class UnsafeTriangulatorEditorTestsWithRefinement<T> where T : unmanaged
    {
        [Test]
        public void UnsafeTriangulatorOutputTrianglesTest([Values] bool constrain, [Values] bool refine, [Values] bool holes)
        {
            using var positions = new NativeArray<T>(LakeSuperior.Points.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);
            using var holesSeeds = new NativeArray<T>(LakeSuperior.Holes.Scale(1000, typeof(T) == typeof(int2)).DynamicCast<T>(), Allocator.Persistent);
            using var triangles = new NativeList<int>(64, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constrain ? constraints : default, HoleSeeds = holes ? holesSeeds : default },
                Settings = { RefineMesh = refine, RestoreBoundary = holes },
            };

            new UnsafeTriangulator<T>().Triangulate(
                input: new() { Positions = positions, ConstraintEdges = constrain ? constraints : default, HoleSeeds = holes ? holesSeeds : default },
                output: new() { Triangles = triangles },
                args: Args.Default(refineMesh: refine, restoreBoundary: holes),
                allocator: Allocator.Persistent
            );
            triangulator.Run();

            Assert.That(triangles.AsArray().ToArray(), Is.EqualTo(triangulator.Output.Triangles.AsArray().ToArray()));
        }

        [Test]
        public void UnsafeTriangulatorRefineMeshTest()
        {
            var t = new UnsafeTriangulator<T>();
            var input = new LowLevel.Unsafe.InputData<T>()
            {
                Positions = LakeSuperior.Points.DynamicCast<T>().AsNativeArray(out var h1),
            };

            using var triangles1 = new NativeList<int>(Allocator.Persistent);
            t.Triangulate(input, new() { Triangles = triangles1 }, Args.Default(validateInput: false, refineMesh: true), Allocator.Persistent);

            using var triangles2 = new NativeList<int>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            var output = new LowLevel.Unsafe.OutputData<T>
            {
                Triangles = triangles2,
                Halfedges = halfedges,
                Positions = outputPositions,
                ConstrainedHalfedges = constrainedHalfedges
            };

            t.Triangulate(input, new() { Triangles = triangles2, Positions = outputPositions, ConstrainedHalfedges = constrainedHalfedges, Halfedges = halfedges }, Args.Default(validateInput: false, refineMesh: false), Allocator.Persistent);
            LowLevel.Unsafe.Extensions.RefineMesh((dynamic)t, (dynamic)output, Allocator.Persistent, constrainBoundary: true);

            h1.Free();
            Assert.That(triangles1.AsArray(), Is.EqualTo(triangles2.AsArray().ToArray()));
        }

        [Test]
        public void UnsafeTriangulatorRefineMeshConstrainedTest()
        {
            var t = new UnsafeTriangulator<T>();
            var input = new LowLevel.Unsafe.InputData<T>()
            {
                Positions = LakeSuperior.Points.DynamicCast<T>().AsNativeArray(out var h1),
                ConstraintEdges = LakeSuperior.Constraints.AsNativeArray(out var h2),
            };
            var args = Args.Default(validateInput: false, refineMesh: true, restoreBoundary: true);

            using var triangles1 = new NativeList<int>(Allocator.Persistent);
            t.Triangulate(input, new() { Triangles = triangles1 }, args, Allocator.Persistent);

            using var triangles2 = new NativeList<int>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            var output = new LowLevel.Unsafe.OutputData<T>
            {
                Triangles = triangles2,
                Halfedges = halfedges,
                Positions = outputPositions,
                ConstrainedHalfedges = constrainedHalfedges
            };

            t.Triangulate(input, new() { Triangles = triangles2, Positions = outputPositions, ConstrainedHalfedges = constrainedHalfedges, Halfedges = halfedges }, args.With(refineMesh: false), Allocator.Persistent);
            LowLevel.Unsafe.Extensions.RefineMesh((dynamic)t, (dynamic)output, Allocator.Persistent, constrainBoundary: false);

            h1.Free();
            h2.Free();
            Assert.That(triangles1.AsArray(), Is.EqualTo(triangles2.AsArray().ToArray()));
        }

        [Test]
        public void UnsafeTriangulatorPlantHoleSeedsRefineMeshTest()
        {
            var t = new UnsafeTriangulator<T>();
            var inputWithHoles = new LowLevel.Unsafe.InputData<T>()
            {
                Positions = LakeSuperior.Points.DynamicCast<T>().AsNativeArray(out var h1),
                ConstraintEdges = LakeSuperior.Constraints.AsNativeArray(out var h2),
                HoleSeeds = LakeSuperior.Holes.DynamicCast<T>().AsNativeArray(out var h3),
            };
            var inputWithoutHoles = inputWithHoles;
            inputWithoutHoles.HoleSeeds = default;
            var args = Args.Default(validateInput: false, refineMesh: true, restoreBoundary: true);

            using var triangles1 = new NativeList<int>(Allocator.Persistent);
            t.Triangulate(inputWithHoles, new() { Triangles = triangles1 }, args, Allocator.Persistent);

            using var triangles2 = new NativeList<int>(Allocator.Persistent);
            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            var output = new LowLevel.Unsafe.OutputData<T> { Triangles = triangles2, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges, Positions = outputPositions };
            t.Triangulate(inputWithoutHoles, output, args.With(refineMesh: false), Allocator.Persistent);
            t.PlantHoleSeeds(inputWithHoles, output, args, Allocator.Persistent);
            LowLevel.Unsafe.Extensions.RefineMesh((dynamic)t, (dynamic)output, Allocator.Persistent);

            h1.Free();
            h2.Free();
            h3.Free();
            Assert.That(triangles1.AsArray(), Is.EqualTo(triangles2.AsArray().ToArray()));
        }
    }
}