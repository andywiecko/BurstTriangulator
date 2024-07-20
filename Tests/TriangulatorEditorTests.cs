using NUnit.Framework;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    public class TriangulatorEditorTests
    {
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
                Settings = { AutoHolesAndBoundary = input.autoHoles, RefineMesh = input.refineMesh, RestoreBoundary = input.restoreBoundary, Preprocessor = input.preprocessor }
            };

            triangulator.Run();
            impl.Run();

            Assert.That(triangulator.Output.Positions.AsArray().ToArray(), Is.EqualTo(impl.Output.Positions.AsArray().ToArray()));
            Assert.That(triangulator.Output.Triangles.AsArray().ToArray(), Is.EqualTo(impl.Output.Triangles.AsArray().ToArray()));
            Assert.That(triangulator.Output.Halfedges.AsArray().ToArray(), Is.EqualTo(impl.Output.Halfedges.AsArray().ToArray()));

            if (holes.IsCreated) { holes.Dispose(); }
            if (constraints.IsCreated) { constraints.Dispose(); }
        }

        [Test]
        public void ManagedInputTest([Values] bool constrain, [Values] bool holes)
        {
            using var positions = new NativeArray<float2>(LakeSuperior.Points, Allocator.Persistent);
            using var constraints = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);
            using var holeSeeds = new NativeArray<float2>(LakeSuperior.Holes, Allocator.Persistent);
            using var t0 = new Triangulator<float2>(Allocator.Persistent)
            {
                Input = {
                    Positions = positions,
                    ConstraintEdges = constrain ? constraints : default,
                    HoleSeeds = holes ? holeSeeds : default
                },
                Settings = { RestoreBoundary = true },
            };
            using var t1 = new Triangulator<float2>(Allocator.Persistent)
            {
                Input = new ManagedInput<float2>
                {
                    Positions = LakeSuperior.Points,
                    ConstraintEdges = constrain ? LakeSuperior.Constraints : null,
                    HoleSeeds = holes ? LakeSuperior.Holes : null
                },
                Settings = { RestoreBoundary = true },
            };

            t0.Run();
            t1.Run();

            Assert.That(t0.Output.Triangles.AsArray().ToArray(), Is.EqualTo(t1.Output.Triangles.AsArray().ToArray()));
            Assert.That(t0.Output.Halfedges.AsArray().ToArray(), Is.EqualTo(t1.Output.Halfedges.AsArray().ToArray()));
            Assert.That(t0.Output.Positions.AsArray().ToArray(), Is.EqualTo(t1.Output.Positions.AsArray().ToArray()));
        }
    }
}
