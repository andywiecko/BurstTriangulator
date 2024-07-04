using NUnit.Framework;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    public class GithubReportedIssuesTests
    {
        [Test]
        public void GithubIssue030Test()
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

        [Test]
        public void GithubIssue031Test()
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
        public void GithubIssue105Test()
        {
            using var points = new NativeArray<double2>(GithubIssuesData.Issue105.points, Allocator.Persistent);
            using var edges = new NativeArray<int>(GithubIssuesData.Issue105.constraints, Allocator.Persistent);
            using var triangulator = new Triangulator(capacity: 10_000, Allocator.Persistent)
            {
                Input = { Positions = points, ConstraintEdges = edges, },
                Settings = { RestoreBoundary = true, }
            };

            triangulator.Run();

            Assert.That(triangulator.Output.Triangles, Has.Length.GreaterThan(0));
            triangulator.Draw();
        }

        [Test]
        public void GithubIssue106Test()
        {
            var (count, N) = (100, 204);

            var points = new List<double2>(count * count);
            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    points.Add(new(i / (float)(count - 1), j / (float)(count - 1)));
                }
            }

            var constraints = new List<int>(N + 1);
            for (int k = 0; k < 2; k++)
            {
                var offset = points.Count;
                for (int i = 0; i < N; i++)
                {
                    var phi = 2 * math.PI / N * i + 0.1452f;
                    var p = (0.06f * (k + 1)) * math.double2(math.cos(phi), math.sin(phi)) + 0.5f;
                    points.Add(p);
                    if (i < N - 2)
                    {
                        constraints.Add(offset + i);
                        constraints.Add(offset + ((i + 1) % N));
                    }
                }
            }

            using var positions = new NativeArray<double2>(points.ToArray(), Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints.ToArray(), Allocator.Persistent);
            using var triangulator = new Triangulator(capacity: count * count + N, Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraintEdges },
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
