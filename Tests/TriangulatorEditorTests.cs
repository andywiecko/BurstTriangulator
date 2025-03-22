using NUnit.Framework;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
#if UNITY_MATHEMATICS_FIXEDPOINT
using Unity.Mathematics.FixedPoint;
#endif

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    public class TriangulatorEditorTests
    {
#if UNITY_MATHEMATICS_FIXEDPOINT
        [Test] public void FixedMathImportTest() => Assert.That((float)fpmath.PI, Is.EqualTo(math.PI).Within(1e-6f));
#endif

        private static string name((bool autoHoles, bool constrainMesh, bool refineMesh, bool useHoles, bool restoreBoundary, Preprocessor preprocessor) input) =>
            "(" +
                $"{nameof(input.autoHoles)}: {input.autoHoles}, " +
                $"{nameof(input.constrainMesh)}: {input.constrainMesh}, " +
                $"{nameof(input.refineMesh)}: {input.refineMesh}, " +
                $"{nameof(input.useHoles)}: {input.useHoles}" +
                $"{nameof(input.restoreBoundary)}: {input.restoreBoundary}" +
                $"{nameof(input.preprocessor)}: {input.preprocessor}" +
            ")"
        ;
        public static readonly TestCaseData[] triangulatorWrapperTestData = new TestCaseData[]
        {
            new((autoHoles: false, constrainMesh: false, refineMesh: false, useHoles: false, restoreBoundary: false, preprocessor: Preprocessor.None)),

            new((autoHoles: false, constrainMesh: true,  refineMesh: false, useHoles: false, restoreBoundary: false, preprocessor: Preprocessor.None)),
            new((autoHoles: true, constrainMesh: true,  refineMesh: false, useHoles: false, restoreBoundary: false, preprocessor: Preprocessor.None)),
            new((autoHoles: false, constrainMesh: true,  refineMesh: true, useHoles: false, restoreBoundary: false, preprocessor: Preprocessor.None)),
            new((autoHoles: false, constrainMesh: true,  refineMesh: false, useHoles: true, restoreBoundary: true, preprocessor: Preprocessor.None)),

            new((autoHoles: false, constrainMesh: false, refineMesh: true, useHoles: false, restoreBoundary: false, preprocessor: Preprocessor.None)),
            new((autoHoles: false, constrainMesh: false, refineMesh: true, useHoles: true, restoreBoundary: true, preprocessor: Preprocessor.None)),
            new((autoHoles: true, constrainMesh: false, refineMesh: true, useHoles: false, restoreBoundary: false, preprocessor: Preprocessor.None)),

            new((autoHoles: false, constrainMesh: false, refineMesh: false, useHoles: false, restoreBoundary: false, preprocessor: Preprocessor.PCA)),
            new((autoHoles: false, constrainMesh: false, refineMesh: false, useHoles: false, restoreBoundary: false, preprocessor: Preprocessor.COM)),
        }.Select(i => { i.TestName = name((dynamic)i.Arguments[0]); return i; }).ToArray();

        [Test, TestCaseSource(nameof(triangulatorWrapperTestData))]
        public void TriangulatorWrapperTest((bool autoHoles, bool constrainMesh, bool refineMesh, bool useHoles, bool restoreBoundary, Preprocessor preprocessor) input)
        {
            using var positions = new NativeArray<double2>(LakeSuperior.Points.Select(i => (double2)i).ToArray(), Allocator.Persistent);
            var holes = input.useHoles ? new NativeArray<double2>(LakeSuperior.Holes.Select(i => (double2)i).ToArray(), Allocator.Persistent) : default;
            var constraints = input.constrainMesh ? new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent) : default;

            using var triangulator = new Triangulator(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holes },
                Settings = { AutoHolesAndBoundary = input.autoHoles, RefineMesh = input.refineMesh, RestoreBoundary = input.restoreBoundary, Preprocessor = input.preprocessor }
            };

            using var impl = new Triangulator<double2>(Allocator.Persistent)
            {
                Input = triangulator.Input,
                Settings = triangulator.Settings,
            };

            triangulator.Run();
            impl.Run();

            Assert.That(triangulator.Output.Positions.AsArray().ToArray(), Is.EqualTo(impl.Output.Positions.AsArray().ToArray()));
            Assert.That(triangulator.Output.Triangles.AsArray().ToArray(), Is.EqualTo(impl.Output.Triangles.AsArray().ToArray()));
            Assert.That(triangulator.Output.Halfedges.AsArray().ToArray(), Is.EqualTo(impl.Output.Halfedges.AsArray().ToArray()));
            Assert.That(triangulator.Output.ConstrainedHalfedges.AsArray().ToArray(), Is.EqualTo(impl.Output.ConstrainedHalfedges.AsArray().ToArray()));
            Assert.That(triangulator.Output.IgnoredHalfedgesForPlantingSeeds.AsArray().ToArray(), Is.EqualTo(impl.Output.IgnoredHalfedgesForPlantingSeeds.AsArray().ToArray()));

            if (holes.IsCreated) { holes.Dispose(); }
            if (constraints.IsCreated) { constraints.Dispose(); }
        }

        [Test]
        public void TriangulatorWrapperSettingsTest()
        {
            var settings = new TriangulationSettings { SloanMaxIters = 42 };
            using var t = new Triangulator(Allocator.Persistent) { Settings = settings };
            Assert.That(t.Settings, Is.EqualTo(settings));
            Assert.That(t.Settings.SloanMaxIters, Is.EqualTo(42));
        }

        [Test]
        public void AsNativeArrayTest()
        {
            int[] a = { 1, 2, 3, 4, 5, 6, };
            var view = a.AsNativeArray(out var handle);
            Assert.That(view, Is.EqualTo(a));
            handle.Free();
        }

        [BurstCompile]
        private struct AsNativeArrayJob : IJob
        {
            public NativeArray<int> a;
            public void Execute()
            {
                for (int i = 0; i < a.Length; i++)
                {
                    a[i] += 1;
                }
            }
        }

        [Test]
        public void AsNativeArrayInJobTest()
        {
            int[] a = { 1, 2, 3, 4, 5, 6, };
            new AsNativeArrayJob { a = a.AsNativeArray(out var handle) }.Run();
            Assert.That(a, Is.EqualTo(new[] { 2, 3, 4, 5, 6, 7 }));
            handle.Free();
        }

        [Test, Description("Checks for memory leaks during Triangulator allocation and disposal.")]
        public void DisposeLeaksTest()
        {
            // Log and forgive any existing leaks before the test.
            // Note: These leaks may not be related to Triangulator and could be caused by other internal systems (e.g., UIElements).
            Unity.Collections.LowLevel.Unsafe.UnsafeUtility.CheckForLeaks();
            new Triangulator(Allocator.Persistent).Dispose();
            var leaks = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.CheckForLeaks();
            Assert.That(leaks, Is.Zero);
        }

        [Test, Description("Checks for memory leaks during Triangulator allocation and disposal (with dependencies).")]
        public void DisposeLeaksWithDependenciesTest()
        {
            // Log and forgive any existing leaks before the test.
            // Note: These leaks may not be related to Triangulator and could be caused by other internal systems (e.g., UIElements).
            Unity.Collections.LowLevel.Unsafe.UnsafeUtility.CheckForLeaks();
            var dependencies = default(JobHandle);
            dependencies = new Triangulator(Allocator.Persistent).Dispose(dependencies);
            dependencies.Complete();
            var leaks = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.CheckForLeaks();
            Assert.That(leaks, Is.Zero);
        }
    }
}
