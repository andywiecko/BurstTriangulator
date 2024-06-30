using NUnit.Framework;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.TestTools;

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    public class TriangulatorDouble2EditorTests
    {
        [Test]
        public void DelaunayTriangulationWithoutRefinementTest()
        {
            ///  3 ------- 2
            ///  |      . `|
            ///  |    *    |
            ///  |. `      |
            ///  0 ------- 1
            using var positions = new NativeArray<double2>(new[]
            {
                math.double2(0, 0),
                math.double2(1, 0),
                math.double2(1, 1),
                math.double2(0, 1)
            }, Allocator.Persistent);

            using var triangulator = new Triangulator<double2>(capacity: 1024, Allocator.Persistent)
            {
                Settings = { RefineMesh = false },
                Input = { Positions = positions },
            };

            triangulator.Run();

            Assert.That(triangulator.GetTrisTuple(), Is.EqualTo(new[] { (0, 2, 1), (0, 3, 2) }));
        }

        private static readonly TestCaseData[] validateInputPositionsTestData = new[]
        {
            new TestCaseData(
                new[]
                {
                    math.double2(0, 0),
                    math.double2(0, 1)
                }
            ) { TestName = "Test Case 1 (points count less than 3)" },
            new TestCaseData(
                new[]
                {
                    math.double2(0, 0),
                    math.double2(0, 0),
                    math.double2(1, 1),
                    math.double2(0, 1)
                }
            ) { TestName = "Test Case 2 (duplicated position)" },
            new TestCaseData(
                new[]
                {
                    math.double2(0, 0),
                    math.double2(1, float.NaN),
                    math.double2(1, 1),
                    math.double2(0, 1)
                }
            ) { TestName = "Test Case 3 (point with NaN)" },
            new TestCaseData(
                new[]
                {
                    math.double2(0, 0),
                    math.double2(1, float.PositiveInfinity),
                    math.double2(1, 1),
                    math.double2(0, 1)
                }
            ) { TestName = "Test Case 4 (point with +inf)" },
            new TestCaseData(
                new[]
                {
                    math.double2(0, 0),
                    math.double2(1, float.NegativeInfinity),
                    math.double2(1, 1),
                    math.double2(0, 1)
                }
            ) { TestName = "Test Case 4 (point with -inf)" },
        };

        [Test, TestCaseSource(nameof(validateInputPositionsTestData))]
        public void ValidateInputPositionsTest(double2[] managedPositions)
        {
            using var positions = new NativeArray<double2>(managedPositions, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(capacity: 1024, Allocator.Persistent)
            {
                Settings = { ValidateInput = true, Verbose = true },
                Input = { Positions = positions },
            };

            LogAssert.Expect(UnityEngine.LogType.Error, new Regex(".*"));
            triangulator.Run();

            Assert.That(triangulator.Output.Status.Value, Is.EqualTo(Status.ERR));
        }

        private static readonly TestCaseData[] validateInputPositionsNoVerboseTestData = validateInputPositionsTestData
            .Select(i => new TestCaseData(i.Arguments) { TestName = i.TestName[..^1] + ", no verbose" }).ToArray();

        [Test, TestCaseSource(nameof(validateInputPositionsNoVerboseTestData))]
        public void ValidateInputPositionsNoVerboseTest(double2[] managedPositions)
        {
            using var positions = new NativeArray<double2>(managedPositions, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(capacity: 1024, Allocator.Persistent)
            {
                Settings = { ValidateInput = true, Verbose = false },
                Input = { Positions = positions },
            };

            triangulator.Run();

            Assert.That(triangulator.Output.Status.Value, Is.EqualTo(Status.ERR));
        }

        [Test]
        public void DelaunayTriangulationWithRefinementTest()
        {
            ///  3 ------- 2
            ///  |         |
            ///  |         |
            ///  |         |
            ///  0 ------- 1
            using var positions = new NativeArray<double2>(new[]
            {
                math.double2(0, 0),
                math.double2(1, 0),
                math.double2(1, 1),
                math.double2(0, 1)
            }, Allocator.Persistent);

            using var triangulator = new Triangulator<double2>(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
                    RefineMesh = true,
                    RefinementThresholds = { Area = 0.3f, Angle = math.radians(20f) }
                },
                Input = { Positions = positions },
            };

            triangulator.Run();

            var expectedPositions = new[]
            {
                math.double2(0, 0),
                math.double2(1, 0),
                math.double2(1, 1),
                math.double2(0, 1),
                math.double2(1, 0.5f),
                math.double2(0, 0.5f),
            };
            var expectedTriangles = new[]
            {
                (5, 1, 0),
                (5, 2, 4),
                (5, 3, 2),
                (5, 4, 1),
            };
            Assert.That(triangulator.Output.Positions.AsArray(), Is.EqualTo(expectedPositions));
            Assert.That(triangulator.GetTrisTuple(), Is.EqualTo(expectedTriangles));
        }

        private static readonly TestCaseData[] edgeConstraintsTestData = new[]
        {
            new TestCaseData(
                new[]
                {
                    math.double2(0, 0),
                    math.double2(1, 0),
                    math.double2(1, 1),
                    math.double2(0, 1),
                }, new int[]{ }
            )
            {
                TestName = "Test case 0",
                ExpectedResult = new[]
                {
                    (0, 2, 1),
                    (0, 3, 2),
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
                    math.double2(0, 0),
                    math.double2(1, 0),
                    math.double2(2, 0),
                    math.double2(2, 1),
                    math.double2(2, 2),
                    math.double2(1, 2),
                    math.double2(0, 2),
                    math.double2(0, 1),
                }, new[] { 1, 5 })
            {
                TestName = "Test case 1",
                ExpectedResult = new[]
                {
                    (1, 0, 7),
                    (3, 2, 1),
                    (5, 3, 1),
                    (5, 4, 3),
                    (7, 5, 1),
                    (7, 6, 5),
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
                    math.double2(0, 0),
                    math.double2(1, 0),
                    math.double2(2, 0),
                    math.double2(2, 1),
                    math.double2(2, 2),
                    math.double2(1, 2),
                    math.double2(0, 2),
                    math.double2(0, 1),
                }, new[] { 1, 5, 1, 4 })
            {
                TestName = "Test case 2",
                ExpectedResult = new[]
                {
                    (1, 0, 7),
                    (3, 2, 1),
                    (4, 3, 1),
                    (5, 4, 1),
                    (7, 5, 1),
                    (7, 6, 5),
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
                    math.double2(0, 0),
                    math.double2(1, 0),
                    math.double2(2, 0),
                    math.double2(3, 0),
                    math.double2(3, 1),
                    math.double2(3, 2),
                    math.double2(3, 3),
                    math.double2(2, 3),
                    math.double2(1, 3),
                    math.double2(0, 3),
                    math.double2(0, 2),
                    math.double2(0, 1),
                }, new[] { 3, 9, 8, 5 })
            {
                TestName = "Test case 3",
                ExpectedResult = new[]
                {
                    (1, 0, 11),
                    (4, 3, 8),
                    (7, 6, 5),
                    (8, 5, 4),
                    (8, 7, 5),
                    (9, 8, 3),
                    (10, 3, 2),
                    (10, 9, 3),
                    (11, 2, 1),
                    (11, 10, 2),
                }
            },
            // 4   5   6   7
            // *   *   *  ,*
            //         ..;
            //      ..;
            //   ..;
            //  ;
            // *   *   *   *
            // 0   1   2   3
            new(new[]
            {
                math.double2(0, 0),
                math.double2(1, 0),
                math.double2(2, 0),
                math.double2(3, 0),
                math.double2(0, 3),
                math.double2(1, 3),
                math.double2(2, 3),
                math.double2(3, 3),
            },
            new[] { 0, 7 }
            )
            {
                TestName = "Test case 4",
                ExpectedResult = new[]
                {
                    (1, 0, 7),
                    (4, 5, 0),
                    (5, 6, 0),
                    (6, 7, 0),
                    (7, 2, 1),
                    (7, 3, 2),
                }
            },
            //    8   9   10  11
            //    *   *   *   *
            //             ..;
            //  5 *      ..;  * 7
            //         ..;
            //  4 *  ..;      * 6
            //     ..;
            //    *   *   *   *
            //    0   1   2   3
            new(new[]
            {
                math.double2(0, 0),
                math.double2(1, 0),
                math.double2(2, 0),
                math.double2(3, 0),
                math.double2(0, 1),
                math.double2(0, 2),
                math.double2(3, 1),
                math.double2(3, 2),
                math.double2(0, 3),
                math.double2(1, 3),
                math.double2(2, 3),
                math.double2(3, 3),
            },
            new[]{ 0, 11 }
            )
            {
                TestName = "Test case 5",
                ExpectedResult = new[]
                {
                    (1, 0, 11),
                    (4, 11, 0),
                    (5, 9, 4),
                    (6, 2, 1),
                    (6, 3, 2),
                    (7, 6, 1),
                    (8, 9, 5),
                    (9, 10, 4),
                    (10, 11, 4),
                    (11, 7, 1),
                }
            }
        };

        [Test, TestCaseSource(nameof(edgeConstraintsTestData))]
        public (int, int, int)[] ConstraintDelaunayTriangulationTest(double2[] managedPositions, int[] constraints)
        {
            using var positions = new NativeArray<double2>(managedPositions, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
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
                    math.double2(0, 0),
                    math.double2(1, 0),
                    math.double2(1, 1),
                    math.double2(0, 1),
                },
                new[]{ 0, 2, 1, 3 }
            ) { TestName = "Test Case 1 (edge-edge intersection)" },
            new TestCaseData(
                new[]
                {
                    math.double2(0, 0),
                    math.double2(1, 0),
                    math.double2(1, 1),
                    math.double2(0, 1),
                },
                new[]{ 0, 2, 0, 2 }
            ) { TestName = "Test Case 2 (duplicated edge)" },
            new TestCaseData(
                new[]
                {
                    math.double2(0, 0),
                    math.double2(1, 0),
                    math.double2(1, 1),
                    math.double2(0, 1),
                },
                new[]{ 0, 0 }
            ) { TestName = "Test Case 3 (zero-length edge)" },
            new TestCaseData(
                new[]
                {
                    math.double2(0, 0),
                    math.double2(0.5f, 0),
                    math.double2(1, 0),
                    math.double2(1, 1),
                    math.double2(0, 1),
                },
                new[]{ 0, 2 }
            ) { TestName = "Test Case 4 (edge collinear with other point)" },
            new TestCaseData(
                new[]
                {
                    math.double2(0, 0),
                    math.double2(1, 0),
                    math.double2(1, 1),
                    math.double2(0, 1),
                },
                new[]{ 0, 5, 1 }
            ) { TestName = "Test Case 5 (odd number of elements in constraints buffer)" },
            new TestCaseData(
                new[]
                {
                    math.double2(0, 0),
                    math.double2(1, 0),
                    math.double2(1, 1),
                    math.double2(0, 1),
                },
                new[]{ -1, 1, 1, 1 }
            ) { TestName = "Test Case 6a (constraint out of positions range)" },
            new TestCaseData(
                new[]
                {
                    math.double2(0, 0),
                    math.double2(1, 0),
                    math.double2(1, 1),
                    math.double2(0, 1),
                },
                new[]{ 1, -1, 1, 1 }
            ) { TestName = "Test Case 6b (constraint out of positions range)" },
            new TestCaseData(
                new[]
                {
                    math.double2(0, 0),
                    math.double2(1, 0),
                    math.double2(1, 1),
                    math.double2(0, 1),
                },
                new[]{ 5, 1, 1, 1 }
            ) { TestName = "Test Case 6c (constraint out of positions range)" },
            new TestCaseData(
                new[]
                {
                    math.double2(0, 0),
                    math.double2(1, 0),
                    math.double2(1, 1),
                    math.double2(0, 1),
                },
                new[]{ 1, 5, 1, 1 }
            ) { TestName = "Test Case 6d (constraint out of positions range)" },
        };

        [Test, TestCaseSource(nameof(validateConstraintDelaunayTriangulationTestData))]
        public void ValidateConstraintDelaunayTriangulationTest(double2[] managedPositions, int[] constraints)
        {
            using var positions = new NativeArray<double2>(managedPositions, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = true,
                    Verbose = true,
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges,
                }
            };

            LogAssert.Expect(UnityEngine.LogType.Error, new Regex(".*"));
            triangulator.Run();

            Assert.That(triangulator.Output.Status.Value, Is.EqualTo(Status.ERR));
        }

        private static readonly TestCaseData[] validateConstraintDelaunayTriangulationNoVerboseTestData = validateConstraintDelaunayTriangulationTestData
            .Select(i => new TestCaseData(i.Arguments) { TestName = i.TestName[..^1] + ", no verbose" }).ToArray();

        [Test, TestCaseSource(nameof(validateConstraintDelaunayTriangulationNoVerboseTestData))]
        public void ValidateConstraintDelaunayTriangulationNoVerboseTest(double2[] managedPositions, int[] constraints)
        {
            using var positions = new NativeArray<double2>(managedPositions, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = true,
                    Verbose = false,
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges,
                }
            };

            triangulator.Run();

            Assert.That(triangulator.Output.Status.Value, Is.EqualTo(Status.ERR));
        }

        private static readonly TestCaseData[] constraintDelaunayTriangulationWithRefinementTestData =
        {
            //  3 -------- 2
            //  | ' .      |
            //  |    ' .   |
            //  |       ' .|
            //  0 -------- 1
            new TestCaseData((
                new []
                {
                    math.double2(0, 0),
                    math.double2(1, 0),
                    math.double2(1, 1),
                    math.double2(0, 1)
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
                    math.double2(0.5f, 0.5f),
                    math.double2(1f, 0.5f),
                    math.double2(0.5f, 0f),
                    math.double2(0f, 0.5f),
                    math.double2(0.8189806f, 0.8189806f),
                    math.double2(0.1810194f, 0.1810194f),
                    math.double2(0.5f, 1f),
                    math.double2(0.256f, 0f),
                    math.double2(0f, 0.256f),
                    math.double2(0.744f, 1f),
                }
            ))
            {
                TestName = "Test case 1 (square)",
                ExpectedResult = new []
                {
                    (6, 4, 5),
                    (6, 5, 1),
                    (8, 2, 5),
                    (8, 5, 4),
                    (9, 4, 6),
                    (9, 7, 4),
                    (10, 4, 7),
                    (10, 7, 3),
                    (10, 8, 4),
                    (11, 0, 9),
                    (11, 9, 6),
                    (12, 7, 9),
                    (12, 9, 0),
                    (13, 2, 8),
                    (13, 8, 10),
                }
            },

            //  5 -------- 4 -------- 3
            //  |       . '|      . ' |
            //  |    . '   |   . '    |
            //  |. '      .|. '       |
            //  0 -------- 1 -------- 2
            new TestCaseData((
                new []
                {
                    math.double2(0, 0),
                    math.double2(1, 0),
                    math.double2(2, 0),
                    math.double2(2, 1),
                    math.double2(1, 1),
                    math.double2(0, 1),
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
                    math.double2(1f, 0.5f),
                    math.double2(0.4579467f, 0.2289734f),
                    math.double2(1.542053f, 0.7710266f),
                    math.double2(0.5f, 0f),
                    math.double2(1.5f, 1f),
                }
            ))
            {
                TestName = "Test case 2 (rectangle)",
                ExpectedResult = new []
                {
                    (7, 0, 5),
                    (7, 4, 6),
                    (7, 5, 4),
                    (7, 6, 1),
                    (8, 1, 6),
                    (8, 2, 1),
                    (8, 3, 2),
                    (8, 6, 4),
                    (9, 0, 7),
                    (9, 7, 1),
                    (10, 3, 8),
                    (10, 8, 4),
                }
            },
            new((
                managedPositions: new[]
                {
                    math.double2(0, 0),
                    math.double2(1, 0),
                    math.double2(1, 1),
                    math.double2(0.75f, 0.75f),
                    math.double2(0, 1),
                },
                constraints: new[]
                {
                    0, 1,
                    1, 2,
                    2, 3,
                    3, 4,
                    4, 0,
                },
                insertedPoints: new[]
                {
                    math.double2(1f, 0.5f),
                    math.double2(1f, 0.744f),
                }
            )){
                TestName = "Test case 3 (strange box)",
                ExpectedResult = new[]
                {
                    (0, 4, 3),
                    (5, 0, 3),
                    (5, 1, 0),
                    (6, 3, 2),
                    (6, 5, 3),
                }
            },
        };

        [Test, TestCaseSource(nameof(constraintDelaunayTriangulationWithRefinementTestData))]
        public (int, int, int)[] ConstraintDelaunayTriangulationWithRefinementTest((double2[] managedPositions, int[] constraints, double2[] insertedPoints) input)
        {
            using var positions = new NativeArray<double2>(input.managedPositions, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(input.constraints, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
                    RefineMesh = true,
                    RestoreBoundary = true,
                    RefinementThresholds = { Area = 10.3f, Angle = 0 },
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges,
                }
            };

            triangulator.Run();

            Assert.That(triangulator.Output.Positions.AsArray(), Is.EqualTo(input.managedPositions.Union(input.insertedPoints)).Using(Double2Comparer.Instance));

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
                math.double2(0, 0),
                math.double2(3, 0),
                math.double2(3, 2),
                math.double2(2, 2),
                math.double2(2, 1),
                math.double2(1, 1),
                math.double2(1, 2),
                math.double2(0, 2),
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

            using var positions = new NativeArray<double2>(managedPositions, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
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
                (1, 5, 4),
                (2, 1, 4),
                (4, 3, 2),
                (5, 0, 7),
                (5, 7, 6),
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
                math.double2(0, 0),
                math.double2(1, 0),
                math.double2(1, 1),
                math.double2(0.5f, 0.25f),
                math.double2(0, 1),
            };

            var constraints = new[]
            {
                0, 1,
                1, 2,
                2, 3,
                3, 4,
                4, 0
            };

            using var positions = new NativeArray<double2>(managedPositions, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
                    RefineMesh = true,
                    RestoreBoundary = true,
                    RefinementThresholds = { Area = 0.25f }
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges,
                }
            };

            triangulator.Run();

            double2[] expectedPositions = managedPositions.Union(
                new[] { math.double2(0.5f, 0f) }
            ).ToArray();
            var expectedTriangles = new[]
            {
                (2, 1, 3),
                (3, 0, 4),
                (5, 0, 3),
                (5, 3, 1),
            };
            Assert.That(triangulator.Output.Positions.AsArray(), Is.EqualTo(expectedPositions));
            Assert.That(triangulator.GetTrisTuple(), Is.EqualTo(expectedTriangles));
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
                    math.double2(0, 0),
                    math.double2(3, 0),
                    math.double2(3, 3),
                    math.double2(0, 3),

                    math.double2(1, 1),
                    math.double2(2, 1),
                    math.double2(2, 2),
                    math.double2(1, 2),
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
                new[] { (double2)1.5f }
            )
            {
                TestName = "Test Case 1",
                ExpectedResult = new[]
                {
                    (0, 3, 7),
                    (4, 0, 7),
                    (5, 0, 4),
                    (5, 1, 0),
                    (6, 1, 5),
                    (6, 2, 1),
                    (7, 2, 6),
                    (7, 3, 2),
                }
            },

            new TestCaseData(
                new[]
                {
                    math.double2(0, 0),
                    math.double2(3, 0),
                    math.double2(3, 3),
                    math.double2(0, 3),

                    math.double2(1, 1),
                    math.double2(2, 1),
                    math.double2(2, 2),
                    math.double2(1, 2),
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
                new[] { (double2)1.5f, (double2)1.5f }
            )
            {
                TestName = "Test Case 2 (duplicated hole)",
                ExpectedResult = new[]
                {
                    (0, 3, 7),
                    (4, 0, 7),
                    (5, 0, 4),
                    (5, 1, 0),
                    (6, 1, 5),
                    (6, 2, 1),
                    (7, 2, 6),
                    (7, 3, 2),
                }
            },

            new TestCaseData(
                new[]
                {
                    math.double2(0, 0),
                    math.double2(3, 0),
                    math.double2(3, 3),
                    math.double2(0, 3),

                    math.double2(1, 1),
                    math.double2(2, 1),
                    math.double2(2, 2),
                    math.double2(1, 2),
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
                new[] { (double2)1000 }
            )
            {
                TestName = "Test Case 3 (hole out of range)",
                ExpectedResult = new[]
                {
                    (0, 3, 7),
                    (4, 0, 7),
                    (4, 6, 5),
                    (4, 7, 6),
                    (5, 0, 4),
                    (5, 1, 0),
                    (6, 1, 5),
                    (6, 2, 1),
                    (7, 2, 6),
                    (7, 3, 2),
                }
            },

            new TestCaseData(
                new[]
                {
                    math.double2(0, 0),
                    math.double2(3, 0),
                    math.double2(3, 3),
                    math.double2(0, 3),

                    math.double2(1, 1),
                    math.double2(2, 1),
                    math.double2(2, 2),
                    math.double2(1, 2),
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
                new[] { math.double2(1.5f + 0.25f, 1.5f), math.double2(1.5f - 0.25f, 1.5f) }
            )
            {
                TestName = "Test Case 4 (hole seeds in the same area)",
                ExpectedResult = new[]
                {
                    (0, 3, 7),
                    (4, 0, 7),
                    (5, 0, 4),
                    (5, 1, 0),
                    (6, 1, 5),
                    (6, 2, 1),
                    (7, 2, 6),
                    (7, 3, 2),
                }
            },
        };

        [Test, TestCaseSource(nameof(triangulationWithHolesWithoutRefinementTestData))]
        public (int, int, int)[] TriangulationWithHolesWithoutRefinementTest(double2[] managedPositions, int[] constraints, double2[] holeSeeds)
        {
            using var positions = new NativeArray<double2>(managedPositions, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var holes = new NativeArray<double2>(holeSeeds, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
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
                math.double2(0, 0),
                math.double2(3, 0),
                math.double2(3, 3),
                math.double2(0, 3),

                math.double2(1, 1),
                math.double2(2, 1),
                math.double2(2, 2),
                math.double2(1, 2),
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

            using var positions = new NativeArray<double2>(managedPositions, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var holes = new NativeArray<double2>(new[] { (double2)1.5f }, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
                    RefineMesh = true,
                    RestoreBoundary = true,
                    RefinementThresholds = { Area = 1.0f },
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges,
                    HoleSeeds = holes,
                }
            };

            triangulator.Run();

            var expectedPositions = managedPositions.Union(new double2[]
            {
                math.double2(1.5f, 0f),
                math.double2(3f, 1.5f),
                math.double2(1.5f, 3f),
                math.double2(0f, 1.5f),
            });
            var expectedTriangles = new[]
            {
                (8, 0, 4),
                (8, 4, 5),
                (8, 5, 1),
                (9, 1, 5),
                (9, 5, 6),
                (9, 6, 2),
                (10, 3, 7),
                (10, 4, 0),
                (10, 7, 4),
                (11, 2, 6),
                (11, 6, 7),
                (11, 7, 3),
            };

            Assert.That(triangulator.Output.Positions.AsArray(), Is.EquivalentTo(expectedPositions));
            Assert.That(triangulator.GetTrisTuple(), Is.EquivalentTo(expectedTriangles));
        }

        [BurstCompile]
        private struct DeferredArraySupportInputJob : IJob
        {
            public NativeList<double2> positions;
            public NativeList<int> constraints;
            public NativeList<double2> holes;

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
                positions.Add(math.double2(0, 0));
                positions.Add(math.double2(3, 0));
                positions.Add(math.double2(3, 3));
                positions.Add(math.double2(0, 3));
                positions.Add(math.double2(1, 1));
                positions.Add(math.double2(2, 1));
                positions.Add(math.double2(2, 2));
                positions.Add(math.double2(1, 2));

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
            using var positions = new NativeList<double2>(64, Allocator.Persistent);
            using var constraints = new NativeList<int>(64, Allocator.Persistent);
            using var holes = new NativeList<double2>(64, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(64, Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = true,
                    RefineMesh = true,
                    RestoreBoundary = true,
                    RefinementThresholds = { Area = 1.0f },
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

            double2[] expectedPositions =
            {
                math.double2(0f, 0f),
                math.double2(3f, 0f),
                math.double2(3f, 3f),
                math.double2(0f, 3f),
                math.double2(1f, 1f),
                math.double2(2f, 1f),
                math.double2(2f, 2f),
                math.double2(1f, 2f),
                math.double2(1.5f, 0f),
                math.double2(3f, 1.5f),
                math.double2(1.5f, 3f),
                math.double2(0f, 1.5f),
            };
            var expectedTriangles = new[]
            {
                (8, 0, 4),
                (8, 4, 5),
                (8, 5, 1),
                (9, 1, 5),
                (9, 5, 6),
                (9, 6, 2),
                (10, 3, 7),
                (10, 4, 0),
                (10, 7, 4),
                (11, 2, 6),
                (11, 6, 7),
                (11, 7, 3),
            };

            Assert.That(triangulator.Output.Positions.AsArray(), Is.EquivalentTo(expectedPositions));
            Assert.That(triangulator.GetTrisTuple(), Is.EquivalentTo(expectedTriangles));
        }

        [Test]
        public void LocalTransformationTest()
        {
            double2[] points =
            {
                0.01f * math.double2(-1, -1) + (double2)99999f,
                0.01f * math.double2(+1, -1) + (double2)99999f,
                0.01f * math.double2(+1, +1) + (double2)99999f,
                0.01f * math.double2(-1, +1) + (double2)99999f,
            };

            using var positions = new NativeArray<double2>(points, Allocator.Persistent);
            using var constraints = new NativeArray<int>(new[] { 1, 3 }, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(64, Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = true,
                    RefineMesh = false,
                    RestoreBoundary = false,
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraints,
                }
            };

            triangulator.Settings.Preprocessor = Preprocessor.None;
            triangulator.Run();
            var nonLocalTriangles = triangulator.GetTrisTuple();

            triangulator.Settings.Preprocessor = Preprocessor.COM;
            triangulator.Run();
            var localTriangles = triangulator.GetTrisTuple();

            Assert.That(nonLocalTriangles, Has.Length.LessThanOrEqualTo(2));
            Assert.That(localTriangles, Is.EqualTo(new[] { (0, 3, 1), (3, 2, 1) }));
        }

        [Test, Ignore("This test should be redesigned. It will be done when during refinement quality improvement refactor.")]
        public void LocalTransformationWithRefinementTest()
        {
            var n = 20;
            var managedPositions = Enumerable.Range(0, n)
                .Select(i => math.double2(
                 x: math.cos(2 * math.PI * i / n),
                 y: math.sin(2 * math.PI * i / n))).ToArray();
            managedPositions = new double2[] { 0 }.Concat(managedPositions).ToArray();
            managedPositions = managedPositions.Select(x => 0.1f * x + 5f).ToArray();

            using var positions = new NativeArray<double2>(managedPositions, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(1024, Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = true,
                    RefineMesh = true,
                    RestoreBoundary = false,
                    RefinementThresholds = { Area = 0.0005f },
                },
                Input =
                {
                    Positions = positions,
                }
            };

            triangulator.Settings.Preprocessor = Preprocessor.None;
            triangulator.Run();
            var nonLocalTriangles = triangulator.GetTrisTuple();

            triangulator.Settings.Preprocessor = Preprocessor.COM;
            triangulator.Run();
            var localTriangles = triangulator.GetTrisTuple();

            var ratio = localTriangles.Intersect(nonLocalTriangles).Count() / (float)localTriangles.Length;
            Assert.That(ratio, Is.GreaterThan(0.80), message: "Only few triangles may be flipped.");
            Assert.That(localTriangles, Has.Length.EqualTo(nonLocalTriangles.Length));
        }

        [Test]
        public void LocalTransformationWithHolesTest()
        {
            var n = 12;
            var innerCircle = Enumerable
                .Range(0, n)
                .Select(i => math.double2(
                    x: 0.75f * math.cos(2 * math.PI * i / n),
                    y: 0.75f * math.sin(2 * math.PI * i / n)))
                .ToArray();

            var outerCircle = Enumerable
                .Range(0, n)
                .Select(i => math.double2(
                    x: 1.5f * math.cos(2 * math.PI * (i + 0.5f) / n),
                    y: 1.5f * math.sin(2 * math.PI * (i + 0.5f) / n)))
                .ToArray();

            var managedPositions = new double2[] { 0 }.Concat(outerCircle).Concat(innerCircle).ToArray();
            managedPositions = managedPositions.Select(x => 0.1f * x + 5f).ToArray();

            var constraints = Enumerable
                .Range(1, n - 1)
                .SelectMany(i => new[] { i, i + 1 })
                .Concat(new[] { n, 1 })
                .Concat(Enumerable.Range(n + 1, n - 1).SelectMany(i => new[] { i, i + 1 }))
                .Concat(new[] { 2 * n, n + 1 })
                .ToArray();

            using var holes = new NativeArray<double2>(new double2[] { 5 }, Allocator.Persistent);
            using var edges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var positions = new NativeArray<double2>(managedPositions, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(1024, Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = true,
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

            triangulator.Settings.Preprocessor = Preprocessor.None;
            triangulator.Run();
            var nonLocalTriangles = triangulator.GetTrisTuple();

            triangulator.Settings.Preprocessor = Preprocessor.COM;
            triangulator.Run();
            var localTriangles = triangulator.GetTrisTuple();

            Assert.That(TestUtils.SortTrianglesIds(localTriangles), Is.EquivalentTo(TestUtils.SortTrianglesIds(nonLocalTriangles)));
            Assert.That(localTriangles, Has.Length.EqualTo(24));
        }

        // Input for this test is grabbed from issue #30 from @mduvergey user.
        // https://github.com/andywiecko/BurstTriangulator/issues/30
        // This test tests editor hanging problem reported in issue #30 and #31.
        //
        // UPDATE: Thanks to the recent fix, this input will no longer cause the algorithm to hang,
        //         unless "max iters" are intentionally reduced.
        private static readonly (double2[] points, int[] constraints) sloanMaxItersData = (
            new[]
            {
                new double2(14225.59f, -2335.27f), new double2(13380.24f, -2344.72f), new double2(13197.35f, -2119.65f),
                new double2(11750.51f, -2122.18f), new double2(11670.1f, -2186.25f), new double2(11424.88f, -2178.53f),
                new double2(11193.54f, -2025.36f), new double2(11159.36f, -1812.22f), new double2(10956.29f, -1731.62f),
                new double2(10949.03f, -1524.29f), new double2(10727.71f, -1379.53f), new double2(10532.48f, -1145.83f),
                new double2(10525.18f, -906.69f), new double2(10410.57f, -750.73f), new double2(10629.48f, -657.33f),
                new double2(10622f, -625.7f), new double2(10467.05f, -552.15f), new double2(10415.75f, -423.21f),
                new double2(10037.01f, -427.11f), new double2(9997.4f, -487.33f), new double2(9788.02f, -539.44f),
                new double2(9130.03f, -533.95f), new double2(8905.69f, -490.95f), new double2(8842.1f, -396.11f),
                new double2(8410.81f, -407.12f), new double2(8211.88f, -583.32f), new double2(7985.37f, -588.47f),
                new double2(7880.46f, -574.94f), new double2(7200.87f, -574.14f), new double2(6664.29f, -637.89f),
                new double2(6351.84f, -483.61f), new double2(6324.37f, -143.48f), new double2(6093.94f, -152.8f),
                new double2(5743.03f, 213.65f), new double2(5725.63f, 624.21f), new double2(5562.64f, 815.17f),
                new double2(5564.65f, 1145.66f), new double2(4846.4f, 1325.89f), new double2(4362.98f, 1327.97f),
                new double2(5265.89f, 267.31f), new double2(5266.32f, -791.39f), new double2(3806f, -817.38f),
                new double2(3385.84f, -501.25f), new double2(3374.35f, -372.48f), new double2(3555.98f, -321.11f),
                new double2(3549.9f, -272.35f), new double2(3356.27f, -221.45f), new double2(3352.42f, 13.16f),
                new double2(1371.39f, 5.41f), new double2(1362.47f, -191.23f), new double2(1188.9f, -235.72f),
                new double2(1180.86f, -709.59f), new double2(132.26f, -720.07f), new double2(1.94f, -788.66f),
                new double2(-1240.12f, -779.03f), new double2(-1352.26f, -973.64f), new double2(-1665.17f, -973.84f),
                new double2(-1811.91f, -932.75f), new double2(-1919.98f, -772.61f), new double2(-2623.09f, -782.31f),
                new double2(-3030.54f, -634.38f), new double2(-3045.53f, -52.71f), new double2(-3969.83f, -61.28f),
                new double2(-6676.96f, 102.16f), new double2(-7209.27f, 100.12f), new double2(-7729.39f, 178.02f),
                new double2(-8228.73f, 126.39f), new double2(-8409.52f, 164.47f), new double2(-9432.81f, 168.43f),
                new double2(-9586.02f, 116.14f), new double2(-10758.65f, 110.23f), new double2(-10894.94f, 63.53f),
                new double2(-11737.45f, 60.54f), new double2(-11935.7f, 1.79f), new double2(-12437.14f, -4.33f),
                new double2(-12832.19f, 41.15f), new double2(-13271.23f, 30.64f), new double2(-13478.52f, 65.91f),
                new double2(-13729f, 65.71f), new double2(-13846.23f, 21.68f), new double2(-14000.3f, 62.86f),
                new double2(-15224.52f, 58.78f), new double2(-15232.59f, -142.28f), new double2(-4326.12f, -232.09f),
                new double2(-4083.7f, -441.37f), new double2(-3467.35f, -478.48f), new double2(-3040.92f, -1160.16f),
                new double2(7192.14f, -1332.7f), new double2(7249.66f, -939.11f), new double2(8399.41f, -932.84f),
                new double2(8816.72f, -830.49f), new double2(9861.58f, -835.71f), new double2(10065.59f, -1110.57f),
                new double2(10052.32f, -2118.14f), new double2(9006.64f, -2125.78f), new double2(8818.37f, -2203.58f),
                new double2(8846.09f, -2620.2f), new double2(14244.65f, -2650.96f)
            },
            new[]
            {
                97, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16, 17, 17, 18,
                18, 19, 19, 20, 20, 21, 21, 22, 22, 23, 23, 24, 24, 25, 25, 26, 26, 27, 27, 28, 28, 29, 29, 30, 30, 31, 31, 32, 32, 33,
                33, 34, 34, 35, 35, 36, 36, 37, 37, 38, 38, 39, 39, 40, 40, 41, 41, 42, 42, 43, 43, 44, 44, 45, 45, 46, 46, 47, 47, 48,
                48, 49, 49, 50, 50, 51, 51, 52, 52, 53, 53, 54, 54, 55, 55, 56, 56, 57, 57, 58, 58, 59, 59, 60, 60, 61, 61, 62, 62, 63,
                63, 64, 64, 65, 65, 66, 66, 67, 67, 68, 68, 69, 69, 70, 70, 71, 71, 72, 72, 73, 73, 74, 74, 75, 75, 76, 76, 77, 77, 78,
                78, 79, 79, 80, 80, 81, 81, 82, 82, 83, 83, 84, 84, 85, 85, 86, 86, 87, 87, 88, 88, 89, 89, 90, 90, 91, 91, 92, 92, 93,
                93, 94, 94, 95, 95, 96, 96, 97
            }
        );

        [Test]
        public void SloanMaxItersTest([Values] bool verbose)
        {
            using var inputPositions = new NativeArray<double2>(sloanMaxItersData.points, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(sloanMaxItersData.constraints, Allocator.Persistent);

            using var triangulator = new Triangulator<double2>(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
                    RefineMesh = false,
                    RestoreBoundary = true,
                    SloanMaxIters = 5,
                    Verbose = verbose,
                },
                Input =
                {
                    Positions = inputPositions,
                    ConstraintEdges = constraintEdges,
                }
            };

            if (verbose)
            {
                LogAssert.Expect(UnityEngine.LogType.Error, new Regex("Sloan max iterations exceeded.*"));
            }

            triangulator.Run();

            Assert.That(triangulator.Output.Status.Value, Is.EqualTo(Status.ERR));
        }

        [Test]
        public void PCATransformationPositionsConservationTest()
        {
            using var positions = new NativeArray<double2>(new[]
            {
                math.double2(1, 1),
                math.double2(2, 10),
                math.double2(2, 11),
                math.double2(1, 2),
            }, Allocator.Persistent);

            using var triangulator = new Triangulator<double2>(capacity: 1024, Allocator.Persistent)
            {
                Input = { Positions = positions },

                Settings =
                {
                    RefineMesh = false,
                    RestoreBoundary = true,
                    Preprocessor = Preprocessor.PCA
                }
            };

            triangulator.Run();

            var result = triangulator.Output.Positions.AsArray().ToArray();
            Assert.That(result, Is.EqualTo(positions).Using(Double2Comparer.With(epsilon: 0.0001f)));
        }

        [Test]
        public void PCATransformationPositionsConservationWithRefinementTest()
        {
            using var positions = new NativeArray<double2>(new[]
            {
                math.double2(1, 1),
                math.double2(2, 10),
                math.double2(2, 11),
                math.double2(1, 2),
            }, Allocator.Persistent);

            using var triangulator = new Triangulator<double2>(capacity: 1024, Allocator.Persistent)
            {
                Input = { Positions = positions },

                Settings =
                {
                    RefinementThresholds = { Area = 0.01f },
                    RefineMesh = true,
                    RestoreBoundary = true,
                    Preprocessor = Preprocessor.PCA
                }
            };

            triangulator.Run();

            var result = triangulator.Output.Positions.AsArray().ToArray()[..4];
            Assert.That(result, Is.EqualTo(positions).Using(Double2Comparer.With(epsilon: 0.0001f)));
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
            using var positions = new NativeArray<double2>(new[]
            {
                math.double2(0, 0), math.double2(3, 0), math.double2(3, 3), math.double2(0, 3),
                math.double2(1, 1), math.double2(2, 1), math.double2(2, 2), math.double2(1, 2),
            }, Allocator.Persistent);

            using var constraintEdges = new NativeArray<int>(new[]
            {
                0, 1, 1, 2, 2, 3, 3, 0,
                4, 5, 5, 6, 6, 7, 7, 4
            }, Allocator.Persistent);

            using var holes = new NativeArray<double2>(new[] { math.double2(1.5f) }, Allocator.Persistent);

            using var triangulator = new Triangulator<double2>(capacity: 1024, Allocator.Persistent)
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
                    RestoreBoundary = true,
                    Preprocessor = Preprocessor.PCA
                }
            };

            triangulator.Run();

            (int, int, int)[] expected =
            {
                (0, 3, 7),
                (4, 0, 7),
                (5, 0, 4),
                (5, 1, 0),
                (6, 1, 5),
                (6, 2, 1),
                (7, 2, 6),
                (7, 3, 2),
            };
            Assert.That(triangulator.GetTrisTuple(), Is.EqualTo(expected));
        }

        [Test, Description("Test checks if triangulation with data from GitHub Issue #30 passes.")]
        public void PCATransformationGithubIssue30Test()
        {
            using var positions = new NativeArray<double2>(new[]
            {
                new double2(14225.59f, -2335.27f), new double2(13380.24f, -2344.72f), new double2(13197.35f, -2119.65f),
                new double2(11750.51f, -2122.18f), new double2(11670.1f, -2186.25f), new double2(11424.88f, -2178.53f),
                new double2(11193.54f, -2025.36f), new double2(11159.36f, -1812.22f), new double2(10956.29f, -1731.62f),
                new double2(10949.03f, -1524.29f), new double2(10727.71f, -1379.53f), new double2(10532.48f, -1145.83f),
                new double2(10525.18f, -906.69f), new double2(10410.57f, -750.73f), new double2(10629.48f, -657.33f),
                new double2(10622f, -625.7f), new double2(10467.05f, -552.15f), new double2(10415.75f, -423.21f),
                new double2(10037.01f, -427.11f), new double2(9997.4f, -487.33f), new double2(9788.02f, -539.44f),
                new double2(9130.03f, -533.95f), new double2(8905.69f, -490.95f), new double2(8842.1f, -396.11f),
                new double2(8410.81f, -407.12f), new double2(8211.88f, -583.32f), new double2(7985.37f, -588.47f),
                new double2(7880.46f, -574.94f), new double2(7200.87f, -574.14f), new double2(6664.29f, -637.89f),
                new double2(6351.84f, -483.61f), new double2(6324.37f, -143.48f), new double2(6093.94f, -152.8f),
                new double2(5743.03f, 213.65f), new double2(5725.63f, 624.21f), new double2(5562.64f, 815.17f),
                new double2(5564.65f, 1145.66f), new double2(4846.4f, 1325.89f), new double2(4362.98f, 1327.97f),
                new double2(5265.89f, 267.31f), new double2(5266.32f, -791.39f), new double2(3806f, -817.38f),
                new double2(3385.84f, -501.25f), new double2(3374.35f, -372.48f), new double2(3555.98f, -321.11f),
                new double2(3549.9f, -272.35f), new double2(3356.27f, -221.45f), new double2(3352.42f, 13.16f),
                new double2(1371.39f, 5.41f), new double2(1362.47f, -191.23f), new double2(1188.9f, -235.72f),
                new double2(1180.86f, -709.59f), new double2(132.26f, -720.07f), new double2(1.94f, -788.66f),
                new double2(-1240.12f, -779.03f), new double2(-1352.26f, -973.64f), new double2(-1665.17f, -973.84f),
                new double2(-1811.91f, -932.75f), new double2(-1919.98f, -772.61f), new double2(-2623.09f, -782.31f),
                new double2(-3030.54f, -634.38f), new double2(-3045.53f, -52.71f), new double2(-3969.83f, -61.28f),
                new double2(-6676.96f, 102.16f), new double2(-7209.27f, 100.12f), new double2(-7729.39f, 178.02f),
                new double2(-8228.73f, 126.39f), new double2(-8409.52f, 164.47f), new double2(-9432.81f, 168.43f),
                new double2(-9586.02f, 116.14f), new double2(-10758.65f, 110.23f), new double2(-10894.94f, 63.53f),
                new double2(-11737.45f, 60.54f), new double2(-11935.7f, 1.79f), new double2(-12437.14f, -4.33f),
                new double2(-12832.19f, 41.15f), new double2(-13271.23f, 30.64f), new double2(-13478.52f, 65.91f),
                new double2(-13729f, 65.71f), new double2(-13846.23f, 21.68f), new double2(-14000.3f, 62.86f),
                new double2(-15224.52f, 58.78f), new double2(-15232.59f, -142.28f), new double2(-4326.12f, -232.09f),
                new double2(-4083.7f, -441.37f), new double2(-3467.35f, -478.48f), new double2(-3040.92f, -1160.16f),
                new double2(7192.14f, -1332.7f), new double2(7249.66f, -939.11f), new double2(8399.41f, -932.84f),
                new double2(8816.72f, -830.49f), new double2(9861.58f, -835.71f), new double2(10065.59f, -1110.57f),
                new double2(10052.32f, -2118.14f), new double2(9006.64f, -2125.78f), new double2(8818.37f, -2203.58f),
                new double2(8846.09f, -2620.2f), new double2(14244.65f, -2650.96f)
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

            using var triangulator = new Triangulator<double2>(capacity: 1024, Allocator.Persistent)
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
        }

        [Test, Description("Test checks if triangulation with data from GitHub Issue #31 passes.")]
        public void PCATransformationGithubIssue31Test()
        {
            using var positions = new NativeArray<double2>(new[]
            {
                new double2(31.28938f, 37.67612f), new double2(31.79285f, 37.00624f), new double2(32.03879f, 36.60557f),
                new double2(32.29923f, 36.36939f), new double2(32.58526f, 36.42342f), new double2(32.876f, 36.53085f),
                new double2(33.42577f, 36.38619f), new double2(33.88485f, 35.21272f), new double2(34.62434f, 34.02968f),
                new double2(34.73527f, 33.69278f), new double2(34.86366f, 33.55389f), new double2(35.10379f, 33.08732f),
                new double2(35.35777f, 32.77784f), new double2(35.69171f, 32.50069f), new double2(35.84656f, 32.22465f),
                new double2(36.01643f, 32.11908f), new double2(36.17846f, 31.92439f), new double2(36.32175f, 31.51735f),
                new double2(36.49083f, 31.40269f), new double2(36.6428f, 31.09395f), new double2(36.98143f, 30.87008f),
                new double2(37.34995f, 30.98518f), new double2(37.65298f, 30.35742f), new double2(38.14125f, 29.79839f),
                new double2(38.30097f, 29.57764f), new double2(38.63807f, 29.33636f), new double2(38.79191f, 29.04884f),
                new double2(38.95393f, 28.85409f), new double2(39.28638f, 28.56006f), new double2(39.44593f, 28.33743f),
                new double2(39.59904f, 28.04167f), new double2(39.76351f, 27.87474f), new double2(40.23344f, 27.10765f),
                new double2(40.5593f, 26.7389f), new double2(40.64825f, 26.77288f), new double2(40.15792f, 28.3197f),
                new double2(40.47878f, 27.74801f), new double2(40.6029f, 27.9564f), new double2(40.46587f, 28.58052f),
                new double2(40.49842f, 29.02806f), new double2(40.85061f, 29.12524f), new double2(41.09647f, 28.99164f),
                new double2(41.32619f, 28.90017f), new double2(41.62305f, 29.14183f), new double2(41.9069f, 29.41749f),
                new double2(42.3455f, 29.289f), new double2(42.62182f, 29.07582f), new double2(42.94831f, 28.73164f),
                new double2(43.10726f, 28.825f), new double2(43.09475f, 29.11192f), new double2(43.03548f, 29.52093f),
                new double2(43.47666f, 28.87722f), new double2(43.68863f, 28.83212f), new double2(44.24405f, 30.16822f),
                new double2(44.15243f, 30.4401f), new double2(44.13583f, 30.68668f), new double2(43.97319f, 31.2235f),
                new double2(43.59188f, 32.07505f), new double2(43.55514f, 32.32842f), new double2(43.28082f, 32.9029f),
                new double2(43.19778f, 33.17189f), new double2(42.99816f, 33.4802f), new double2(42.90062f, 33.75408f),
                new double2(42.84206f, 34.01482f), new double2(42.60212f, 34.33672f), new double2(42.51172f, 34.60819f),
                new double2(42.49178f, 34.8559f), new double2(42.44653f, 35.11214f), new double2(42.40163f, 35.85024f),
                new double2(42.3139f, 36.1208f), new double2(42.2475f, 36.86615f), new double2(42.09885f, 37.39825f),
                new double2(41.93857f, 37.69329f), new double2(41.9395f, 38.41592f), new double2(41.74668f, 39.06109f),
                new double2(41.71388f, 39.41131f), new double2(41.4817f, 39.82877f), new double2(41.40123f, 40.19506f),
                new double2(41.83162f, 40.72823f), new double2(41.72264f, 41.10414f), new double2(41.74435f, 41.43597f),
                new double2(41.68886f, 41.79384f), new double2(41.49154f, 42.19954f), new double2(41.38077f, 42.57606f),
                new double2(41.07294f, 43.35818f), new double2(40.84647f, 43.77372f), new double2(40.94663f, 44.0791f),
                new double2(40.84524f, 44.45245f), new double2(39.73372f, 45.59874f), new double2(39.7615f, 45.89402f),
                new double2(39.86456f, 46.3304f), new double2(39.79302f, 46.43954f), new double2(39.95979f, 46.8737f),
                new double2(40.02057f, 47.10924f), new double2(39.68147f, 46.96014f), new double2(39.87912f, 47.57381f),
                new double2(39.88906f, 47.83566f), new double2(39.62033f, 47.85281f), new double2(39.28307f, 47.74151f),
                new double2(38.99869f, 47.72932f), new double2(38.72726f, 47.7414f), new double2(37.57701f, 47.14798f),
                new double2(37.24718f, 47.0506f), new double2(37.07909f, 47.25636f), new double2(36.70338f, 47.07301f),
                new double2(36.64832f, 47.4906f), new double2(36.43682f, 47.61499f), new double2(36.1853f, 47.66438f),
                new double2(35.82184f, 47.50399f), new double2(35.3458f, 47.65338f), new double2(35.14989f, 47.98354f),
                new double2(35.08235f, 48.16993f), new double2(34.95149f, 48.71061f), new double2(34.80748f, 49.13337f),
                new double2(35.23618f, 45.39426f), new double2(35.2403f, 45.17966f), new double2(34.40559f, 48.73275f),
                new double2(34.30258f, 48.75045f), new double2(34.44608f, 47.66825f), new double2(34.40862f, 47.59851f),
                new double2(34.21555f, 47.40396f), new double2(33.79879f, 47.39106f), new double2(33.84159f, 47.00494f),
                new double2(33.46966f, 46.95563f), new double2(33.17641f, 47.0181f), new double2(32.9339f, 47.03938f),
                new double2(32.56659f, 46.98632f), new double2(31.7903f, 47.26541f), new double2(31.56507f, 47.27264f),
                new double2(31.26646f, 47.33947f), new double2(31.65442f, 46.67304f), new double2(31.69027f, 46.29256f),
                new double2(32.24787f, 45.48835f), new double2(32.47871f, 44.59815f), new double2(32.53637f, 44.19995f),
                new double2(32.54522f, 43.84141f), new double2(32.44212f, 43.57378f), new double2(32.21579f, 43.47334f),
                new double2(31.94443f, 43.40946f), new double2(31.53552f, 43.17303f), new double2(30.83317f, 43.17491f),
                new double2(30.61916f, 43.06446f), new double2(30.34215f, 43.00517f), new double2(30.04128f, 42.681f),
                new double2(30.19507f, 41.98758f), new double2(29.75365f, 41.77756f), new double2(29.58416f, 41.34669f),
                new double2(29.70014f, 40.94119f), new double2(29.99722f, 40.84808f), new double2(30.21854f, 40.72022f),
                new double2(29.90438f, 40.11747f), new double2(29.82732f, 39.62344f), new double2(30.09426f, 39.28717f),
                new double2(30.51098f, 39.01957f), new double2(30.38978f, 38.73465f), new double2(30.38398f, 38.04395f),
                new double2(30.40719f, 37.85884f), new double2(30.80989f, 37.99457f), new double2(31.34938f, 38.09515f)
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

            using var triangulator = new Triangulator<double2>(capacity: 1024, Allocator.Persistent)
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
        }

        [Test]
        public void CleanupPointsWithHolesTest()
        {
            using var positions = new NativeArray<double2>(new double2[]
            {
                new(0, 0),
                new(8, 0),
                new(8, 8),
                new(0, 8),

                new(2, 2),
                new(6, 2),
                new(6, 6),
                new(2, 6),
            }, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(new[]
            {
                0, 1, 1, 2, 2, 3, 3, 0,
                4, 5, 5, 6, 6, 7, 7, 4,
            }, Allocator.Persistent);
            using var holes = new NativeArray<double2>(new[] { math.double2(4) }, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraintEdges, HoleSeeds = holes },
                Settings = {
                    RefineMesh = true,
                    RestoreBoundary = false,
                    RefinementThresholds = { Area = 1f },
                },
            };

            triangulator.Schedule().Complete();

            Assert.That(triangulator.Output.Triangles.AsArray(), Has.All.LessThan(triangulator.Output.Positions.Length));
        }

        [Test]
        public void RefinementWithoutConstraintsTest()
        {
            var n = 20;

            using var positions = new NativeArray<double2>(Enumerable
                .Range(0, n)
                .Select(i => math.double2(
                    math.sin(i / (float)n * 2 * math.PI),
                    math.cos(i / (float)n * 2 * math.PI)))
                .ToArray(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(Enumerable
                .Range(0, n)
                .SelectMany(i => new[] { i, (i + 1) % n })
                .ToArray(), Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(1024 * 1024, Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = true,
                    RefineMesh = true,
                    RestoreBoundary = true,
                    RefinementThresholds =
                    {
                        Area = .10f,
                        Angle = math.radians(22f),
                    }
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraints,
                }
            };

            triangulator.Run();

            var trianglesWithConstraints = triangulator.GetTrisTuple();

            triangulator.Input.ConstraintEdges = default;
            triangulator.Run();
            var trianglesWithoutConstraints = triangulator.GetTrisTuple();

            Assert.That(trianglesWithConstraints, Is.EqualTo(trianglesWithoutConstraints));
        }

        [Test(Description = "Checks if triangulator passes for `very` accute angle input")]
        public void AccuteInputAngleTest()
        {
            using var positions = new NativeArray<double2>(new[] {
                math.double2(0, 0),
                math.double2(1, 0),
                math.double2(1, .1f),
            }, Allocator.Persistent);
            using var constraints = new NativeArray<int>(new[] {
                0, 1,
                1, 2,
                2, 0,
            }, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(1024 * 1024, Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = true,
                    RefineMesh = true,
                    RestoreBoundary = true,
                    RefinementThresholds =
                    {
                        Area = 1f,
                        Angle = math.radians(20f),
                    }
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraints,
                }
            };

            triangulator.Run();
        }

        [Test]
        public void GenericCase1Test()
        {
            using var positions = new NativeArray<double2>(new[] {
                math.double2(0, 0),
                math.double2(3, 0),
                math.double2(3, 1),
                math.double2(0, 1),
            }, Allocator.Persistent);
            using var constraints = new NativeArray<int>(new[] {
                0, 1,
                1, 2,
                2, 3,
                3, 0,
                0, 2,
            }, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(1024 * 1024, Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = true,
                    RefineMesh = true,
                    RestoreBoundary = true,
                    RefinementThresholds =
                    {
                        Area = .1f,
                        Angle = math.radians(33f),
                    }
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraints,
                }
            };

            triangulator.Run();

            triangulator.Draw();
        }

        [Test]
        public void GithubIssue111Test()
        {
            double2[] positions =
            {
                new(16.5f,1.5f),
                new(16.5f,2.5f),
                new(7.5f,2.5f),
                new(7.5f,8.5f),
                new(0.5f,8.5f),
                new(0.5f,15.5f),
                new(7.5f,15.5f),
                new(7.5f,20.5f),
                new(16.5f,20.5f),
                new(16.5f,21.5f),
                new(24.5f,21.5f),
                new(24.5f,20.5f),
                new(33.5f,20.5f),
                new(33.5f,15.5f),
                new(39.5f,15.5f),
                new(39.5f,8.5f),
                new(33.5f,8.5f),
                new(33.5f,2.5f),
                new(24.5f,2.5f),
                new(24.5f,1.5f),
                new(15.5f,15.5f),
                new(25.5f,15.5f),
                new(25.5f,18.5f),
                new(15.5f,18.5f),
                new(15.5f,4.5f),
                new(25.5f,4.5f),
                new(25.5f,7.5f),
                new(15.5f,7.5f),
                new(10.5f,6.5f),
                new(12.5f,6.5f),
                new(12.5f,17.5f),
                new(10.5f,17.5f),
                new(28.5f,6.5f),
                new(30.5f,6.5f),
                new(30.5f,17.5f),
                new(28.5f,17.5f),
                new(0.5f,11.5f),
                new(20.5f,21.5f),
                new(39.5f,11.5f),
                new(20.5f,1.44f)
            };

            int[] constraint =
            {
                0,1, 1,2, 2,3, 3,4, 36,5, 5,6, 6,7, 7,8, 8,9, 37,10, 10,11, 11,12, 12,13, 13,14, 38,15, 15,16, 16,17, 17,18, 18,19, 39,0, 20,21, 21,22, 22,23, 23,20, 24,25, 25,26, 26,27, 27,24, 28,29, 29,30, 30,31, 31,28, 32,33, 33,34, 34,35, 35,32, 4,36, 9,37, 14,38, 19,39
            };


            double2[] holesSeeds =
            {
                new(16.5f,16.5f),
                new(16.5f,5.5f),
                new(11.5f,7.5f),
                new(29.5f,7.5f),
            };

            using var constraintEdges = new NativeArray<int>(constraint, Allocator.Persistent);
            using var nativePositions = new NativeArray<double2>(positions, Allocator.Persistent);
            using var holes = new NativeArray<double2>(holesSeeds, Allocator.Persistent);

            using var triangulator = new Triangulator<double2>(Allocator.Persistent)
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

        [Test]
        public void HalfedgesForDelaunayTriangulationTest()
        {
            using var positions = new NativeArray<double2>(new double2[]
            {
                new(0),
                new(1),
                new(2),
                new(3),
                new(4),
                new(4, 0),
            }, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(Allocator.Persistent)
            {
                Input = { Positions = positions },
            };

            triangulator.Run();

            Assert.That(triangulator.Output.Halfedges.AsArray(), Is.EqualTo(new[]
            {
                8, 5, -1, -1, -1, 1, -1, 11, 0, -1, -1, 7,
            }));
        }

        [Test]
        public void HalfedgesForConstrainedDelaunayTriangulationTest()
        {
            using var positions = new NativeArray<double2>(new double2[]
            {
                new(0, 0),
                new(1, 0),
                new(2, 0),
                new(2, 1),
                new(1, 1),
                new(0, 1),
            }, Allocator.Persistent);
            using var constraints = new NativeArray<int>(new[] { 0, 3 }, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraints },
            };

            triangulator.Run();

            Assert.That(triangulator.Output.Halfedges.AsArray(), Is.EqualTo(new[]
            {
                -1, 10, 8, -1, -1, 11, -1, -1, 2, -1, 1, 5,
            }));
        }

        [Test]
        public void HalfedgesForConstrainedDelaunayTriangulationWithHolesTest()
        {
            using var positions = new NativeArray<double2>(new double2[]
            {
                math.double2(0, 0),
                math.double2(4, 0),
                math.double2(2, 4),
                math.double2(2, 2),
                math.double2(1.5f, 1),
                math.double2(2.5f, 1),
            }, Allocator.Persistent);
            using var constraints = new NativeArray<int>(new[]
            {
                0, 1, 1, 2, 2, 0,
                3, 4, 4, 5, 5, 3,
            }, Allocator.Persistent);
            using var holes = new NativeArray<double2>(new[] { math.double2(2, 1.5f) }, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holes }
            };

            triangulator.Run();

            Assert.That(triangulator.Output.Halfedges.AsArray(), Is.EqualTo(new[]
            {
                11, 3, -1, 1, 14, -1, 17, 9, -1, 7, -1, 0, -1, 15, 4, 13, -1, 6,
            }));
        }

        [Test]
        public void HalfedgesForTriangulationWithRefinementTest()
        {
            using var positions = new NativeArray<double2>(new double2[]
            {
                math.double2(0, 0),
                math.double2(2, 0),
                math.double2(1, 2),
            }, Allocator.Persistent);
            using var triangulator = new Triangulator<double2>(Allocator.Persistent)
            {
                Input = { Positions = positions },
                Settings = { RefineMesh = true, RefinementThresholds =
                {
                    Angle = math.radians(0),
                    Area = 0.5f
                }}
            };

            triangulator.Run();

            Assert.That(triangulator.Output.Halfedges.AsArray(), Is.EqualTo(new[]
            {
                -1, -1, 7, -1, -1, 6, 5, 2, 9, 8, -1, -1,
            }));
        }

        [Test]
        public void AutoHolesTest()
        {
            using var positions = new NativeArray<double2>(LakeSuperior.Points.Select(i => (double2)i).ToArray(), Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);
            using var holes = new NativeArray<double2>(LakeSuperior.Holes.Select(i => (double2)i).ToArray(), Allocator.Persistent);

            using var triangulator = new Triangulator<double2>(1024 * 1024, Allocator.Persistent)
            {
                Input = {
                    Positions = positions,
                    ConstraintEdges = constraintEdges,
                },
                Settings = { AutoHolesAndBoundary = true, },
            };

            triangulator.Run();

            var autoResult = triangulator.Output.Triangles.AsArray().ToArray();

            triangulator.Draw(color: UnityEngine.Color.green);

            triangulator.Input.HoleSeeds = holes;
            triangulator.Settings.AutoHolesAndBoundary = false;
            triangulator.Settings.RestoreBoundary = true;
            triangulator.Run();

            var manualResult = triangulator.Output.Triangles.AsArray().ToArray();
            Assert.That(autoResult, Is.EqualTo(manualResult));
        }
    }
}