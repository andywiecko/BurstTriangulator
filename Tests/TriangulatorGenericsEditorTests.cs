using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.TestTools;

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    public static class TestExtensions
    {
        public static void Run<T>(this Triangulator<T> triangulator) where T : unmanaged =>
            Extensions.Run((dynamic)triangulator);
        public static JobHandle Schedule<T>(this Triangulator<T> triangulator, JobHandle dependencies = default) where T : unmanaged =>
            Extensions.Schedule((dynamic)triangulator, dependencies);
        public static T[] DynamicCast<T>(this IEnumerable<float2> data) where T : unmanaged =>
            data.Select(i => (T)(dynamic)i).ToArray();
        public static T[] DynamicCast<T>(this IEnumerable<double2> data) where T : unmanaged =>
            data.Select(i => (T)(dynamic)i).ToArray();
        public static IEqualityComparer<T> Comparer<T>(float epsilon = 0.0001f) => default(T) switch
        {
            float2 _ => Float2Comparer.With(epsilon) as IEqualityComparer<T>,
            double2 _ => Double2Comparer.With(epsilon) as IEqualityComparer<T>,
            _ => throw new NotImplementedException()
        };
        public static void Draw<T>(this Triangulator<T> triangulator, float duration = 5f) where T : unmanaged =>
            TestUtils.Draw((dynamic)triangulator, duration);
        public static void Draw<T>(this Triangulator<T> triangulator, UnityEngine.Color color, float duration = 5f) where T : unmanaged =>
            TestUtils.Draw((dynamic)triangulator, color, duration);
    }

    [TestFixture(typeof(float2))]
    [TestFixture(typeof(double2))]
    public class TriangulatorGenericsEditorTests<T> where T : unmanaged
    {
        [Test]
        public void DelaunayTriangulationWithoutRefinementTest()
        {
            ///  3 ------- 2
            ///  |      . `|
            ///  |    *    |
            ///  |. `      |
            ///  0 ------- 1
            using var positions = new NativeArray<T>(new[]
            {
                math.float2(0, 0),
                math.float2(1, 0),
                math.float2(1, 1),
                math.float2(0, 1)
            }.DynamicCast<T>(), Allocator.Persistent);

            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
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
            using var positions = new NativeArray<T>(managedPositions.DynamicCast<T>(), Allocator.Persistent);
            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
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
        public void ValidateInputPositionsNoVerboseTest(float2[] managedPositions)
        {
            using var positions = new NativeArray<T>(managedPositions.DynamicCast<T>(), Allocator.Persistent);
            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
            {
                Settings = { ValidateInput = true, Verbose = false },
                Input = { Positions = positions },
            };

            triangulator.Run();

            Assert.That(triangulator.Output.Status.Value, Is.EqualTo(Status.ERR));
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
            using var positions = new NativeArray<T>(managedPositions.DynamicCast<T>(), Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
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
        public void ValidateConstraintDelaunayTriangulationNoVerboseTest(float2[] managedPositions, int[] constraints)
        {
            using var positions = new NativeArray<T>(managedPositions.DynamicCast<T>(), Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
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

        [Test]
        public void DelaunayTriangulationWithRefinementTest()
        {
            ///  3 ------- 2
            ///  |         |
            ///  |         |
            ///  |         |
            ///  0 ------- 1
            using var positions = new NativeArray<T>(new[]
            {
                math.float2(0, 0),
                math.float2(1, 0),
                math.float2(1, 1),
                math.float2(0, 1)
            }.DynamicCast<T>(), Allocator.Persistent);

            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
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
                math.float2(0, 0),
                math.float2(1, 0),
                math.float2(1, 1),
                math.float2(0, 1),
                math.float2(1, 0.5f),
                math.float2(0, 0.5f),
            }.DynamicCast<T>();
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
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
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
                math.float2(0, 0),
                math.float2(1, 0),
                math.float2(2, 0),
                math.float2(3, 0),
                math.float2(0, 3),
                math.float2(1, 3),
                math.float2(2, 3),
                math.float2(3, 3),
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
                math.float2(0, 0),
                math.float2(1, 0),
                math.float2(2, 0),
                math.float2(3, 0),
                math.float2(0, 1),
                math.float2(0, 2),
                math.float2(3, 1),
                math.float2(3, 2),
                math.float2(0, 3),
                math.float2(1, 3),
                math.float2(2, 3),
                math.float2(3, 3),
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
        public (int, int, int)[] ConstraintDelaunayTriangulationTest(float2[] managedPositions, int[] constraints)
        {
            using var positions = new NativeArray<T>(managedPositions.DynamicCast<T>(), Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
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
                    math.float2(0.5f, 0.5f),
                    math.float2(1f, 0.5f),
                    math.float2(0.5f, 0f),
                    math.float2(0f, 0.5f),
                    math.float2(0.8189806f, 0.8189806f),
                    math.float2(0.1810194f, 0.1810194f),
                    math.float2(0.5f, 1f),
                    math.float2(0.256f, 0f),
                    math.float2(0f, 0.256f),
                    math.float2(0.744f, 1f),
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
                    math.float2(1f, 0.5f),
                    math.float2(0.4579467f, 0.2289734f),
                    math.float2(1.542053f, 0.7710266f),
                    math.float2(0.5f, 0f),
                    math.float2(1.5f, 1f),
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
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0.75f, 0.75f),
                    math.float2(0, 1),
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
                    math.float2(1f, 0.5f),
                    math.float2(1f, 0.744f),
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
        public (int, int, int)[] ConstraintDelaunayTriangulationWithRefinementTest((float2[] managedPositions, int[] constraints, float2[] insertedPoints) input)
        {
            using var positions = new NativeArray<T>(input.managedPositions.DynamicCast<T>(), Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(input.constraints, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
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

            var expected = input.managedPositions.Union(input.insertedPoints).DynamicCast<T>();
            Assert.That(triangulator.Output.Positions.AsArray(), Is.EqualTo(expected).Using(TestExtensions.Comparer<T>()));

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

            using var positions = new NativeArray<T>(managedPositions.DynamicCast<T>(), Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
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

            using var positions = new NativeArray<T>(managedPositions.DynamicCast<T>(), Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
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

            var expectedPositions = managedPositions.Union(
                new[] { math.float2(0.5f, 0f) }
            ).ToArray().DynamicCast<T>();
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
        public (int, int, int)[] TriangulationWithHolesWithoutRefinementTest(float2[] managedPositions, int[] constraints, float2[] holeSeeds)
        {
            using var positions = new NativeArray<T>(managedPositions.DynamicCast<T>(), Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var holes = new NativeArray<T>(holeSeeds.DynamicCast<T>(), Allocator.Persistent);
            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
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
                math.float2(0, 0),
                math.float2(3, 0),
                math.float2(3, 3),
                math.float2(0, 3),

                math.float2(1, 1),
                math.float2(2, 1),
                math.float2(2, 2),
                math.float2(1, 2),
            }.DynamicCast<T>();

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

            using var positions = new NativeArray<T>(managedPositions, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var holes = new NativeArray<T>(new[] { (float2)1.5f }.DynamicCast<T>(), Allocator.Persistent);
            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
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

            var expectedPositions = managedPositions.Union(new float2[]
            {
                math.float2(1.5f, 0f),
                math.float2(3f, 1.5f),
                math.float2(1.5f, 3f),
                math.float2(0f, 1.5f),
            }.DynamicCast<T>());
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
        private static readonly Type[] _ = { // Forced compilation
            typeof(TriangulatorGenericsEditorTests<float2>.DeferredArraySupportInputJobFloat2),
            typeof(TriangulatorGenericsEditorTests<double2>.DeferredArraySupportInputJobDouble2),
        };

        [BurstCompile]
        private struct DeferredArraySupportInputJobDouble2 : IJob
        {
            public NativeList<double2> positions;
            public NativeList<int> constraints;
            public NativeList<double2> holes;

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

        [BurstCompile]
        private struct DeferredArraySupportInputJobFloat2 : IJob
        {
            public NativeList<float2> positions;
            public NativeList<int> constraints;
            public NativeList<float2> holes;

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
            using var positions = new NativeList<T>(64, Allocator.Persistent);
            using var constraints = new NativeList<int>(64, Allocator.Persistent);
            using var holes = new NativeList<T>(64, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(64, Allocator.Persistent)
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

            dependencies = default(T) switch
            {
                float2 _ => new DeferredArraySupportInputJobFloat2
                {
                    positions = (dynamic)positions,
                    constraints = constraints,
                    holes = (dynamic)holes
                }.Schedule(dependencies),
                double2 _ => new DeferredArraySupportInputJobDouble2
                {
                    positions = (dynamic)positions,
                    constraints = constraints,
                    holes = (dynamic)holes
                }.Schedule(dependencies),
                _ => throw new NotImplementedException()
            };

            dependencies = triangulator.Schedule(dependencies);
            dependencies.Complete();

            var expectedPositions = new[]
            {
                math.float2(0f, 0f),
                math.float2(3f, 0f),
                math.float2(3f, 3f),
                math.float2(0f, 3f),
                math.float2(1f, 1f),
                math.float2(2f, 1f),
                math.float2(2f, 2f),
                math.float2(1f, 2f),
                math.float2(1.5f, 0f),
                math.float2(3f, 1.5f),
                math.float2(1.5f, 3f),
                math.float2(0f, 1.5f),
            }.DynamicCast<T>();
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
            float2[] points =
            {
                0.01f * math.float2(-1, -1) + (float2)99999f,
                0.01f * math.float2(+1, -1) + (float2)99999f,
                0.01f * math.float2(+1, +1) + (float2)99999f,
                0.01f * math.float2(-1, +1) + (float2)99999f,
            };

            using var positions = new NativeArray<T>(points.DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(new[] { 1, 3 }, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(64, Allocator.Persistent)
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
                .Select(i => math.float2(
                 x: math.cos(2 * math.PI * i / n),
                 y: math.sin(2 * math.PI * i / n))).ToArray();
            managedPositions = new float2[] { 0 }.Concat(managedPositions).ToArray();
            managedPositions = managedPositions.Select(x => 0.1f * x + 5f).ToArray();

            using var positions = new NativeArray<T>(managedPositions.DynamicCast<T>(), Allocator.Persistent);
            using var triangulator = new Triangulator<T>(1024, Allocator.Persistent)
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

            using var holes = new NativeArray<T>(new float2[] { 5 }.DynamicCast<T>(), Allocator.Persistent);
            using var edges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var positions = new NativeArray<T>(managedPositions.DynamicCast<T>(), Allocator.Persistent);
            using var triangulator = new Triangulator<T>(1024, Allocator.Persistent)
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

        [Test]
        public void SloanMaxItersTest([Values] bool verbose)
        {
            using var inputPositions = new NativeArray<T>(GithubIssuesData.Issue30.points.DynamicCast<T>(), Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(GithubIssuesData.Issue30.constraints, Allocator.Persistent);

            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
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
            using var positions = new NativeArray<T>(new[]
            {
                math.float2(1, 1),
                math.float2(2, 10),
                math.float2(2, 11),
                math.float2(1, 2),
            }.DynamicCast<T>(), Allocator.Persistent);

            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
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
            Assert.That(result, Is.EqualTo(positions).Using(TestExtensions.Comparer<T>(epsilon: 0.0001f)));
        }

        [Test]
        public void PCATransformationPositionsConservationWithRefinementTest()
        {
            using var positions = new NativeArray<T>(new[]
            {
                math.float2(1, 1),
                math.float2(2, 10),
                math.float2(2, 11),
                math.float2(1, 2),
            }.DynamicCast<T>(), Allocator.Persistent);

            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
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
            Assert.That(result, Is.EqualTo(positions).Using(TestExtensions.Comparer<T>(epsilon: 0.0001f)));
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
            using var positions = new NativeArray<T>(new[]
            {
                math.float2(0, 0), math.float2(3, 0), math.float2(3, 3), math.float2(0, 3),
                math.float2(1, 1), math.float2(2, 1), math.float2(2, 2), math.float2(1, 2),
            }.DynamicCast<T>(), Allocator.Persistent);

            using var constraintEdges = new NativeArray<int>(new[]
            {
                0, 1, 1, 2, 2, 3, 3, 0,
                4, 5, 5, 6, 6, 7, 7, 4
            }, Allocator.Persistent);

            using var holes = new NativeArray<T>(new[] { math.float2(1.5f) }.DynamicCast<T>(), Allocator.Persistent);

            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
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

        [Test]
        public void CleanupPointsWithHolesTest()
        {
            using var positions = new NativeArray<T>(new float2[]
            {
                new(0, 0),
                new(8, 0),
                new(8, 8),
                new(0, 8),

                new(2, 2),
                new(6, 2),
                new(6, 6),
                new(2, 6),
            }.DynamicCast<T>(), Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(new[]
            {
                0, 1, 1, 2, 2, 3, 3, 0,
                4, 5, 5, 6, 6, 7, 7, 4,
            }, Allocator.Persistent);
            using var holes = new NativeArray<T>(new[] { math.float2(4) }.DynamicCast<T>(), Allocator.Persistent);
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
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

            using var positions = new NativeArray<T>(Enumerable
                .Range(0, n)
                .Select(i => math.float2(
                    math.sin(i / (float)n * 2 * math.PI),
                    math.cos(i / (float)n * 2 * math.PI)))
                .DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(Enumerable
                .Range(0, n)
                .SelectMany(i => new[] { i, (i + 1) % n })
                .ToArray(), Allocator.Persistent);
            using var triangulator = new Triangulator<T>(1024 * 1024, Allocator.Persistent)
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
            using var positions = new NativeArray<T>(new[] {
                math.float2(0, 0),
                math.float2(1, 0),
                math.float2(1, .1f),
            }.DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(new[] {
                0, 1,
                1, 2,
                2, 0,
            }, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(1024 * 1024, Allocator.Persistent)
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

            triangulator.Draw();
        }

        [Test]
        public void GenericCase1Test()
        {
            using var positions = new NativeArray<T>(new[] {
                math.float2(0, 0),
                math.float2(3, 0),
                math.float2(3, 1),
                math.float2(0, 1),
            }.DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(new[] {
                0, 1,
                1, 2,
                2, 3,
                3, 0,
                0, 2,
            }, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(1024 * 1024, Allocator.Persistent)
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
        public void HalfedgesForDelaunayTriangulationTest()
        {
            using var positions = new NativeArray<T>(new float2[]
            {
                new(0),
                new(1),
                new(2),
                new(3),
                new(4),
                new(4, 0),
            }.DynamicCast<T>(), Allocator.Persistent);
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
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
            using var positions = new NativeArray<T>(new float2[]
            {
                new(0, 0),
                new(1, 0),
                new(2, 0),
                new(2, 1),
                new(1, 1),
                new(0, 1),
            }.DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(new[] { 0, 3 }, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
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
            using var positions = new NativeArray<T>(new float2[]
            {
                math.float2(0, 0),
                math.float2(4, 0),
                math.float2(2, 4),
                math.float2(2, 2),
                math.float2(1.5f, 1),
                math.float2(2.5f, 1),
            }.DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(new[]
            {
                0, 1, 1, 2, 2, 0,
                3, 4, 4, 5, 5, 3,
            }, Allocator.Persistent);
            using var holes = new NativeArray<T>(new[] { math.float2(2, 1.5f) }.DynamicCast<T>(), Allocator.Persistent);
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
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
            using var positions = new NativeArray<T>(new float2[]
            {
                math.float2(0, 0),
                math.float2(2, 0),
                math.float2(1, 2),
            }.DynamicCast<T>(), Allocator.Persistent);
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
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
            using var positions = new NativeArray<T>(LakeSuperior.Points.DynamicCast<T>(), Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);
            using var holes = new NativeArray<T>(LakeSuperior.Holes.DynamicCast<T>(), Allocator.Persistent);

            using var triangulator = new Triangulator<T>(1024 * 1024, Allocator.Persistent)
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