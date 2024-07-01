using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    public class GithubReportedIssuesTests
    {
        [Test, Description("Test checks if triangulation with data from GitHub Issue #30 passes.")]
        public void GithubIssue30Test()
        {
            using var positions = new NativeArray<double2>(GithubIssuesData.Issue30.points, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(GithubIssuesData.Issue30.constraints, Allocator.Persistent);
            using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent)
            {
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges
                },

                Settings =
                {
                    ValidateInput = true,
                    RefineMesh = false,
                    RestoreBoundary = true,
                    Preprocessor = Preprocessor.PCA
                }
            };

            triangulator.Run();

            triangulator.Draw();
        }

        [Test, Description("Test checks if triangulation with data from GitHub Issue #31 passes.")]
        public void GithubIssue31Test()
        {
            using var positions = new NativeArray<double2>(GithubIssuesData.Issue31.points, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(GithubIssuesData.Issue31.constraints, Allocator.Persistent);
            using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent)
            {
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges
                },

                Settings =
                {
                    RefinementThresholds = {
                        Angle = math.radians(15),
                        Area = 4f
                    },

                    ValidateInput = true,
                    RefineMesh = true,
                    RestoreBoundary = true,
                    Preprocessor = Preprocessor.PCA
                }
            };

            triangulator.Run();

            triangulator.Draw();
        }

        [Test]
        public void GithubIssue111Test()
        {
            using var constraintEdges = new NativeArray<int>(GithubIssuesData.Issue111.constraints, Allocator.Persistent);
            using var nativePositions = new NativeArray<double2>(GithubIssuesData.Issue111.points, Allocator.Persistent);
            using var holes = new NativeArray<double2>(GithubIssuesData.Issue111.holes, Allocator.Persistent);

            using var triangulator = new Triangulator(Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = true,
                    RestoreBoundary = true,
                },
                Input =
                {
                    Positions = nativePositions,
                    ConstraintEdges = constraintEdges,
                    HoleSeeds = holes,
                },
            };

            triangulator.Run();

            Assert.That(triangulator.Output.Triangles, Has.Length.GreaterThan(0));
            triangulator.Draw();
        }
    }
}
