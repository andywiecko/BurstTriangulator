using andywiecko.BurstTriangulator.LowLevel.Unsafe;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using System;
using System.Linq;
using static andywiecko.BurstTriangulator.Utilities;

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
            Assert.That(args.UseAlphaShapeFilter, Is.EqualTo(settings.UseAlphaShapeFilter));
            Assert.That(args.Alpha, Is.EqualTo(settings.AlphaShapeSettings.Alpha));
            Assert.That(args.AlphaShapeProtectPoints, Is.EqualTo(settings.AlphaShapeSettings.ProtectPoints));
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

        [BurstCompile]
        private struct UnsafeTriangulatorWithTempInputAllocatorJob : IJob
        {
            public NativeList<double2> positions;
            public NativeList<int> triangles, halfedges;
            public NativeList<bool> ignoredHalfedgesForPlantingSeeds, constrainedHalfedges;
            public NativeReference<Status> status;

            public void Execute()
            {
                //
                //                2
                //              .'|
                //            .'  |
                //          .' .5 |
                //        .' .'X| |
                //      .'  3---4 |
                //     0 -------- 1
                //
                using var inputPositions = new NativeList<double2>(Allocator.Temp)
                {
                    new(0, 0), new(5, 0), new(5, 5),
                    new(3, 1), new(4, 1), new(4, 2),
                };
                using var inputConstraints = new NativeList<int>(Allocator.Temp)
                {
                    0, 1, 1, 2, 2, 0,
                    3, 4, 4, 5, 5, 3,
                };
                using var ignoreConstraintForPlantingSeeds = new NativeList<bool>(Allocator.Temp)
                {
                    false, false, false,
                    false, false, false,
                };
                using var holeSeeds = new NativeList<double2>(Allocator.Temp)
                {
                    new(4.5, 1.5)
                };

                using var outputPositions = new NativeList<double2>(Allocator.Temp);
                using var outputTriangles = new NativeList<int>(Allocator.Temp);
                using var outputStatus = new NativeReference<Status>(Allocator.Temp);
                using var outputHalfedges = new NativeList<int>(Allocator.Temp);
                using var outputConstrainedHalfedges = new NativeList<bool>(Allocator.Temp);
                using var outputIgnoredHalfedgesForPlantingSeeds = new NativeList<bool>(Allocator.Temp);

                using var p = inputPositions.ToArray(Allocator.Temp);
                using var c = inputConstraints.ToArray(Allocator.Temp);
                using var i = ignoreConstraintForPlantingSeeds.ToArray(Allocator.Temp);
                using var h = holeSeeds.ToArray(Allocator.Temp);

                var input = new NativeInputData<double2>
                {
                    Positions = p,
                    ConstraintEdges = c,
                    IgnoreConstraintForPlantingSeeds = i,
                    HoleSeeds = h,
                };

                var output = new NativeOutputData<double2>
                {
                    Positions = outputPositions,
                    Triangles = outputTriangles,
                    Status = outputStatus,
                    Halfedges = outputHalfedges,
                    ConstrainedHalfedges = outputConstrainedHalfedges,
                    IgnoredHalfedgesForPlantingSeeds = outputIgnoredHalfedgesForPlantingSeeds,
                };

                var t = new UnsafeTriangulator();
                var args = Args.Default(autoHolesAndBoundary: true, refineMesh: true);
                LowLevel.Unsafe.Extensions.Triangulate(t, input, output, args, allocator: Allocator.Temp);

                positions.CopyFrom(outputPositions);
                triangles.CopyFrom(outputTriangles);
                status.CopyFrom(outputStatus);
                halfedges.CopyFrom(outputHalfedges);
                constrainedHalfedges.CopyFrom(constrainedHalfedges);
                ignoredHalfedgesForPlantingSeeds.CopyFrom(outputIgnoredHalfedgesForPlantingSeeds);
            }
        }

        [Test]
        public void UsingTempAllocatorInJobWithInputTempTest()
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
            using var positions = new NativeList<double2>(Allocator.Persistent);
            using var triangles = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            using var ignoredHalfedgesForPlantingSeeds = new NativeList<bool>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var status = new NativeReference<Status>(Status.OK, Allocator.Persistent);
            new UnsafeTriangulatorWithTempInputAllocatorJob
            {
                positions = positions,
                triangles = triangles,
                constrainedHalfedges = constrainedHalfedges,
                halfedges = halfedges,
                ignoredHalfedgesForPlantingSeeds = ignoredHalfedgesForPlantingSeeds,
                status = status,
            }.Run();
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
            var input = new NativeInputData<T>()
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
            var input = new NativeInputData<T>()
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
            var inputWithHoles = new NativeInputData<T>()
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

        [Test]
        public void UnsafeTriangulatorAutoHolesWithIgnoredConstraintsTest()
        {
            // 3 ------------------------ 2
            // |                          |
            // |   5                      |
            // |   |                      |
            // |   |                      |
            // |   |                      |
            // |   |                      |
            // |   |                      |
            // |   |          9 ---- 8    |
            // |   |          |      |    |
            // |   |          |      |    |
            // |   4          6 ---- 7    |
            // |                          |
            // 0 ------------------------ 1
            using var positions = new NativeArray<T>(new float2[]
            {
                math.float2(0, 0),
                math.float2(10, 0),
                math.float2(10, 10),
                math.float2(0, 10),

                math.float2(1, 1),
                math.float2(1, 9),

                math.float2(8, 1),
                math.float2(9, 1),
                math.float2(9, 2),
                math.float2(8, 2),
            }.DynamicCast<T>(), Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(new int[]
            {
                0, 1, 1, 2, 2, 3, 3, 0,
                4, 5,
                6, 7, 7, 8, 8, 9, 9, 6,
            }, Allocator.Persistent);
            using var ignoreConstraints = new NativeArray<bool>(new bool[]
            {
                false, false, false, false,
                true,
                false, false, false, false,
            }, Allocator.Persistent);


            var t = new UnsafeTriangulator<T>();

            using var triangles1 = new NativeList<int>(Allocator.Persistent);
            t.Triangulate(
                input: new() { Positions = positions, ConstraintEdges = constraintEdges, IgnoreConstraintForPlantingSeeds = ignoreConstraints },
                output: new() { Triangles = triangles1 },
                args: Args.Default().With(autoHolesAndBoundary: true), Allocator.Persistent
            );

            using var triangles2 = new NativeList<int>(Allocator.Persistent);
            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            using var ignoredHalfedges = new NativeList<bool>(Allocator.Persistent);
            t.Triangulate(
                input: new() { Positions = positions, ConstraintEdges = constraintEdges, IgnoreConstraintForPlantingSeeds = ignoreConstraints },
                output: new() { Triangles = triangles2, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges, Positions = outputPositions, IgnoredHalfedgesForPlantingSeeds = ignoredHalfedges },
                args: Args.Default(), Allocator.Persistent
            );
            t.PlantHoleSeeds(
                input: default,
                output: new() { Triangles = triangles2, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges, Positions = outputPositions, IgnoredHalfedgesForPlantingSeeds = ignoredHalfedges },
                args: Args.Default().With(autoHolesAndBoundary: true), Allocator.Persistent
            );

            Assert.That(triangles1.AsArray(), Has.Length.EqualTo(3 * 12));
            Assert.That(triangles1.AsArray(), Is.EqualTo(triangles2.AsArray()).Using(TrianglesComparer.Instance));
        }

        [Test]
        public void ConstrainEdgeTest()
        {
            var t = new UnsafeTriangulator<T>();

            using var inputPositions = new NativeArray<T>(
                new float2[] { new(0, 0), new(1, 0), new(1, 1), new(0, 1), }.DynamicCast<T>(),
                Allocator.Persistent
            );

            using var status = new NativeReference<Status>(Status.OK, Allocator.Persistent);
            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            using var triangles = new NativeList<int>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            using var ignoredHalfedgesForPlantingSeeds = new NativeList<bool>(Allocator.Persistent);

            var args = Args.Default();
            var output = new NativeOutputData<T>
            {
                Status = status,
                Positions = outputPositions,
                Triangles = triangles,
                Halfedges = halfedges,
                ConstrainedHalfedges = constrainedHalfedges,
                IgnoredHalfedgesForPlantingSeeds = ignoredHalfedgesForPlantingSeeds,
            };

            t.Triangulate(new() { Positions = inputPositions }, output, args, Allocator.Persistent);

            var p = HasEdge(0, 2) ? math.int2(1, 3) : math.int2(0, 2);
            t.ConstrainEdge(output, pi: p.x, pj: p.y, args, allocator: Allocator.Persistent, ignoreForPlantingSeeds: true);

            var he = FindEdge(p.x, p.y);
            var ohe = halfedges[he];
            Assert.That(HasEdge(p.x, p.y), Is.True);
            var expected = new bool[ignoredHalfedgesForPlantingSeeds.Length];
            expected[he] = true;
            expected[ohe] = true;
            Assert.That(ignoredHalfedgesForPlantingSeeds.AsReadOnly(), Is.EqualTo(expected));

            bool HasEdge(int pi, int pj) => FindEdge(pi, pj) != -1;
            int FindEdge(int pi, int pj)
            {
                for (int i = 0; i < triangles.Length; i++)
                {
                    var qi = triangles[i];
                    var qj = triangles[NextHalfedge(i)];
                    if (pi == qi && pj == qj || pi == qj && pj == qi)
                    {
                        return i;
                    }
                }

                return -1;
            }
        }

        [Test]
        public void UnsafeTriangulatorAlphaShapeTest()
        {
            var t = new UnsafeTriangulator<T>();

            var alpha = 1e-3f;
            var count = 64;
            using var points = new NativeList<float2>(Allocator.Persistent)
            {
                new(0, 0), new(1000, 0), new(1000, 1000), new(0, 1000),
            };
            var random = new Unity.Mathematics.Random(42);
            for (int i = 0; i < count; i++)
            {
                points.Add(random.NextInt2(0, 1000));
            }

            using var positions = new NativeArray<T>(points.AsReadOnly().DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(new[] { 0, 1, 1, 2, 2, 3, 3, 0 }, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraints },
                Settings = { UseAlphaShapeFilter = true, AlphaShapeSettings = { Alpha = alpha, ProtectPoints = true, PreventWindmills = true, ProtectConstraints = true } }
            };
            triangulator.Run();

            triangulator.Draw();

            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            using var triangles = new NativeList<int>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            var output = new NativeOutputData<T>()
            {
                Positions = outputPositions,
                Triangles = triangles,
                Halfedges = halfedges,
                ConstrainedHalfedges = constrainedHalfedges,
            };
            t.Triangulate(new() { Positions = positions, ConstraintEdges = constraints }, output, Args.Default(), Allocator.Persistent);
            t.AlphaShapeFilter(output, Allocator.Persistent, alpha, protectPoints: true, preventWindmills: true, protectConstraints: true);

            Assert.That(triangulator.Output.Triangles.AsReadOnly().ToArray(), Is.EqualTo(triangles.AsReadOnly().ToArray()).Using(TrianglesComparer.Instance));
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
#if UNITY_MATHEMATICS_FIXEDPOINT
            if (typeof(T) == typeof(fp2) && constrain && refine && !holes)
            {
                Assert.Ignore(
                    "This input gets stuck with this configuration.\n" +
                    "\n" +
                    "Explanation: When constraints and refinement are enabled, but restore boundary is not, \n" +
                    "the refinement procedure can quickly get stuck and produce an excessive number of triangles. \n" +
                    "According to the literature, there are many examples suggesting that one should plant holes first, \n" +
                    "then refine the mesh. These small triangles fall outside of `fp2` precision."
                );
            }
#endif

            using var positions = new NativeArray<T>(LakeSuperior.Points.DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);
            using var holesSeeds = new NativeArray<T>(LakeSuperior.Holes.DynamicCast<T>(), Allocator.Persistent);
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
            var input = new NativeInputData<T>()
            {
                Positions = LakeSuperior.Points.DynamicCast<T>().AsNativeArray(out var h1),
            };

            using var triangles1 = new NativeList<int>(Allocator.Persistent);
            t.Triangulate(input, new() { Triangles = triangles1 }, Args.Default(validateInput: false, refineMesh: true), Allocator.Persistent);

            using var triangles2 = new NativeList<int>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            var output = new NativeOutputData<T>
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
            var input = new NativeInputData<T>()
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
            var output = new NativeOutputData<T>
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
            var inputWithHoles = new NativeInputData<T>()
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
            var output = new NativeOutputData<T> { Triangles = triangles2, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges, Positions = outputPositions };
            t.Triangulate(inputWithoutHoles, output, args.With(refineMesh: false), Allocator.Persistent);
            t.PlantHoleSeeds(inputWithHoles, output, args, Allocator.Persistent);
            LowLevel.Unsafe.Extensions.RefineMesh((dynamic)t, (dynamic)output, Allocator.Persistent);

            h1.Free();
            h2.Free();
            h3.Free();
            Assert.That(triangles1.AsArray(), Is.EqualTo(triangles2.AsArray().ToArray()));
        }

        [Test]
        public void DynamicInsertTest()
        {
            var managedInput = new float2[]
            {
                new(0, 0),
                new(3, 0),
                new(3, 3),
                new(0, 3),

                new(1, 1),
                new(2, 1),
                new(2, 2),
                new(1, 2),
            }.DynamicCast<T>();

            int[] managedConstraints =
            {
                0, 1, 1, 2, 2, 3, 3, 0,
                4, 5, 5, 6, 6, 7, 7, 4,
            };

            var t = new UnsafeTriangulator<T>();
            using var positions = new NativeArray<T>(managedInput, Allocator.Persistent);
            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            using var triangles = new NativeList<int>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            using var constraints = new NativeArray<int>(managedConstraints, Allocator.Persistent);
            var input = new NativeInputData<T> { Positions = positions, ConstraintEdges = constraints };
            var output = new NativeOutputData<T> { Positions = outputPositions, Triangles = triangles, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges };

            int FindTriangle(ReadOnlySpan<int> initialTriangles, int j)
            {
                var (s0, s1, s2) = (initialTriangles[3 * j + 0], initialTriangles[3 * j + 1], initialTriangles[3 * j + 2]);
                for (int i = 0; i < triangles.Length / 3; i++)
                {
                    var (t0, t1, t2) = (triangles[3 * i + 0], triangles[3 * i + 1], triangles[3 * i + 2]);
                    if (t0 == s0 && t1 == s1 && t2 == s2)
                    {
                        return i;
                    }
                }
                return -1;
            }

            t.Triangulate(input, output, args: Args.Default(autoHolesAndBoundary: true), Allocator.Persistent);
            TestUtils.Draw(outputPositions.AsReadOnly().CastToFloat2(), triangles.AsReadOnly(), Color.red, duration: 5f);

            var random = new Unity.Mathematics.Random(seed: 42);
            for (int iter = 0; iter < 5; iter++)
            {
                using var initialTriangles = triangles.ToArray(Allocator.Persistent);
                for (int j = 0; j < initialTriangles.Length / 3; j++)
                {
                    var i = FindTriangle(initialTriangles, j);
                    if (i != -1)
                    {
                        t.DynamicInsertPoint(output, tId: i, bar: random.NextBarcoords3(), allocator: Allocator.Persistent);
                    }
                }

                var result = outputPositions.AsReadOnly().CastToFloat2();
                TestUtils.Draw(result.Select(i => i + math.float2((iter + 1) * 4f, 0)).ToArray(), triangles.AsReadOnly(), Color.red, duration: 5f);
                TestUtils.AssertValidTriangulation(result, triangles.AsReadOnly());
            }
        }

        [Test]
        public void DynamicSplitTest([Values] bool bulk)
        {
            var managedInput = new float2[]
            {
                new(0, 0),
                new(1, 0),
                new(1, 1),
                new(0, 1),
            }.DynamicCast<T>();

            int[] managedConstraints =
            {
                0, 1, 1, 2, 2, 3, 3, 0, 0, 2,
            };

            var t = new UnsafeTriangulator<T>();
            using var positions = new NativeArray<T>(managedInput, Allocator.Persistent);
            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            using var triangles = new NativeList<int>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            using var constraints = new NativeArray<int>(managedConstraints, Allocator.Persistent);
            var input = new NativeInputData<T> { Positions = positions, ConstraintEdges = constraints };
            var output = new NativeOutputData<T> { Positions = outputPositions, Triangles = triangles, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges };

            t.Triangulate(input, output, args: Args.Default(), Allocator.Persistent);
            TestUtils.Draw(outputPositions.AsReadOnly().CastToFloat2(), triangles.AsReadOnly(), Color.red, duration: 5f);

            for (int iter = 0; iter < 4; iter++)
            {
                do_iter(iter);
            }

            void do_iter(int iter)
            {
                var count = 0;
                // If `bulk` is enabled we split 2^iter **diagonal** halfedges, where target length is sqrt(2) / 2^iter, otherwise
                // we split 4^(iter+1) **boundary**  halfedges, where target length is 1 / 2^iter.
                var dist = (bulk ? math.sqrt(2) : 1f) / (1 << iter);
                while (count < (bulk ? 1 : 4) << iter)
                {
                    for (int he = 0; he < triangles.Length; he++)
                    {
                        var ell = len(he);
                        if (constrainedHalfedges[he] && (bulk ? halfedges[he] != -1 : halfedges[he] == -1) && math.abs(ell - dist) <= math.EPSILON)
                        {
                            t.DynamicSplitHalfedge(output, he, 0.5f, Allocator.Persistent);
                            count++;
                        }
                    }
                }

                var result = outputPositions.AsReadOnly().CastToFloat2();
                TestUtils.Draw(result.Select(i => i + math.float2((iter + 1) * 2f, 0)).ToArray(), triangles.AsReadOnly(), Color.red, duration: 5f);
                TestUtils.AssertValidTriangulation(result, triangles.AsReadOnly());
            }

            float len(int he)
            {
                var (i, j) = (triangles[he], triangles[NextHalfedge(he)]);
                var (p, q) = (outputPositions[i].ToFloat2(), outputPositions[j].ToFloat2());
                return math.distance(p, q);
            }
        }

        [Test]
        public void DynamicSplitRandomTest()
        {
            var managedInput = new float2[]
            {
                new(0, 0),
                new(3, 0),
                new(3, 3),
                new(0, 3),

                new(1, 1),
                new(2, 1),
                new(2, 2),
                new(1, 2),
            }.DynamicCast<T>();

            int[] managedConstraints =
            {
                0, 1, 1, 2, 2, 3, 3, 0,
                4, 5, 5, 6, 6, 7, 7, 4,
            };

            var t = new UnsafeTriangulator<T>();
            using var positions = new NativeArray<T>(managedInput, Allocator.Persistent);
            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            using var triangles = new NativeList<int>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            using var constraints = new NativeArray<int>(managedConstraints, Allocator.Persistent);
            var input = new NativeInputData<T> { Positions = positions, ConstraintEdges = constraints };
            var output = new NativeOutputData<T> { Positions = outputPositions, Triangles = triangles, Halfedges = halfedges, ConstrainedHalfedges = constrainedHalfedges };

            t.Triangulate(input, output, args: Args.Default(autoHolesAndBoundary: true), Allocator.Persistent);
            TestUtils.Draw(outputPositions.AsReadOnly().CastToFloat2(), triangles.AsReadOnly(), Color.red, duration: 5f);

            var random = new Unity.Mathematics.Random(seed: 42);

            for (int iter = 0; iter < 3; iter++)
            {
                for (int i = 0; i < 16; i++)
                {
                    var he = Find();
                    var alpha = random.NextFloat(0.1f, 0.9f);
                    t.DynamicSplitHalfedge(output, he, alpha, Allocator.Persistent);
                }

                var result = outputPositions.AsReadOnly().CastToFloat2();
                TestUtils.Draw(result.Select(i => i + math.float2((iter + 1) * 4f, 0)).ToArray(), triangles.AsReadOnly(), Color.red, duration: 5f);
                TestUtils.AssertValidTriangulation(result, triangles.AsReadOnly());
            }

            int Find()
            {
                var maxLen = float.MinValue;
                var maxHe = -1;
                for (int he = 0; he < triangles.Length; he++)
                {
                    if (!constrainedHalfedges[he])
                    {
                        continue;
                    }

                    var ell = len(he);
                    if (ell > maxLen)
                    {
                        (maxHe, maxLen) = (he, ell);
                    }
                }
                return maxHe;
            }

            float len(int he)
            {
                var (i, j) = (triangles[he], triangles[NextHalfedge(he)]);
                var (p, q) = (outputPositions[i].ToFloat2(), outputPositions[j].ToFloat2());
                return math.distance(p, q);
            }
        }

        [Test]
        public void RemovePointPartialTriangulationTest()
        {
            var t = new UnsafeTriangulator<T>();

            using var inputPositions = new NativeArray<T>(new[]
            {
                math.double2(0, 0),
                math.double2(3, 0),
                math.double2(3, 3),
                math.double2(0, 3),
                math.double2(1, 0.5f),
                math.double2(2, 0.5f),
                math.double2(2.5f, 1.5f),
                math.double2(1.5f, 2.5f),
                math.double2(0.5f, 1.5f),
                math.double2(1.5f, 1.5f),
            }.DynamicCast<T>(), Allocator.Persistent);

            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            using var triangles = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            t.Triangulate(
                input: new() { Positions = inputPositions },
                output: new() { Positions = outputPositions, Triangles = triangles, ConstrainedHalfedges = constrainedHalfedges, Halfedges = halfedges },
                args: Args.Default(),
                allocator: Allocator.Persistent
            );

            TestUtils.Draw(outputPositions.AsReadOnly().CastToFloat2(), triangles.AsReadOnly(), Color.blue, 5f);

            var constrain = constrainedHalfedges.AsArray();
            for (int i = 0; i < triangles.Length; i++)
            {
                var p = triangles[i];
                var q = triangles[NextHalfedge(i)];
                if (p == 5 && q == 6)
                {
                    constrain[i] = true;
                    constrain[halfedges[i]] = true;
                    break;
                }
            }

            t.DynamicRemoveBulkPoint(
                output: new() { Positions = outputPositions, Triangles = triangles, ConstrainedHalfedges = constrainedHalfedges, Halfedges = halfedges },
                pId: 9,
                allocator: Allocator.Persistent
            );

            TestUtils.Draw(outputPositions.AsReadOnly().CastToFloat2(), triangles.AsReadOnly(), Color.red, 5f);

            Assert.That(outputPositions.AsReadOnly(), Is.EqualTo(inputPositions.AsReadOnly().ToArray()[..^1]));
            Assert.That(triangles.AsReadOnly(), Is.EqualTo(
                //      0  1  2  3  4  5  6  7  8  9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35
                new[] { 6, 1, 5, 5, 1, 4, 7, 2, 6, 6, 2, 1, 1, 0, 4, 4, 0, 8, 8, 3, 7, 7, 3, 2, 0, 3, 8, 6, 5, 4, 4, 8, 6, 8, 7, 6, }
            ));
            Assert.That(halfedges.AsReadOnly(), Is.EqualTo(
                //       0  1   2  3   4   5   6  7   8  9  10 11  12  13 14  15  16  17  18  19  20  21  22 23  24  25  26 27 28  29  30  31  32  33 34  35
                new[] { 11, 3, 27, 1, 14, 28, 23, 9, 34, 7, -1, 0, -1, 15, 4, 13, 26, 30, 25, 21, 33, 19, -1, 6, -1, 18, 16, 2, 5, 32, 17, 35, 29, 20, 8, 31, }
            ));
            var expectedConstrained = new bool[36];
            expectedConstrained[2] = true;
            expectedConstrained[27] = true;
            Assert.That(constrainedHalfedges.AsReadOnly(), Is.EqualTo(expectedConstrained));
        }

        [Test]
        public void RemovePointFullTriangulationTest()
        {
            var t = new UnsafeTriangulator<T>();

            using var inputPositions = new NativeArray<T>(new[]
            {
                math.double2(0, 0),
                math.double2(1, 0),
                math.double2(2, 1),
                math.double2(2, 2),
                math.double2(1, 3),
                math.double2(0, 3),
                math.double2(-1, 2),
                math.double2(-1, 1),
                math.double2(1, 1),
            }.DynamicCast<T>(), Allocator.Persistent);

            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            using var triangles = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            t.Triangulate(
                input: new() { Positions = inputPositions },
                output: new() { Positions = outputPositions, Triangles = triangles, ConstrainedHalfedges = constrainedHalfedges, Halfedges = halfedges },
                args: Args.Default(),
                allocator: Allocator.Persistent
            );

            TestUtils.Draw(outputPositions.AsReadOnly().CastToFloat2(), triangles.AsReadOnly(), Color.blue, 5f);

            var constrain = constrainedHalfedges.AsArray();
            for (int i = 0; i < triangles.Length; i++)
            {
                var p = triangles[i];
                var q = triangles[NextHalfedge(i)];
                if (p == 1 && q == 0)
                {
                    constrain[i] = true;
                    break;
                }
            }

            t.DynamicRemoveBulkPoint(
                output: new() { Positions = outputPositions, Triangles = triangles, ConstrainedHalfedges = constrainedHalfedges, Halfedges = halfedges },
                pId: 8,
                allocator: Allocator.Persistent
            );

            TestUtils.Draw(outputPositions.AsReadOnly().CastToFloat2(), triangles.AsReadOnly(), Color.red, 5f);

            Assert.That(outputPositions.AsReadOnly(), Is.EqualTo(inputPositions.AsReadOnly().ToArray()[..^1]));
            Assert.That(triangles.AsReadOnly(), Is.EqualTo(
                //      0  1  2  3  4  5  6  7  8  9 10 11 12 13 14 15 16 17 
                new[] { 1, 0, 7, 7, 6, 1, 6, 5, 1, 5, 4, 1, 4, 3, 1, 3, 2, 1, }
            ));
            Assert.That(halfedges.AsReadOnly(), Is.EqualTo(
                //       0   1  2   3  4  5   6   7  8   9  10 11  12  13  14  15  16  17 
                new[] { -1, -1, 5, -1, 8, 2, -1, 11, 4, -1, 14, 7, -1, 17, 10, -1, -1, 13, }
            ));
            Assert.That(constrainedHalfedges.AsReadOnly(), Is.EqualTo(new[] {
                true, false, false,
                false, false, false, false, false,
                false, false, false, false, false,
                false, false, false, false, false,
            }));
        }

        [Test]
        public void RemovePointFromMiddleIndexTest()
        {
            var t = new UnsafeTriangulator<T>();

            using var inputPositions = new NativeArray<T>(new[]
            {
                math.double2(0, 0),
                math.double2(1, 0),
                math.double2(0.5f, 1),
                math.double2(1.5f, 1.5f),
                math.double2(0.5f, 2f),
                math.double2(-0.5f, 1.5f),
            }.DynamicCast<T>(), Allocator.Persistent);

            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            using var triangles = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);
            t.Triangulate(
                input: new() { Positions = inputPositions },
                output: new() { Positions = outputPositions, Triangles = triangles, ConstrainedHalfedges = constrainedHalfedges, Halfedges = halfedges },
                args: Args.Default(),
                allocator: Allocator.Persistent
            );

            TestUtils.Draw(outputPositions.AsReadOnly().CastToFloat2(), triangles.AsReadOnly(), Color.blue, 5f);

            t.DynamicRemoveBulkPoint(
                output: new() { Positions = outputPositions, Triangles = triangles, ConstrainedHalfedges = constrainedHalfedges, Halfedges = halfedges },
                pId: 2,
                allocator: Allocator.Persistent
            );

            TestUtils.Draw(outputPositions.AsReadOnly().CastToFloat2(), triangles.AsReadOnly(), Color.red, 5f);

            Assert.That(triangles.AsReadOnly(), Is.EqualTo(
                //      0  1  2  3  4  5  6  7  8 
                new[] { 3, 2, 1, 1, 0, 3, 0, 4, 3, }
            ));
            Assert.That(halfedges.AsReadOnly(), Is.EqualTo(
                //       0   1  2   3  4  5   6   7  8
                new[] { -1, -1, 5, -1, 8, 2, -1, -1, 4, }
            ));
        }

        [Test]
        public void RemovePointFromLakeTest()
        {
            var t = new UnsafeTriangulator<T>();

            using var inputPositions = new NativeArray<T>(LakeSuperior.Points.DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);

            using var outputPositions = new NativeList<T>(Allocator.Persistent);
            using var triangles = new NativeList<int>(Allocator.Persistent);
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Persistent);
            using var halfedges = new NativeList<int>(Allocator.Persistent);

            var output = new NativeOutputData<T> { Positions = outputPositions, Triangles = triangles, ConstrainedHalfedges = constrainedHalfedges, Halfedges = halfedges };

            t.Triangulate(
                input: new() { Positions = inputPositions, ConstraintEdges = constraints },
                output, args: Args.Default(autoHolesAndBoundary: true), allocator: Allocator.Persistent
            );

            var random = new Unity.Mathematics.Random(42);
            for (int i = 0; i < 15; i++)
            {
                t.DynamicInsertPoint(output, random.NextInt(0, triangles.Length / 3), (float3)1 / 3f, Allocator.Persistent);
            }

            TestUtils.Draw(outputPositions.AsReadOnly().CastToFloat2(), triangles.AsReadOnly(), Color.red, 5f);

            for (int i = 0; i < 15; i++)
            {
                t.DynamicRemoveBulkPoint(output, outputPositions.Length - 1, Allocator.Persistent);
            }

            TestUtils.Draw(outputPositions.AsReadOnly().CastToFloat2(), triangles.AsReadOnly(), Color.blue, 5f);

            var result = outputPositions.AsReadOnly().CastToFloat2();
            TestUtils.AssertValidTriangulation(result, triangles.AsReadOnly());
        }
    }
}
