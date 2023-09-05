using NUnit.Framework;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    public static class TriangulatorTestExtensions
    {
        public static (int, int, int)[] GetTrisTuple(this Triangulator triangulator) =>
            triangulator.Output.Triangles.ToTrisTuple();

        private static (int, int, int)[] ToTrisTuple(this NativeList<int> triangles) => Enumerable
            .Range(0, triangles.Length / 3)
            .Select(i => (triangles[3 * i], triangles[3 * i + 1], triangles[3 * i + 2]))
            .OrderBy(i => i.Item1).ThenBy(i => i.Item2).ThenBy(i => i.Item3)
            .ToArray();
    }

    public class Float2Comparer : IEqualityComparer<float2>
    {
        private readonly float epsilon;
        public static readonly Float2Comparer Instance = new(0.0001f);
        public Float2Comparer(float epsilon) => this.epsilon = epsilon;
        public static Float2Comparer With(float epsilon) => new(epsilon);
        public bool Equals(float2 expected, float2 actual) =>
            Utils.AreFloatsEqual(expected.x, actual.x, epsilon) &&
            Utils.AreFloatsEqual(expected.y, actual.y, epsilon);
        public int GetHashCode(float2 _) => 0;
    }

    public class TriangulatorEditorTests
    {
        [Test]
        public void DelaunayTriangulationWithoutRefinementTest()
        {
            ///  3 ------- 2
            ///  |      . `|
            ///  |    *    |
            ///  |. `      |
            ///  0 ------- 1
            using var positions = new NativeArray<float2>(new[]
            {
                math.float2(0, 0),
                math.float2(1, 0),
                math.float2(1, 1),
                math.float2(0, 1)
            }, Allocator.Persistent);

            using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent)
            {
                Settings = { RefineMesh = false },
                Input = { Positions = positions },
            };

            triangulator.Run();

            Assert.That(triangulator.GetTrisTuple(), Is.EqualTo(new[] { (1, 0, 2), (2, 0, 3) }));
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
            using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent)
            {
                Settings = { RefineMesh = false },
                Input = { Positions = positions },
            };

            LogAssert.Expect(UnityEngine.LogType.Error, new Regex(".*"));
            triangulator.Run();

            Assert.That(triangulator.Output.Status.Value, Is.EqualTo(Triangulator.Status.ERR));
        }

        [Test]
        public void DelaunayTriangulationWithRefinementTest()
        {
            ///  3 ------- 2
            ///  |` .   . `|
            ///  |    4    |
            ///  |. `   ` .|
            ///  0 ------- 1
            using var positions = new NativeArray<float2>(new[]
            {
                math.float2(0, 0),
                math.float2(1, 0),
                math.float2(1, 1),
                math.float2(0, 1)
            }, Allocator.Persistent);

            using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
                    RefineMesh = true,
                    MinimumAngle = math.radians(30),
                    MinimumArea = 0.3f,
                    MaximumArea = 0.3f
                },
                Input = { Positions = positions },
            };

            triangulator.Run();

            var expectedPositions = new[]
            {
                math.float2(0, 0),
                math.float2(1, 0),
                math.float2(1, 1),
                math.float2(0, 1),
                math.float2(0.5f, 0.5f)
            };
            Assert.That(triangulator.Output.Positions.AsArray(), Is.EqualTo(expectedPositions));

            var expectedTriangles = new[]
            {
                (1, 0, 4), (2, 1, 4), (3, 2, 4), (4, 0, 3)
            };
            Assert.That(triangulator.GetTrisTuple(), Is.EqualTo(expectedTriangles));
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
            using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
                    ConstrainEdges = true,
                    RefineMesh = false
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges,
                }
            };

            triangulator.Run();

            return triangulator.GetTrisTuple();
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
            using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
                    ConstrainEdges = true,
                    RefineMesh = false
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges,
                }
            };

            LogAssert.Expect(UnityEngine.LogType.Error, new Regex(".*"));
            triangulator.Run();

            Assert.That(triangulator.Output.Status.Value, Is.EqualTo(Triangulator.Status.ERR));
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
            using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
                    ConstrainEdges = true,
                    RefineMesh = true,
                    MinimumArea = .125f,
                    MaximumArea = .250f,
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges,
                }
            };

            triangulator.Run();

            Assert.That(triangulator.Output.Positions.AsArray(), Is.EqualTo(managedPositions.Union(insertedPoints)));

            return triangulator.GetTrisTuple();
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
            using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
                    ConstrainEdges = true,
                    RefineMesh = false,
                    RestoreBoundary = true,
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges,
                }
            };

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
            Assert.That(triangulator.GetTrisTuple(), Is.EqualTo(expected));
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
            using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
                    ConstrainEdges = true,
                    RefineMesh = true,
                    RestoreBoundary = true,
                    MinimumArea = 0.125f,
                    MaximumArea = 0.25f,
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges,
                }
            };

            triangulator.Run();

            var expectedTriangles = new[]
            {
                (1, 0, 3),
                (3, 0, 4),
                (5, 2, 6),
                (6, 1, 3),
                (6, 3, 5),
            };
            Assert.That(triangulator.GetTrisTuple(), Is.EqualTo(expectedTriangles));
            Assert.That(triangulator.Output.Positions.AsArray(), Is.EqualTo(managedPositions.Union(new[] { math.float2(0.75f, 0.625f), math.float2(1f, 0.5f) })));
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
            using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
                    ConstrainEdges = true,
                    RefineMesh = false,
                    RestoreBoundary = true,
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges,
                    HoleSeeds = holes,
                }
            };

            triangulator.Run();

            return triangulator.GetTrisTuple();
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
            using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
                    ConstrainEdges = true,
                    RefineMesh = true,
                    RestoreBoundary = true,
                    MinimumArea = 0.5f,
                    MaximumArea = 1.0f,
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges,
                    HoleSeeds = holes,
                }
            };

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
            Assert.That(triangulator.GetTrisTuple(), Is.EqualTo(expectedTriangles));

            Assert.That(triangulator.Output.Positions.AsArray(), Is.EqualTo(managedPositions.Union(new[]
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
            Assert.That(triangulator.GetTrisTuple(), Is.EqualTo(expectedTriangles));
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

            triangulator.Settings.Preprocessor = Triangulator.Preprocessor.None;
            triangulator.Run();
            var nonLocalTriangles = triangulator.GetTrisTuple();

            triangulator.Settings.Preprocessor = Triangulator.Preprocessor.COM;
            triangulator.Run();
            var localTriangles = triangulator.GetTrisTuple();

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

            triangulator.Settings.Preprocessor = Triangulator.Preprocessor.None;
            triangulator.Run();
            var nonLocalTriangles = triangulator.GetTrisTuple();

            triangulator.Settings.Preprocessor = Triangulator.Preprocessor.COM;
            triangulator.Run();
            var localTriangles = triangulator.GetTrisTuple();

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

            triangulator.Settings.Preprocessor = Triangulator.Preprocessor.None;
            triangulator.Run();
            var nonLocalTriangles = triangulator.GetTrisTuple();

            triangulator.Settings.Preprocessor = Triangulator.Preprocessor.COM;
            triangulator.Run();
            var localTriangles = triangulator.GetTrisTuple();

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

            using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
                    RefineMesh = false,
                    ConstrainEdges = true,
                    RestoreBoundary = true,
                },
                Input =
                {
                    Positions = inputPositions,
                    ConstraintEdges = constraintEdges,
                }
            };

            LogAssert.Expect(UnityEngine.LogType.Error, new Regex("Sloan max iterations exceeded.*"));
            triangulator.Run();
        }

        [Test]
        public void PCATransformationPositionsConservationTest()
        {
            using var positions = new NativeArray<float2>(new[]
            {
                math.float2(1, 1),
                math.float2(2, 10),
                math.float2(2, 11),
                math.float2(1, 2),
            }, Allocator.Persistent);

            using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent)
            {
                Input = { Positions = positions },

                Settings =
                {
                    RefineMesh = false,
                    ConstrainEdges = false,
                    RestoreBoundary = true,
                    Preprocessor = Triangulator.Preprocessor.PCA
                }
            };

            triangulator.Run();

            var result = triangulator.Output.Positions.AsArray().ToArray();
            Assert.That(result, Is.EqualTo(positions).Using(Float2Comparer.With(epsilon: 0.0001f)));
        }

        [Test]
        public void PCATransformationPositionsConservationWithRefinementTest()
        {
            using var positions = new NativeArray<float2>(new[]
            {
                math.float2(1, 1),
                math.float2(2, 10),
                math.float2(2, 11),
                math.float2(1, 2),
            }, Allocator.Persistent);

            using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent)
            {
                Input = { Positions = positions },

                Settings =
                {
                    MinimumArea = 0.01f,
                    MaximumArea = 0.01f,

                    RefineMesh = true,
                    ConstrainEdges = false,
                    RestoreBoundary = true,
                    Preprocessor = Triangulator.Preprocessor.PCA
                }
            };

            triangulator.Run();

            var result = triangulator.Output.Positions.AsArray().ToArray()[..4];
            Assert.That(result, Is.EqualTo(positions).Using(Float2Comparer.With(epsilon: 0.0001f)));
            Assert.That(triangulator.Output.Triangles.Length, Is.GreaterThan(2 * 3));
        }

        [Test]
        public void PCATransformationWithHolesTest()
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
            using var positions = new NativeArray<float2>(new[]
            {
                math.float2(0, 0), math.float2(3, 0), math.float2(3, 3), math.float2(0, 3),
                math.float2(1, 1), math.float2(2, 1), math.float2(2, 2), math.float2(1, 2),
            }, Allocator.Persistent);

            using var constraintEdges = new NativeArray<int>(new[]
            {
                0, 1, 1, 2, 2, 3, 3, 0,
                4, 5, 5, 6, 6, 7, 7, 4
            }, Allocator.Persistent);

            using var holes = new NativeArray<float2>(new[] { math.float2(1.5f) }, Allocator.Persistent);

            using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent)
            {
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges,
                    HoleSeeds = holes,
                },

                Settings =
                {
                    ValidateInput = true,
                    RefineMesh = false,
                    ConstrainEdges = true,
                    RestoreBoundary = true,
                    Preprocessor = Triangulator.Preprocessor.PCA
                }
            };

            triangulator.Run();

            (int, int, int)[] expected =
            {
                (1, 0, 4),
                (2, 1, 5),
                (3, 2, 6),
                (4, 0, 3),
                (4, 3, 7),
                (5, 1, 4),
                (6, 2, 5),
                (7, 3, 6),
            };
            Assert.That(triangulator.GetTrisTuple(), Is.EqualTo(expected));
        }

        [Test, Description("Test checks if triangulation with data from GitHub Issue #30 passes.")]
        public void PCATransformationGithubIssue30Test()
        {
            using var positions = new NativeArray<float2>(new[]
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
            }, Allocator.Persistent);

            using var constraintEdges = new NativeArray<int>(new[]
            {
                97, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16,
                17, 17, 18, 18, 19, 19, 20, 20, 21, 21, 22, 22, 23, 23, 24, 24, 25, 25, 26, 26, 27, 27, 28, 28, 29, 29, 30, 30, 31, 31,
                32, 32, 33, 33, 34, 34, 35, 35, 36, 36, 37, 37, 38, 38, 39, 39, 40, 40, 41, 41, 42, 42, 43, 43, 44, 44, 45, 45, 46, 46,
                47, 47, 48, 48, 49, 49, 50, 50, 51, 51, 52, 52, 53, 53, 54, 54, 55, 55, 56, 56, 57, 57, 58, 58, 59, 59, 60, 60, 61, 61,
                62, 62, 63, 63, 64, 64, 65, 65, 66, 66, 67, 67, 68, 68, 69, 69, 70, 70, 71, 71, 72, 72, 73, 73, 74, 74, 75, 75, 76, 76,
                77, 77, 78, 78, 79, 79, 80, 80, 81, 81, 82, 82, 83, 83, 84, 84, 85, 85, 86, 86, 87, 87, 88, 88, 89, 89, 90, 90, 91, 91,
                92, 92, 93, 93, 94, 94, 95, 95, 96, 96, 97
            }, Allocator.Persistent);

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
                    ConstrainEdges = true,
                    RestoreBoundary = true,
                    Preprocessor = Triangulator.Preprocessor.PCA
                }
            };

            triangulator.Run();
        }

        [Test, Description("Test checks if triangulation with data from GitHub Issue #31 passes.")]
        public void PCATransformationGithubIssue31Test()
        {
            using var positions = new NativeArray<float2>(new[]
            {
                new float2(31.28938f, 37.67612f), new float2(31.79285f, 37.00624f), new float2(32.03879f, 36.60557f),
                new float2(32.29923f, 36.36939f), new float2(32.58526f, 36.42342f), new float2(32.876f, 36.53085f),
                new float2(33.42577f, 36.38619f), new float2(33.88485f, 35.21272f), new float2(34.62434f, 34.02968f),
                new float2(34.73527f, 33.69278f), new float2(34.86366f, 33.55389f), new float2(35.10379f, 33.08732f),
                new float2(35.35777f, 32.77784f), new float2(35.69171f, 32.50069f), new float2(35.84656f, 32.22465f),
                new float2(36.01643f, 32.11908f), new float2(36.17846f, 31.92439f), new float2(36.32175f, 31.51735f),
                new float2(36.49083f, 31.40269f), new float2(36.6428f, 31.09395f), new float2(36.98143f, 30.87008f),
                new float2(37.34995f, 30.98518f), new float2(37.65298f, 30.35742f), new float2(38.14125f, 29.79839f),
                new float2(38.30097f, 29.57764f), new float2(38.63807f, 29.33636f), new float2(38.79191f, 29.04884f),
                new float2(38.95393f, 28.85409f), new float2(39.28638f, 28.56006f), new float2(39.44593f, 28.33743f),
                new float2(39.59904f, 28.04167f), new float2(39.76351f, 27.87474f), new float2(40.23344f, 27.10765f),
                new float2(40.5593f, 26.7389f), new float2(40.64825f, 26.77288f), new float2(40.15792f, 28.3197f),
                new float2(40.47878f, 27.74801f), new float2(40.6029f, 27.9564f), new float2(40.46587f, 28.58052f),
                new float2(40.49842f, 29.02806f), new float2(40.85061f, 29.12524f), new float2(41.09647f, 28.99164f),
                new float2(41.32619f, 28.90017f), new float2(41.62305f, 29.14183f), new float2(41.9069f, 29.41749f),
                new float2(42.3455f, 29.289f), new float2(42.62182f, 29.07582f), new float2(42.94831f, 28.73164f),
                new float2(43.10726f, 28.825f), new float2(43.09475f, 29.11192f), new float2(43.03548f, 29.52093f),
                new float2(43.47666f, 28.87722f), new float2(43.68863f, 28.83212f), new float2(44.24405f, 30.16822f),
                new float2(44.15243f, 30.4401f), new float2(44.13583f, 30.68668f), new float2(43.97319f, 31.2235f),
                new float2(43.59188f, 32.07505f), new float2(43.55514f, 32.32842f), new float2(43.28082f, 32.9029f),
                new float2(43.19778f, 33.17189f), new float2(42.99816f, 33.4802f), new float2(42.90062f, 33.75408f),
                new float2(42.84206f, 34.01482f), new float2(42.60212f, 34.33672f), new float2(42.51172f, 34.60819f),
                new float2(42.49178f, 34.8559f), new float2(42.44653f, 35.11214f), new float2(42.40163f, 35.85024f),
                new float2(42.3139f, 36.1208f), new float2(42.2475f, 36.86615f), new float2(42.09885f, 37.39825f),
                new float2(41.93857f, 37.69329f), new float2(41.9395f, 38.41592f), new float2(41.74668f, 39.06109f),
                new float2(41.71388f, 39.41131f), new float2(41.4817f, 39.82877f), new float2(41.40123f, 40.19506f),
                new float2(41.83162f, 40.72823f), new float2(41.72264f, 41.10414f), new float2(41.74435f, 41.43597f),
                new float2(41.68886f, 41.79384f), new float2(41.49154f, 42.19954f), new float2(41.38077f, 42.57606f),
                new float2(41.07294f, 43.35818f), new float2(40.84647f, 43.77372f), new float2(40.94663f, 44.0791f),
                new float2(40.84524f, 44.45245f), new float2(39.73372f, 45.59874f), new float2(39.7615f, 45.89402f),
                new float2(39.86456f, 46.3304f), new float2(39.79302f, 46.43954f), new float2(39.95979f, 46.8737f),
                new float2(40.02057f, 47.10924f), new float2(39.68147f, 46.96014f), new float2(39.87912f, 47.57381f),
                new float2(39.88906f, 47.83566f), new float2(39.62033f, 47.85281f), new float2(39.28307f, 47.74151f),
                new float2(38.99869f, 47.72932f), new float2(38.72726f, 47.7414f), new float2(37.57701f, 47.14798f),
                new float2(37.24718f, 47.0506f), new float2(37.07909f, 47.25636f), new float2(36.70338f, 47.07301f),
                new float2(36.64832f, 47.4906f), new float2(36.43682f, 47.61499f), new float2(36.1853f, 47.66438f),
                new float2(35.82184f, 47.50399f), new float2(35.3458f, 47.65338f), new float2(35.14989f, 47.98354f),
                new float2(35.08235f, 48.16993f), new float2(34.95149f, 48.71061f), new float2(34.80748f, 49.13337f),
                new float2(35.23618f, 45.39426f), new float2(35.2403f, 45.17966f), new float2(34.40559f, 48.73275f),
                new float2(34.30258f, 48.75045f), new float2(34.44608f, 47.66825f), new float2(34.40862f, 47.59851f),
                new float2(34.21555f, 47.40396f), new float2(33.79879f, 47.39106f), new float2(33.84159f, 47.00494f),
                new float2(33.46966f, 46.95563f), new float2(33.17641f, 47.0181f), new float2(32.9339f, 47.03938f),
                new float2(32.56659f, 46.98632f), new float2(31.7903f, 47.26541f), new float2(31.56507f, 47.27264f),
                new float2(31.26646f, 47.33947f), new float2(31.65442f, 46.67304f), new float2(31.69027f, 46.29256f),
                new float2(32.24787f, 45.48835f), new float2(32.47871f, 44.59815f), new float2(32.53637f, 44.19995f),
                new float2(32.54522f, 43.84141f), new float2(32.44212f, 43.57378f), new float2(32.21579f, 43.47334f),
                new float2(31.94443f, 43.40946f), new float2(31.53552f, 43.17303f), new float2(30.83317f, 43.17491f),
                new float2(30.61916f, 43.06446f), new float2(30.34215f, 43.00517f), new float2(30.04128f, 42.681f),
                new float2(30.19507f, 41.98758f), new float2(29.75365f, 41.77756f), new float2(29.58416f, 41.34669f),
                new float2(29.70014f, 40.94119f), new float2(29.99722f, 40.84808f), new float2(30.21854f, 40.72022f),
                new float2(29.90438f, 40.11747f), new float2(29.82732f, 39.62344f), new float2(30.09426f, 39.28717f),
                new float2(30.51098f, 39.01957f), new float2(30.38978f, 38.73465f), new float2(30.38398f, 38.04395f),
                new float2(30.40719f, 37.85884f), new float2(30.80989f, 37.99457f), new float2(31.34938f, 38.09515f)
            }, Allocator.Persistent);

            using var constraintEdges = new NativeArray<int>(new[]
            {
                0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14,
                15, 15, 16, 16, 17, 17, 18, 18, 19, 19, 20, 20, 21, 21, 22, 22, 23, 23, 24, 24, 25, 25, 26, 26,
                27, 27, 28, 28, 29, 29, 30, 30, 31, 31, 32, 32, 33, 33, 34, 34, 35, 35, 36, 36, 37, 37, 38, 38,
                39, 39, 40, 40, 41, 41, 42, 42, 43, 43, 44, 44, 45, 45, 46, 46, 47, 47, 48, 48, 49, 49, 50, 50,
                51, 51, 52, 52, 53, 53, 54, 54, 55, 55, 56, 56, 57, 57, 58, 58, 59, 59, 60, 60, 61, 61, 62, 62,
                63, 63, 64, 64, 65, 65, 66, 66, 67, 67, 68, 68, 69, 69, 70, 70, 71, 71, 72, 72, 73, 73, 74, 74,
                75, 75, 76, 76, 77, 77, 78, 78, 79, 79, 80, 80, 81, 81, 82, 82, 83, 83, 84, 84, 85, 85, 86, 86,
                87, 87, 88, 88, 89, 89, 90, 90, 91, 91, 92, 92, 93, 93, 94, 94, 95, 95, 96, 96, 97, 97, 98, 98,
                99, 99, 100, 100, 101, 101, 102, 102, 103, 103, 104, 104, 105, 105, 106, 106, 107, 107, 108, 108,
                109, 109, 110, 110, 111, 111, 112, 112, 113, 113, 114, 114, 115, 115, 116, 116, 117, 117, 118, 118,
                119, 119, 120, 120, 121, 121, 122, 122, 123, 123, 124, 124, 125, 125, 126, 126, 127, 127, 128, 128,
                129, 129, 130, 130, 131, 131, 132, 132, 133, 133, 134, 134, 135, 135, 136, 136, 137, 137, 138, 138,
                139, 139, 140, 140, 141, 141, 142, 142, 143, 143, 144, 144, 145, 145, 146, 146, 147, 147, 148, 148,
                149, 149, 150, 150, 151, 151, 152, 152, 153, 153, 154, 154, 155, 155, 156, 156, 157, 157, 158, 158, 0
            }, Allocator.Persistent);

            using var triangulator = new Triangulator(capacity: 1024, Allocator.Persistent)
            {
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges
                },

                Settings =
                {
                    MinimumAngle = math.radians(33),
                    MinimumArea = 0.5f,
                    MaximumArea = 4.0f,

                    ValidateInput = true,
                    RefineMesh = true,
                    ConstrainEdges = true,
                    RestoreBoundary = true,
                    Preprocessor = Triangulator.Preprocessor.PCA
                }
            };

            triangulator.Run();
        }

        private static readonly TestCaseData[] refineMeshBenchmarkTestData =
        {
            new((area: 10.000f, N: 100)),
            new((area: 05.000f, N: 100)),
            new((area: 01.000f, N: 100)),
            new((area: 0.5000f, N: 100)),
            new((area: 0.1000f, N: 100)),
            new((area: 0.0500f, N: 100)),
            new((area: 0.0100f, N: 100)),
            new((area: 0.0050f, N: 100)),
            new((area: 0.0010f, N: 100)),
            new((area: 0.0005f, N: 010)),
            new((area: 0.0003f, N: 010)),
            new((area: 0.0002f, N: 005)),
        };

        [Test, TestCaseSource(nameof(refineMeshBenchmarkTestData)), Explicit]
        public void RefineMeshBenchmarkTest((float area, int N) input)
        {
            var (area, N) = input;
            var debuggerInitialValue = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled;
            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = false;

            var stopwatch = Stopwatch.StartNew();
            using var points = new NativeArray<float2>(new[]
            {
                math.float2(-1, -1),
                math.float2(+1, -1),
                math.float2(+1, +1),
                math.float2(-1, +1),
            }, Allocator.Persistent);
            using var triangulator = new Triangulator(capacity: 64 * 1024, Allocator.Persistent)
            {
                Input = { Positions = points },
                Settings = {
                    RefineMesh = true,
                    ConstrainEdges = false,
                    RestoreBoundary = false,
                    MinimumArea = area,
                    MaximumArea = area,
                },
            };

            var dependencies = default(JobHandle);
            for (int i = 0; i < N; i++) dependencies = triangulator.Schedule(dependencies);
            dependencies.Complete();
            stopwatch.Stop();
            UnityEngine.Debug.Log($"{triangulator.Output.Triangles.Length} {stopwatch.Elapsed.TotalMilliseconds / N}");

            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = debuggerInitialValue;
        }
    }
}
