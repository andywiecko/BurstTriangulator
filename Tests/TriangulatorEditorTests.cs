using NUnit.Framework;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.TestTools;

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    public static class TriangulatorTestExtensions
    {
        public static (int, int, int)[] ToTrisTuple(this NativeList<int> triangles) => Enumerable
            .Range(0, triangles.Length / 3)
            .Select(i => (triangles[3 * i], triangles[3 * i + 1], triangles[3 * i + 2]))
            .OrderBy(i => i.Item1).ThenBy(i => i.Item2).ThenBy(i => i.Item3)
            .ToArray();
    }

    public class TriangulatorEditorTests
    {
        private (int, int, int)[] Triangles => triangulator.Output.Triangles.ToTrisTuple();
        private float2[] Positions => triangulator.Output.Positions.ToArray();

        private Triangulator triangulator;

        [SetUp]
        public void SetUp()
        {
            triangulator = new Triangulator(capacity: 1024, Allocator.Persistent);
        }

        [TearDown]
        public void TearDown()
        {
            triangulator?.Dispose();
        }

        [Test]
        public void DelaunayTriangulationWithoutRefinementTest()
        {
            var managedPositions = new[]
            {
                math.float2(0, 0),
                math.float2(1, 0),
                math.float2(1, 1),
                math.float2(0, 1)
            };

            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);

            triangulator.Settings.RefineMesh = false;
            var input = triangulator.Input;
            input.Positions = positions;

            triangulator.Run();

            ///  3 ------- 2
            ///  |      . `|
            ///  |    *    |
            ///  |. `      |
            ///  0 ------- 1

            Assert.That(Triangles, Is.EqualTo(new[] { (1, 0, 2), (2, 0, 3) }));
        }

        private static readonly TestCaseData[] validateInputPositionsTestData = new[]
        {
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(0, 1)
                }
            ) { TestName = "Test Case 1 (points count less than 3)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(0, 0),
                    math.float2(1, 1),
                    math.float2(0, 1)
                }
            ) { TestName = "Test Case 2 (duplicated position)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, float.NaN),
                    math.float2(1, 1),
                    math.float2(0, 1)
                }
            ) { TestName = "Test Case 3 (point with NaN)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, float.PositiveInfinity),
                    math.float2(1, 1),
                    math.float2(0, 1)
                }
            ) { TestName = "Test Case 4 (point with +inf)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, float.NegativeInfinity),
                    math.float2(1, 1),
                    math.float2(0, 1)
                }
            ) { TestName = "Test Case 4 (point with -inf)" },
        };

        [Test, TestCaseSource(nameof(validateInputPositionsTestData))]
        public void ValidateInputPositionsTest(float2[] managedPositions)
        {
            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);
            triangulator.Settings.RefineMesh = false;
            triangulator.Input = new() { Positions = positions };
            Assert.Throws<ArgumentException>(() => triangulator.Run());
        }

        [Test]
        public void DelaunayTriangulationWithRefinementTest()
        {
            var managedPositions = new[]
            {
                math.float2(0, 0),
                math.float2(1, 0),
                math.float2(1, 1),
                math.float2(0, 1)
            };

            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);

            var settings = triangulator.Settings;
            settings.RefineMesh = true;
            settings.MinimumAngle = math.radians(30);
            settings.MinimumArea = 0.3f;
            settings.MaximumArea = 0.3f;

            var input = triangulator.Input;
            input.Positions = positions;

            triangulator.Run();

            ///  3 ------- 2
            ///  |` .   . `|
            ///  |    4    |
            ///  |. `   ` .|
            ///  0 ------- 1

            var expectedPositions = new[]
            {
                math.float2(0, 0),
                math.float2(1, 0),
                math.float2(1, 1),
                math.float2(0, 1),
                math.float2(0.5f, 0.5f)
            };
            Assert.That(Positions, Is.EqualTo(expectedPositions));

            var expectedTriangles = new[]
            {
                (1, 0, 4), (2, 1, 4), (3, 2, 4), (4, 0, 3)
            };
            Assert.That(Triangles, Is.EqualTo(expectedTriangles));
        }

        private static readonly TestCaseData[] edgeConstraintsTestData = new[]
        {
            new TestCaseData(
                //   6 ----- 5 ----- 4
                //   |    .`   `.    |
                //   | .`         `. |
                //   7 ------------- 3
                //   | `.         .` |
                //   |    `.   .`    |
                //   0 ----- 1 ----- 2
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(2, 0),
                    math.float2(2, 1),
                    math.float2(2, 2),
                    math.float2(1, 2),
                    math.float2(0, 2),
                    math.float2(0, 1),
                }, new[] { 1, 5 })
            {
                TestName = "Test case 1",
                ExpectedResult = new[]
                {
                    (1, 0, 7),
                    (1, 7, 5),
                    (2, 1, 3),
                    (4, 3, 5),
                    (5, 3, 1),
                    (6, 5, 7),
                }
            },

            new TestCaseData(
                //   6 ----- 5 ----- 4
                //   |    .`   `.    |
                //   | .`         `. |
                //   7 ------------- 3
                //   | `.         .` |
                //   |    `.   .`    |
                //   0 ----- 1 ----- 2
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(2, 0),
                    math.float2(2, 1),
                    math.float2(2, 2),
                    math.float2(1, 2),
                    math.float2(0, 2),
                    math.float2(0, 1),
                }, new[] { 1, 5, 1, 4 })
            {
                TestName = "Test case 2",
                ExpectedResult = new[]
                {
                    (1, 0, 7),
                    (1, 5, 4),
                    (1, 7, 5),
                    (2, 1, 3),
                    (4, 3, 1),
                    (6, 5, 7),
                }
            },

            new TestCaseData(
                //   9 ----- 8 ----- 7 ----- 6
                //   |    .` `   . ``  `.    |
                //   | .`  :   ..         `. |
                //  10    :  ..        ..... 5
                //   |   :` .   ....`````    |
                //   | ,.:..`````            |
                //  11 ..................... 4
                //   | `. ` . .           .` |
                //   |    `.    ` . .  .`    |
                //   0 ----- 1 ----- 2 ----- 3
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(2, 0),
                    math.float2(3, 0),
                    math.float2(3, 1),
                    math.float2(3, 2),
                    math.float2(3, 3),
                    math.float2(2, 3),
                    math.float2(1, 3),
                    math.float2(0, 3),
                    math.float2(0, 2),
                    math.float2(0, 1),
                }, new[] { 3, 9, 8, 5 })
            {
                TestName = "Test case 3",
                ExpectedResult = new[]
                {
                    (1, 0, 11),
                    (2, 1, 11),
                    (3, 2, 11),
                    (3, 10, 9),
                    (3, 11, 10),
                    (6, 5, 7),
                    (8, 4, 3),
                    (8, 5, 4),
                    (8, 7, 5),
                    (9, 8, 3),
                }
            },
        };

        [Test, TestCaseSource(nameof(edgeConstraintsTestData))]
        public (int, int, int)[] ConstraintDelaunayTriangulationTest(float2[] managedPositions, int[] constraints)
        {
            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);

            var settings = triangulator.Settings;
            settings.ConstrainEdges = true;
            settings.RefineMesh = false;

            var input = triangulator.Input;
            input.Positions = positions;
            input.ConstraintEdges = constraintEdges;

            triangulator.Run();

            return Triangles;
        }

        private static readonly TestCaseData[] validateConstraintDelaunayTriangulationTestData = new[]
        {
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ 0, 2, 1, 3 }
            ) { TestName = "Test Case 1 (edge-edge intersection)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ 0, 2, 0, 2 }
            ) { TestName = "Test Case 2 (duplicated edge)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ 0, 0 }
            ) { TestName = "Test Case 3 (zero-length edge)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(0.5f, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ 0, 2 }
            ) { TestName = "Test Case 4 (edge collinear with other point)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ 0, 5, 1 }
            ) { TestName = "Test Case 5 (odd number of elements in constraints buffer)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ -1, 1, 1, 1 }
            ) { TestName = "Test Case 6a (constraint out of positions range)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ 1, -1, 1, 1 }
            ) { TestName = "Test Case 6b (constraint out of positions range)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ 5, 1, 1, 1 }
            ) { TestName = "Test Case 6c (constraint out of positions range)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ 1, 5, 1, 1 }
            ) { TestName = "Test Case 6d (constraint out of positions range)" },
        };

        [Test, TestCaseSource(nameof(validateConstraintDelaunayTriangulationTestData))]
        public void ValidateConstraintDelaunayTriangulationTest(float2[] managedPositions, int[] constraints)
        {
            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);

            var settings = triangulator.Settings;
            settings.ConstrainEdges = true;
            settings.RefineMesh = false;

            var input = triangulator.Input;
            input.Positions = positions;
            input.ConstraintEdges = constraintEdges;

            Assert.Throws<ArgumentException>(() => triangulator.Run());
        }

        private static readonly TestCaseData[] constraintDelaunayTriangulationWithRefinementTestData = new[]
        {
            //  3 -------- 2
            //  | ' .      |
            //  |    ' .   |
            //  |       ' .|
            //  0 -------- 1
            new TestCaseData(
                new []
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1)
                },
                new []
                {
                    0, 1,
                    1, 2,
                    2, 3,
                    3, 0,
                    0, 2
                },
                new []
                {
                    math.float2(0.5f, 0.5f)
                }
            )
            {
                TestName = "Test case 1 (square)",
                ExpectedResult = new []
                {
                    (1, 0, 4),
                    (2, 1, 4),
                    (3, 2, 4),
                    (4, 0, 3),
                }
            },

            //  5 -------- 4 -------- 3
            //  |       . '|      . ' |
            //  |    . '   |   . '    |
            //  |. '      .|. '       |
            //  0 -------- 1 -------- 2
            new TestCaseData(
                new []
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(2, 0),
                    math.float2(2, 1),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new []
                {
                    0, 1,
                    1, 2,
                    2, 3,
                    3, 4,
                    4, 5,
                    5, 0,
                    0, 3
                },
                new []
                {
                    math.float2(1, 0.5f),
                    math.float2(0.5f, 0),
                    math.float2(0.5f, 0.25f),
                    math.float2(0.75f, 0.625f),
                    math.float2(0.5f, 1),
                    math.float2(1.5f, 1),
                    math.float2(1.5f, 0.75f),
                    math.float2(1.25f, 0.375f),
                    math.float2(1.5f, 0),
                }
            )
            {
                TestName = "Test case 2 (rectangle)",
                ExpectedResult = new []
                {
                    (3, 2, 12),
                    (6, 1, 8),
                    (6, 4, 12),
                    (7, 0, 8),
                    (8, 0, 5),
                    (8, 1, 7),
                    (8, 5, 10),
                    (9, 4, 6),
                    (9, 6, 8),
                    (9, 8, 10),
                    (10, 4, 9),
                    (11, 3, 12),
                    (12, 2, 14),
                    (12, 4, 11),
                    (13, 1, 6),
                    (13, 6, 12),
                    (13, 12, 14),
                    (14, 1, 13)
                }
            },
        };

        [Test, TestCaseSource(nameof(constraintDelaunayTriangulationWithRefinementTestData))]
        public (int, int, int)[] ConstraintDelaunayTriangulationWithRefinementTest(float2[] managedPositions, int[] constraints, float2[] insertedPoints)
        {
            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);

            var settings = triangulator.Settings;
            settings.ConstrainEdges = true;
            settings.RefineMesh = true;
            settings.MinimumArea = .125f;
            settings.MaximumArea = .250f;

            var input = triangulator.Input;
            input.Positions = positions;
            input.ConstraintEdges = constraintEdges;

            triangulator.Run();

            Assert.That(Positions, Is.EqualTo(managedPositions.Union(insertedPoints)));

            return Triangles;
        }

        [Test]
        public void BoundaryReconstructionWithoutRefinementTest()
        {
            // 7 ----- 6       3 ----- 2
            // |       |       |       |
            // |       |       |       |
            // |       5 ----- 4       |
            // |                       |
            // |                       |
            // 0 --------------------- 1
            var managedPositions = new[]
            {
                math.float2(0, 0),
                math.float2(3, 0),
                math.float2(3, 2),
                math.float2(2, 2),
                math.float2(2, 1),
                math.float2(1, 1),
                math.float2(1, 2),
                math.float2(0, 2),
            };

            var constraints = new[]
            {
                0, 1,
                1, 2,
                2, 3,
                3, 4,
                4, 5,
                5, 6,
                6, 7,
                7, 0
            };

            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);

            var settings = triangulator.Settings;
            settings.ConstrainEdges = true;
            settings.RefineMesh = false;
            settings.RestoreBoundary = true;

            var input = triangulator.Input;
            input.Positions = positions;
            input.ConstraintEdges = constraintEdges;

            triangulator.Run();

            var expected = new[]
            {
                (1, 0, 5),
                (2, 1, 4),
                (3, 2, 4),
                (4, 1, 5),
                (5, 0, 7),
                (6, 5, 7),
            };
            Assert.That(Triangles, Is.EqualTo(expected));
        }

        [Test]
        public void BoundaryReconstructionWithRefinementTest()
        {
            // 4.             .2
            // | '.         .' |
            // |   '.     .'   |
            // |     '. .'     |
            // |       3       |
            // |               |
            // 0 ------------- 1
            var managedPositions = new[]
            {
                math.float2(0, 0),
                math.float2(1, 0),
                math.float2(1, 1),
                math.float2(0.5f, 0.25f),
                math.float2(0, 1),
            };

            var constraints = new[]
            {
                0, 1,
                1, 2,
                2, 3,
                3, 4,
                4, 0
            };

            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);

            var settings = triangulator.Settings;
            settings.ConstrainEdges = true;
            settings.RefineMesh = true;
            settings.RestoreBoundary = true;
            settings.MinimumArea = 0.125f;
            settings.MaximumArea = 0.25f;

            var input = triangulator.Input;
            input.Positions = positions;
            input.ConstraintEdges = constraintEdges;

            triangulator.Run();

            var expectedTriangles = new[]
            {
                (1, 0, 3),
                (3, 0, 4),
                (5, 2, 6),
                (6, 1, 3),
                (6, 3, 5),
            };
            Assert.That(Triangles, Is.EqualTo(expectedTriangles));
            Assert.That(Positions, Is.EqualTo(managedPositions.Union(new[] { math.float2(0.75f, 0.625f), math.float2(1f, 0.5f) })));
        }

        private static readonly TestCaseData[] triangulationWithHolesWithoutRefinementTestData = new[]
        {
            //   * --------------------- *
            //   |                       |
            //   |                       |
            //   |       * ----- *       |
            //   |       |   X   |       |
            //   |       |       |       |
            //   |       * ----- *       |
            //   |                       |
            //   |                       |
            //   * --------------------- *
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(3, 0),
                    math.float2(3, 3),
                    math.float2(0, 3),

                    math.float2(1, 1),
                    math.float2(2, 1),
                    math.float2(2, 2),
                    math.float2(1, 2),
                },
                new[]
                {
                    0, 1,
                    1, 2,
                    2, 3,
                    3, 0,

                    4, 5,
                    5, 6,
                    6, 7,
                    7, 4
                },
                new[] { (float2)1.5f }
            )
            {
                TestName = "Test Case 1",
                ExpectedResult = new[]
                {
                    (1, 0, 5),
                    (2, 1, 6),
                    (3, 2, 7),
                    (4, 0, 7),
                    (5, 0, 4),
                    (6, 1, 5),
                    (7, 0, 3),
                    (7, 2, 6),
                }
            },

            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(3, 0),
                    math.float2(3, 3),
                    math.float2(0, 3),

                    math.float2(1, 1),
                    math.float2(2, 1),
                    math.float2(2, 2),
                    math.float2(1, 2),
                },
                new[]
                {
                    0, 1,
                    1, 2,
                    2, 3,
                    3, 0,

                    4, 5,
                    5, 6,
                    6, 7,
                    7, 4
                },
                new[] { (float2)1.5f, (float2)1.5f }
            )
            {
                TestName = "Test Case 2 (duplicated hole)",
                ExpectedResult = new[]
                {
                    (1, 0, 5),
                    (2, 1, 6),
                    (3, 2, 7),
                    (4, 0, 7),
                    (5, 0, 4),
                    (6, 1, 5),
                    (7, 0, 3),
                    (7, 2, 6),
                }
            },

            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(3, 0),
                    math.float2(3, 3),
                    math.float2(0, 3),

                    math.float2(1, 1),
                    math.float2(2, 1),
                    math.float2(2, 2),
                    math.float2(1, 2),
                },
                new[]
                {
                    0, 1,
                    1, 2,
                    2, 3,
                    3, 0,

                    4, 5,
                    5, 6,
                    6, 7,
                    7, 4
                },
                new[] { (float2)1000 }
            )
            {
                TestName = "Test Case 3 (hole out of range)",
                ExpectedResult = new[]
                {
                    (1, 0, 5),
                    (2, 1, 6),
                    (3, 2, 7),
                    (4, 0, 7),
                    (5, 0, 4),
                    (5, 4, 6),
                    (6, 1, 5),
                    (6, 4, 7),
                    (7, 0, 3),
                    (7, 2, 6),
                }
            },

            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(3, 0),
                    math.float2(3, 3),
                    math.float2(0, 3),

                    math.float2(1, 1),
                    math.float2(2, 1),
                    math.float2(2, 2),
                    math.float2(1, 2),
                },
                new[]
                {
                    0, 1,
                    1, 2,
                    2, 3,
                    3, 0,

                    4, 5,
                    5, 6,
                    6, 7,
                    7, 4
                },
                new[] { math.float2(1.5f + 0.25f, 1.5f), math.float2(1.5f - 0.25f, 1.5f) }
            )
            {
                TestName = "Test Case 4 (hole seeds in the same area)",
                ExpectedResult = new[]
                {
                    (1, 0, 5),
                    (2, 1, 6),
                    (3, 2, 7),
                    (4, 0, 7),
                    (5, 0, 4),
                    (6, 1, 5),
                    (7, 0, 3),
                    (7, 2, 6),
                }
            },
        };

        [Test, TestCaseSource(nameof(triangulationWithHolesWithoutRefinementTestData))]
        public (int, int, int)[] TriangulationWithHolesWithoutRefinementTest(float2[] managedPositions, int[] constraints, float2[] holeSeeds)
        {
            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var holes = new NativeArray<float2>(holeSeeds, Allocator.Persistent);

            var settings = triangulator.Settings;
            settings.ConstrainEdges = true;
            settings.RefineMesh = false;
            settings.RestoreBoundary = true;

            var input = triangulator.Input;
            input.Positions = positions;
            input.ConstraintEdges = constraintEdges;
            input.HoleSeeds = holes;

            triangulator.Run();

            return Triangles;
        }

        [Test]
        public void TriangulationWithHolesWithRefinementTest()
        {
            //   * --------------------- *
            //   |                       |
            //   |                       |
            //   |       * ----- *       |
            //   |       |   X   |       |
            //   |       |       |       |
            //   |       * ----- *       |
            //   |                       |
            //   |                       |
            //   * --------------------- *
            var managedPositions = new[]
            {
                math.float2(0, 0),
                math.float2(3, 0),
                math.float2(3, 3),
                math.float2(0, 3),

                math.float2(1, 1),
                math.float2(2, 1),
                math.float2(2, 2),
                math.float2(1, 2),
            };

            var constraints = new[]
            {
                0, 1,
                1, 2,
                2, 3,
                3, 0,

                4, 5,
                5, 6,
                6, 7,
                7, 4
            };

            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var holes = new NativeArray<float2>(new[] { (float2)1.5f }, Allocator.Persistent);

            var settings = triangulator.Settings;
            settings.ConstrainEdges = true;
            settings.RefineMesh = true;
            settings.RestoreBoundary = true;
            settings.MinimumArea = 0.5f;
            settings.MaximumArea = 1.0f;

            var input = triangulator.Input;
            input.Positions = positions;
            input.ConstraintEdges = constraintEdges;
            input.HoleSeeds = holes;

            triangulator.Run();

            var expectedTriangles = new[]
            {
                (4, 0, 10),
                (5, 1, 8),
                (6, 2, 9),
                (7, 3, 11),
                (7, 4, 10),
                (8, 0, 4),
                (8, 4, 5),
                (9, 1, 5),
                (9, 5, 6),
                (10, 3, 7),
                (11, 2, 6),
                (11, 6, 7),
            };
            Assert.That(Triangles, Is.EqualTo(expectedTriangles));

            Assert.That(Positions, Is.EqualTo(managedPositions.Union(new[]
            {
                math.float2(1.5f, 0),
                math.float2(3, 1.5f),
                math.float2(0, 1.5f),
                math.float2(1.5f, 3f)
            })));
        }

        [BurstCompile]
        private struct DeferredArraySupportInputJob : IJob
        {
            public NativeList<float2> positions;
            public NativeList<int> constraints;
            public NativeList<float2> holes;

            //   * --------------------- *
            //   |                       |
            //   |                       |
            //   |       * ----- *       |
            //   |       |   X   |       |
            //   |       |       |       |
            //   |       * ----- *       |
            //   |                       |
            //   |                       |
            //   * --------------------- *
            public void Execute()
            {
                positions.Clear();
                positions.Add(math.float2(0, 0));
                positions.Add(math.float2(3, 0));
                positions.Add(math.float2(3, 3));
                positions.Add(math.float2(0, 3));
                positions.Add(math.float2(1, 1));
                positions.Add(math.float2(2, 1));
                positions.Add(math.float2(2, 2));
                positions.Add(math.float2(1, 2));

                constraints.Clear();
                constraints.Add(0); constraints.Add(1);
                constraints.Add(1); constraints.Add(2);
                constraints.Add(2); constraints.Add(3);
                constraints.Add(3); constraints.Add(0);
                constraints.Add(4); constraints.Add(5);
                constraints.Add(5); constraints.Add(6);
                constraints.Add(6); constraints.Add(7);
                constraints.Add(7); constraints.Add(4);

                holes.Clear();
                holes.Add(1.5f);
            }
        }

        [Test]
        public void DeferredArraySupportTest()
        {
            using var positions = new NativeList<float2>(64, Allocator.Persistent);
            using var constraints = new NativeList<int>(64, Allocator.Persistent);
            using var holes = new NativeList<float2>(64, Allocator.Persistent);
            using var triangulator = new Triangulator(64, Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = true,
                    ConstrainEdges = true,
                    RefineMesh = true,
                    RestoreBoundary = true,
                    MinimumArea = 0.5f,
                    MaximumArea = 1.0f,
                },
                Input =
                {
                    Positions = positions.AsDeferredJobArray(),
                    ConstraintEdges = constraints.AsDeferredJobArray(),
                    HoleSeeds = holes.AsDeferredJobArray()
                }
            };

            var dependencies = new JobHandle();
            dependencies = new DeferredArraySupportInputJob
            {
                positions = positions,
                constraints = constraints,
                holes = holes
            }.Schedule(dependencies);
            dependencies = triangulator.Schedule(dependencies);
            dependencies.Complete();

            var expectedTriangles = new[]
            {
                (4, 0, 10),
                (5, 1, 8),
                (6, 2, 9),
                (7, 3, 11),
                (7, 4, 10),
                (8, 0, 4),
                (8, 4, 5),
                (9, 1, 5),
                (9, 5, 6),
                (10, 3, 7),
                (11, 2, 6),
                (11, 6, 7),
            };
            Assert.That(triangulator.Output.Triangles.ToTrisTuple(), Is.EqualTo(expectedTriangles));
        }

        [Test, Obsolete]
        public void ObsoleteScheduleSupportTest()
        {
            using var positions = new NativeArray<float2>(new float2[] { new(0), new(1, 0), new(1) }, Allocator.Persistent);
            using var triangulator = new Triangulator(64, Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = true,
                    ConstrainEdges = false,
                    RefineMesh = false,
                    RestoreBoundary = false,
                },
                Input = { Positions = positions }
            };

            triangulator.Schedule(positions.AsReadOnly(), default).Complete();

            Assert.That(triangulator.Output.Triangles, Has.Length.EqualTo(3));
        }

        [Test]
        public void LocalTransformationTest()
        {
            float2[] points = { new(0, -1), new(1, 0), new(0, 1), new(-1, 0) };
            points = points.Select(i => 2 * i + 1).ToArray();

            points = points.Select(i => 0.001f * i + 99f).ToArray();

            using var positions = new NativeArray<float2>(points, Allocator.Persistent);
            using var triangulator = new Triangulator(64, Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = true,
                    ConstrainEdges = false,
                    RefineMesh = false,
                    RestoreBoundary = false,
                },
                Input = { Positions = positions }
            };

            triangulator.Settings.UseLocalTransformation = false;
            triangulator.Run();
            var nonLocalTriangles = triangulator.Output.Triangles.ToTrisTuple();

            triangulator.Settings.UseLocalTransformation = true;
            triangulator.Run();
            var localTriangles = triangulator.Output.Triangles.ToTrisTuple();

            Assert.That(nonLocalTriangles, Is.Empty);
            Assert.That(localTriangles, Is.EqualTo(new[] { (1, 0, 3), (2, 1, 3) }));
        }

        [Test]
        public void LocalTransformationWithRefinementTest()
        {
            var n = 20;
            var managedPositions = Enumerable.Range(0, n)
                .Select(i => math.float2(
                 x: math.cos(2 * math.PI * i / n),
                 y: math.sin(2 * math.PI * i / n))).ToArray();
            managedPositions = new float2[] { 0 }.Concat(managedPositions).ToArray();
            managedPositions = managedPositions.Select(x => 0.1f * x + 5f).ToArray();

            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);
            using var triangulator = new Triangulator(1024, Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = true,
                    ConstrainEdges = false,
                    RefineMesh = true,
                    RestoreBoundary = false,
                    MinimumArea = 0.001f,
                    MaximumArea = 0.010f,
                },
                Input =
                {
                    Positions = positions,
                }
            };

            triangulator.Settings.UseLocalTransformation = false;
            triangulator.Run();
            var nonLocalTriangles = triangulator.Output.Triangles.ToTrisTuple();

            triangulator.Settings.UseLocalTransformation = true;
            triangulator.Run();
            var localTriangles = triangulator.Output.Triangles.ToTrisTuple();

            Assert.That(localTriangles, Is.EqualTo(nonLocalTriangles));
            Assert.That(localTriangles, Has.Length.EqualTo(36));
        }

        [Test]
        public void LocalTransformationWithHolesTest()
        {
            var n = 12;
            var innerCircle = Enumerable
                .Range(0, n)
                .Select(i => math.float2(
                    x: 0.75f * math.cos(2 * math.PI * i / n),
                    y: 0.75f * math.sin(2 * math.PI * i / n)))
                .ToArray();

            var outerCircle = Enumerable
                .Range(0, n)
                .Select(i => math.float2(
                    x: 1.5f * math.cos(2 * math.PI * (i + 0.5f) / n),
                    y: 1.5f * math.sin(2 * math.PI * (i + 0.5f) / n)))
                .ToArray();

            var managedPositions = new float2[] { 0 }.Concat(outerCircle).Concat(innerCircle).ToArray();
            managedPositions = managedPositions.Select(x => 0.1f * x + 5f).ToArray();

            var constraints = Enumerable
                .Range(1, n - 1)
                .SelectMany(i => new[] { i, i + 1 })
                .Concat(new[] { n, 1 })
                .Concat(Enumerable.Range(n + 1, n - 1).SelectMany(i => new[] { i, i + 1 }))
                .Concat(new[] { 2 * n, n + 1 })
                .ToArray();

            using var holes = new NativeArray<float2>(new float2[] { 5 }, Allocator.Persistent);
            using var edges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var positions = new NativeArray<float2>(managedPositions, Allocator.Persistent);
            using var triangulator = new Triangulator(1024, Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = true,
                    ConstrainEdges = true,
                    RefineMesh = false,
                    RestoreBoundary = true,
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = edges,
                    HoleSeeds = holes,
                }
            };

            triangulator.Settings.UseLocalTransformation = false;
            triangulator.Run();
            var nonLocalTriangles = triangulator.Output.Triangles.ToTrisTuple();

            triangulator.Settings.UseLocalTransformation = true;
            triangulator.Run();
            var localTriangles = triangulator.Output.Triangles.ToTrisTuple();

            Assert.That(localTriangles, Is.EqualTo(nonLocalTriangles));
            Assert.That(localTriangles, Has.Length.EqualTo(24));
        }

        [Test]
        public void SloanMaxItersTest()
        {
            // Input for this test is grabbed from issue #30 from @mduvergey user.
            // https://github.com/andywiecko/BurstTriangulator/issues/30
            // This test tests editor hanging problem reported in issue #30 and #31.
            float2[] points =
            {
                new float2(14225.59f, -2335.27f), new float2(13380.24f, -2344.72f), new float2(13197.35f, -2119.65f),
                new float2(11750.51f, -2122.18f), new float2(11670.1f, -2186.25f), new float2(11424.88f, -2178.53f),
                new float2(11193.54f, -2025.36f), new float2(11159.36f, -1812.22f), new float2(10956.29f, -1731.62f),
                new float2(10949.03f, -1524.29f), new float2(10727.71f, -1379.53f), new float2(10532.48f, -1145.83f),
                new float2(10525.18f, -906.69f), new float2(10410.57f, -750.73f), new float2(10629.48f, -657.33f),
                new float2(10622f, -625.7f), new float2(10467.05f, -552.15f), new float2(10415.75f, -423.21f),
                new float2(10037.01f, -427.11f), new float2(9997.4f, -487.33f), new float2(9788.02f, -539.44f),
                new float2(9130.03f, -533.95f), new float2(8905.69f, -490.95f), new float2(8842.1f, -396.11f),
                new float2(8410.81f, -407.12f), new float2(8211.88f, -583.32f), new float2(7985.37f, -588.47f),
                new float2(7880.46f, -574.94f), new float2(7200.87f, -574.14f), new float2(6664.29f, -637.89f),
                new float2(6351.84f, -483.61f), new float2(6324.37f, -143.48f), new float2(6093.94f, -152.8f),
                new float2(5743.03f, 213.65f), new float2(5725.63f, 624.21f), new float2(5562.64f, 815.17f),
                new float2(5564.65f, 1145.66f), new float2(4846.4f, 1325.89f), new float2(4362.98f, 1327.97f),
                new float2(5265.89f, 267.31f), new float2(5266.32f, -791.39f), new float2(3806f, -817.38f),
                new float2(3385.84f, -501.25f), new float2(3374.35f, -372.48f), new float2(3555.98f, -321.11f),
                new float2(3549.9f, -272.35f), new float2(3356.27f, -221.45f), new float2(3352.42f, 13.16f),
                new float2(1371.39f, 5.41f), new float2(1362.47f, -191.23f), new float2(1188.9f, -235.72f),
                new float2(1180.86f, -709.59f), new float2(132.26f, -720.07f), new float2(1.94f, -788.66f),
                new float2(-1240.12f, -779.03f), new float2(-1352.26f, -973.64f), new float2(-1665.17f, -973.84f),
                new float2(-1811.91f, -932.75f), new float2(-1919.98f, -772.61f), new float2(-2623.09f, -782.31f),
                new float2(-3030.54f, -634.38f), new float2(-3045.53f, -52.71f), new float2(-3969.83f, -61.28f),
                new float2(-6676.96f, 102.16f), new float2(-7209.27f, 100.12f), new float2(-7729.39f, 178.02f),
                new float2(-8228.73f, 126.39f), new float2(-8409.52f, 164.47f), new float2(-9432.81f, 168.43f),
                new float2(-9586.02f, 116.14f), new float2(-10758.65f, 110.23f), new float2(-10894.94f, 63.53f),
                new float2(-11737.45f, 60.54f), new float2(-11935.7f, 1.79f), new float2(-12437.14f, -4.33f),
                new float2(-12832.19f, 41.15f), new float2(-13271.23f, 30.64f), new float2(-13478.52f, 65.91f),
                new float2(-13729f, 65.71f), new float2(-13846.23f, 21.68f), new float2(-14000.3f, 62.86f),
                new float2(-15224.52f, 58.78f), new float2(-15232.59f, -142.28f), new float2(-4326.12f, -232.09f),
                new float2(-4083.7f, -441.37f), new float2(-3467.35f, -478.48f), new float2(-3040.92f, -1160.16f),
                new float2(7192.14f, -1332.7f), new float2(7249.66f, -939.11f), new float2(8399.41f, -932.84f),
                new float2(8816.72f, -830.49f), new float2(9861.58f, -835.71f), new float2(10065.59f, -1110.57f),
                new float2(10052.32f, -2118.14f), new float2(9006.64f, -2125.78f), new float2(8818.37f, -2203.58f),
                new float2(8846.09f, -2620.2f), new float2(14244.65f, -2650.96f)
            };

            int[] constraintIndices =
            {
                97, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16, 17, 17, 18,
                18, 19, 19, 20, 20, 21, 21, 22, 22, 23, 23, 24, 24, 25, 25, 26, 26, 27, 27, 28, 28, 29, 29, 30, 30, 31, 31, 32, 32, 33,
                33, 34, 34, 35, 35, 36, 36, 37, 37, 38, 38, 39, 39, 40, 40, 41, 41, 42, 42, 43, 43, 44, 44, 45, 45, 46, 46, 47, 47, 48,
                48, 49, 49, 50, 50, 51, 51, 52, 52, 53, 53, 54, 54, 55, 55, 56, 56, 57, 57, 58, 58, 59, 59, 60, 60, 61, 61, 62, 62, 63,
                63, 64, 64, 65, 65, 66, 66, 67, 67, 68, 68, 69, 69, 70, 70, 71, 71, 72, 72, 73, 73, 74, 74, 75, 75, 76, 76, 77, 77, 78,
                78, 79, 79, 80, 80, 81, 81, 82, 82, 83, 83, 84, 84, 85, 85, 86, 86, 87, 87, 88, 88, 89, 89, 90, 90, 91, 91, 92, 92, 93,
                93, 94, 94, 95, 95, 96, 96, 97
            };

            using var inputPositions = new NativeArray<float2>(points, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraintIndices, Allocator.Persistent);

            using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent);
            triangulator.Settings.RefineMesh = false;
            triangulator.Settings.ConstrainEdges = true;
            triangulator.Settings.RestoreBoundary = true;

            triangulator.Input.Positions = inputPositions;
            triangulator.Input.ConstraintEdges = constraintEdges;

            LogAssert.Expect(UnityEngine.LogType.Exception, new Regex("Sloan max iterations exceeded.*"));
            triangulator.Run();
        }
    }
}
