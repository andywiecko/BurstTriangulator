using NUnit.Framework;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;
using NUnit.Framework.Interfaces;
using static andywiecko.BurstTriangulator.Utilities;

#if UNITY_MATHEMATICS_FIXEDPOINT
using Unity.Mathematics.FixedPoint;
#endif

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    public class TriangulatorGenericsEditorTests
    {
        [Test]
        public void MeshRefinementIntSupportTest()
        {
            using var positions = new NativeArray<int2>(LakeSuperior.Points.Select(i => (int2)(i * 1000)).ToArray(), Allocator.Persistent);
            using var holes = new NativeArray<int2>(LakeSuperior.Holes.Select(i => (int2)(i * 1000)).ToArray(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);

            using var triangulator = new Triangulator<int2>(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holes },
                Settings = { AutoHolesAndBoundary = false, RefineMesh = true, RestoreBoundary = true, Preprocessor = Preprocessor.None, ValidateInput = false }
            };

            triangulator.Run();

            LogAssert.Expect(LogType.Error, new Regex("Mesh refinement is not supported for this coordinate type.*"));
        }

        [Test]
        public void IntOverflowTest()
        {
            //        ....'''''''''::::1
            //     3::------ 2'''''  .'
            //     '. '.  .' :     .'
            //       '. '4'  :   .'
            //         '. '. : .'
            //           '.'.'
            //             0
            using var positions = new NativeArray<int2>(new int2[]
            {
                new(0, 0),
                new(+50_000, +50_000),
                new(+20_000, +49_000),
                new(-50_000, +49_000),
                new(-15_000, +40_000),
            }, Allocator.Persistent);
            using var triangulator = new Triangulator<int2>(Allocator.Persistent)
            {
                Input = { Positions = positions }
            };

            triangulator.Run();

            int[] expected = { 4, 2, 0, 2, 1, 0, 0, 3, 4, 4, 3, 2, 2, 3, 1 };
            Assert.That(triangulator.Output.Triangles.AsArray(), Is.EqualTo(expected).Using(TrianglesComparer.Instance));
        }
    }

    [TestFixture(typeof(float2))]
    [TestFixture(typeof(Vector2))]
    [TestFixture(typeof(double2))]
    [TestFixture(typeof(int2))]
#if UNITY_MATHEMATICS_FIXEDPOINT
    [TestFixture(typeof(fp2))]
#endif
    public class TriangulatorGenericsEditorTests<T> where T : unmanaged
    {
        private static RunState IgnoreInt2AndFp2() => default(T) switch
        {
            int2 => RunState.Ignored,
#if UNITY_MATHEMATICS_FIXEDPOINT
            fp2 => RunState.Ignored,
#endif
            _ => RunState.Runnable,
        };

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

            Assert.That(triangulator.Output.Triangles.AsArray(), Is.EqualTo(new[] { 0, 2, 1, 0, 3, 2 }).Using(TrianglesComparer.Instance));
        }

        private static readonly TestCaseData[] validateArgsTestData = new[]
        {
            new TestCaseData(new TriangulationSettings{ AutoHolesAndBoundary = true }, Status.OK, false){ TestName = "Test case 1 (log warning for AutoHolesAndBoundary)." },
            new TestCaseData(new TriangulationSettings{ RestoreBoundary = true }, Status.OK, false){ TestName = "Test case 2 (log warning for RestoreBoundary)." },
            new TestCaseData(new TriangulationSettings{ RefineMesh = true }, Status.ERR_ARGS_INVALID, false){ TestName = "Test case 3 (log error for RefineMesh).", RunState = typeof(T) != typeof(int2) ? RunState.Ignored : RunState.Runnable },
            new TestCaseData(new TriangulationSettings{ SloanMaxIters = -100 }, Status.ERR_ARGS_INVALID, true){ TestName = "Test case 4 (log error for SloanMaxIters)." },
            new TestCaseData(new TriangulationSettings{ RefineMesh = true, RefinementThresholds = { Area = -1 } }, Status.ERR_ARGS_INVALID, false){ TestName = "Test case 5 (log error for negative area threshold).", RunState = typeof(T) == typeof(int2) ? RunState.Ignored : RunState.Runnable },
            new TestCaseData(new TriangulationSettings{ RefineMesh = true, RefinementThresholds = { Angle = -1 } }, Status.ERR_ARGS_INVALID, false){ TestName = "Test case 6 (log error for negative angle threshold).", RunState = typeof(T) == typeof(int2) ? RunState.Ignored : RunState.Runnable },
            new TestCaseData(new TriangulationSettings{ RefineMesh = true, RefinementThresholds = { Angle = math.PI / 4 + 1e-5f } }, Status.ERR_ARGS_INVALID, false){ TestName = "Test case 7 (log error for too big angle threshold).", RunState = typeof(T) == typeof(int2) ? RunState.Ignored : RunState.Runnable },
            new TestCaseData(new TriangulationSettings{ UseAlphaShapeFilter = true, AlphaShapeSettings = { Alpha = -1 } }, Status.ERR_ARGS_INVALID, false){ TestName = "Test case 8 (log error for negative alpha)." },
        };

        [Test, TestCaseSource(nameof(validateArgsTestData))]
        public void ValidateArgsTest(TriangulationSettings settings, Status expectedStatus, bool constrain)
        {
            using var constraints = constrain ? new NativeArray<int>(new[] { 0, 1 }, Allocator.Persistent) : default;
            using var positions = new NativeArray<T>(new[] { math.float2(0, 0), math.float2(1, 0), math.float2(1, 1) }.DynamicCast<T>(), Allocator.Persistent);
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraints },
                Settings = settings,
            };

            LogAssert.Expect(expectedStatus == Status.OK ? LogType.Warning : LogType.Error, new Regex(".*"));
            triangulator.Run();

            Assert.That(triangulator.Output.Status.Value, Is.EqualTo(expectedStatus));
        }

        private static readonly TestCaseData[] validateInputPositionsTestData = new TestCaseData[]
        {
            new(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(0, 1)
                },
                Status.ERR_INPUT_POSITIONS_LENGTH
            ) { TestName = "Test Case 1 (points count less than 3)" },
            new(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(0, 0),
                    math.float2(1, 1),
                    math.float2(0, 1)
                },
                Status.ERR_INPUT_POSITIONS_DUPLICATES
            ) { TestName = "Test Case 2 (duplicated position)" },
            new(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, float.NaN),
                    math.float2(1, 1),
                    math.float2(0, 1)
                },
                Status.ERR_INPUT_POSITIONS_UNDEFINED_VALUE
            ) { TestName = "Test Case 3 (point with NaN)", RunState = IgnoreInt2AndFp2() },
            new(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, float.PositiveInfinity),
                    math.float2(1, 1),
                    math.float2(0, 1)
                },
                Status.ERR_INPUT_POSITIONS_UNDEFINED_VALUE
            ) { TestName = "Test Case 4 (point with +inf)", RunState = IgnoreInt2AndFp2() },
            new(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, float.NegativeInfinity),
                    math.float2(1, 1),
                    math.float2(0, 1)
                },
                Status.ERR_INPUT_POSITIONS_UNDEFINED_VALUE
            ) { TestName = "Test Case 4 (point with -inf)", RunState = IgnoreInt2AndFp2() },
            new(
                new[]
                {
                    math.float2(0),
                    math.float2(1),
                    math.float2(2),
                    math.float2(3),
                },
                Status.ERR_DELAUNAY_DUPLICATES_OR_COLLINEAR
            ) { TestName = "Test Case 5 (all collinear)" }
        }.SelectMany(i => new[]
        {
            new TestCaseData(i.Arguments[0], i.Arguments[1], true){ TestName = i.TestName + ", verbose", RunState = i.RunState },
            new TestCaseData(i.Arguments[0], i.Arguments[1], false){ TestName = i.TestName + ", no verbose", RunState = i.RunState }
        }).ToArray();

        [Test, TestCaseSource(nameof(validateInputPositionsTestData))]
        public void ValidateInputPositionsTest(float2[] managedPositions, Status expectedStatus, bool verbose)
        {
            using var positions = new NativeArray<T>(managedPositions.DynamicCast<T>(), Allocator.Persistent);
            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
            {
                Settings = { ValidateInput = true, Verbose = verbose },
                Input = { Positions = positions },
            };

            if (verbose)
            {
                LogAssert.Expect(LogType.Error, new Regex(".*"));
            }
            triangulator.Run();

            Assert.That(triangulator.Output.Status.Value, Is.EqualTo(expectedStatus));
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
                new[]{ 0, 2, 1, 3 },
                Status.ERR_INPUT_CONSTRAINTS_INTERSECTING
            ) { TestName = "Test Case 1 (edge-edge intersection)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ 0, 2, 0, 2 },
                Status.ERR_INPUT_CONSTRAINTS_DUPLICATES
            ) { TestName = "Test Case 2 (duplicated edge)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ 0, 0 },
                Status.ERR_INPUT_CONSTRAINTS_SELF_LOOP
            ) { TestName = "Test Case 3 (zero-length edge)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(2, 0),
                    math.float2(2, 2),
                    math.float2(0, 2),
                },
                new[]{ 0, 2 },
                Status.ERR_INPUT_CONSTRAINTS_COLLINEAR
            ) { TestName = "Test Case 4 (edge collinear with other point)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ 0, 5, 2 },
                Status.ERR_INPUT_CONSTRAINTS_LENGTH
            ) { TestName = "Test Case 5 (odd number of elements in constraints buffer)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ -1, 1, 1, 2 },
                Status.ERR_INPUT_CONSTRAINTS_OUT_OF_RANGE
            ) { TestName = "Test Case 6a (constraint out of positions range)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ 1, -1, 1, 2 },
                Status.ERR_INPUT_CONSTRAINTS_OUT_OF_RANGE
            ) { TestName = "Test Case 6b (constraint out of positions range)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ 5, 1, 1, 2 },
                Status.ERR_INPUT_CONSTRAINTS_OUT_OF_RANGE
            ) { TestName = "Test Case 6c (constraint out of positions range)" },
            new TestCaseData(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new[]{ 1, 5, 1, 2 },
                Status.ERR_INPUT_CONSTRAINTS_OUT_OF_RANGE
            ) { TestName = "Test Case 6d (constraint out of positions range)" },
        }.SelectMany(i => new[]
        {
            new TestCaseData(i.Arguments[0], i.Arguments[1], i.Arguments[2], true){ TestName = i.TestName + ", verbose"},
            new TestCaseData(i.Arguments[0], i.Arguments[1], i.Arguments[2], false){ TestName = i.TestName + ", no verbose" }
        }).ToArray();

        [Test, TestCaseSource(nameof(validateConstraintDelaunayTriangulationTestData))]
        public void ValidateConstraintDelaunayTriangulationTest(float2[] managedPositions, int[] constraints, Status expectedStatus, bool verbose)
        {
            using var positions = new NativeArray<T>(managedPositions.DynamicCast<T>(), Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = true,
                    Verbose = verbose,
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges,
                }
            };

            if (verbose)
            {
                LogAssert.Expect(LogType.Error, new Regex(".*"));
            }
            triangulator.Run();

            Assert.That(triangulator.Output.Status.Value, Is.EqualTo(expectedStatus));
        }

        private static readonly TestCaseData[] validateIgnoredConstraintTestData = new TestCaseData[]
        {
            new(new int[]{ 0, 1 }, new bool[]{ false, false }, Status.ERR_INPUT_IGNORED_CONSTRAINTS_LENGTH) { TestName = "Test case 1 (error, bad length)"},
            new(default(int[]), new bool[]{ false, false }, Status.OK) { TestName = "Test case 2 (warning, missing constraints)"},
        }.SelectMany(i => new[]
        {
            new TestCaseData(i.Arguments[0], i.Arguments[1], i.Arguments[2], true){ TestName = i.TestName + ", verbose"},
            new TestCaseData(i.Arguments[0], i.Arguments[1], i.Arguments[2], false){ TestName = i.TestName + ", no verbose" }
        }).ToArray();

        [Test, TestCaseSource(nameof(validateIgnoredConstraintTestData))]
        public void ValidateIgnoredConstraintTest(int[] constraints, bool[] ignoredConstraints, Status expectedStatus, bool verbose)
        {
            using var positions = new NativeArray<T>(new float2[] { 0, 1, math.float2(0, 1), }.DynamicCast<T>(), Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints ?? (new int[0]), Allocator.Persistent);
            using var ignoredConstraint = new NativeArray<bool>(ignoredConstraints, Allocator.Persistent);
            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = true,
                    Verbose = verbose,
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraints != null ? constraintEdges : default,
                    IgnoreConstraintForPlantingSeeds = ignoredConstraint,
                }
            };

            if (verbose)
            {
                LogAssert.Expect(expectedStatus == Status.OK ? LogType.Warning : LogType.Error, new Regex(".*"));
            }

            triangulator.Run();

            Assert.That(triangulator.Output.Status.Value, Is.EqualTo(expectedStatus));
        }

        private static readonly TestCaseData[] validateHolesTestData = new[]
        {
            new TestCaseData(default(int[]), new[]{ math.float2(2, 1) / 3, math.float2(2, 1) / 3 }, Status.OK)
            { TestName = "Test case 1 (log warning, constraints buffer not provided)"},
            new TestCaseData(default(int[]), new[]{ (float2)float.NaN }, Status.ERR_INPUT_HOLES_UNDEFINED_VALUE)
            { TestName = "Test case 2 (log error, nan)", RunState = IgnoreInt2AndFp2() },
            new TestCaseData(default(int[]), new[]{ (float2)float.PositiveInfinity}, Status.ERR_INPUT_HOLES_UNDEFINED_VALUE)
            { TestName = "Test case 3 (log error, +inf)", RunState = IgnoreInt2AndFp2() },
            new TestCaseData(default(int[]), new[]{ (float2)float.NegativeInfinity }, Status.ERR_INPUT_HOLES_UNDEFINED_VALUE)
            { TestName = "Test case 4 (log error, -inf)", RunState = IgnoreInt2AndFp2() },
        }.SelectMany(i => new[]
        {
            new TestCaseData(i.Arguments[0], i.Arguments[1], i.Arguments[2], true){ TestName = i.TestName + ", verbose", RunState = i.RunState },
            new TestCaseData(i.Arguments[0], i.Arguments[1], i.Arguments[2], false){ TestName = i.TestName + ", no verbose", RunState = i.RunState }
        }).ToArray();

        [Test, TestCaseSource(nameof(validateHolesTestData))]
        public void ValidateHolesTest(int[] constraints, float2[] holes, Status expected, bool verbose)
        {
            using var positions = new NativeArray<T>(new float2[] { 0, math.float2(1, 0), math.float2(1, 1), }.DynamicCast<T>(), Allocator.Persistent);
            using var constraintEdges = constraints != null ? new NativeArray<int>(constraints, Allocator.Persistent) : default;
            using var holeSeeds = new NativeArray<T>(holes.DynamicCast<T>(), Allocator.Persistent);
            using var triangulator = new Triangulator<T>(capacity: 1024, Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = true,
                    Verbose = verbose,
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges,
                    HoleSeeds = holeSeeds,
                }
            };

            if (verbose)
            {
                LogAssert.Expect(expected == Status.OK ? LogType.Warning : LogType.Error, new Regex(".*"));
            }

            triangulator.Run();

            Assert.That(triangulator.Output.Status.Value, Is.EqualTo(expected));
        }

        private static readonly TestCaseData[] edgeConstraintsTestData =
        {
            new(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(1, 0),
                    math.float2(1, 1),
                    math.float2(0, 1),
                },
                new int[]{ },
                new[]
                {
                    0, 2, 1,
                    0, 3, 2,
                }
            ){ TestName = "Test case 0" },
            new(
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
                },
                new[] { 1, 5 },
                new[]
                {
                    1, 0, 7,
                    3, 2, 1,
                    5, 3, 1,
                    5, 4, 3,
                    7, 5, 1,
                    7, 6, 5,
                }
            ){ TestName = "Test case 1" },
            new(
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
                },
                new[] { 1, 5, 1, 4 },
                new[]
                {
                    1, 0, 7,
                    3, 2, 1,
                    4, 3, 1,
                    5, 4, 1,
                    7, 5, 1,
                    7, 6, 5,
                }
            ){ TestName = "Test case 2" },
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
                }, new[] { 3, 9, 8, 5 },
                new[]
                {
                    1, 0, 11,
                    4, 3, 8,
                    7, 6, 5,
                    8, 5, 4,
                    8, 7, 5,
                    9, 8, 3,
                    10, 3, 2,
                    10, 9, 3,
                    11, 2, 1,
                    11, 10, 2,
                }
                ){ TestName = "Test case 3" },
            //   4   5   6   7
            //   *   *   *   *
            //
            //
            //
            //
            // *   *   *   *
            // 0   1   2   3
            new(new[]
            {
                math.float2(0, 0),
                math.float2(2, 0),
                math.float2(4, 0),
                math.float2(6, 0),
                math.float2(1, 3),
                math.float2(3, 3),
                math.float2(5, 3),
                math.float2(7, 3),
            },
            new int[] {},
            new[]
                {
                    0, 4, 1,
                    1, 4, 5,
                    1, 5, 2,
                    2, 5, 6,
                    2, 6, 3,
                    3, 6, 7,
                }
            )
            {
                TestName = "Test case 4 (no constraints)",
            },
            // 4   5   6   7
            // *   *   *  ,*
            //         ..;
            //      ..;
            //   ..;
            //  ;
            // *   *   *   *
            // 0   1   2   3
            new(
                new[]
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
                new[] { 0, 7 },
                new[]
                {
                    1, 0, 7,
                    4, 5, 0,
                    5, 6, 0,
                    6, 7, 0,
                    7, 2, 1,
                    7, 3, 2,
                }
            ){ TestName = "Test case 4" },
            //    8   9   10  11
            //    *   *   *   *
            //             ..;
            //  5 *      ..;  * 7
            //         ..;
            //  4 *  ..;      * 6
            //     ..;
            //    *   *   *   *
            //    0   1   2   3
            new(
                new[]
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
                new[]{ 0, 11 },
                new[]
                {
                    1, 0, 11,
                    4, 11, 0,
                    5, 9, 4,
                    6, 2, 1,
                    6, 3, 2,
                    7, 6, 1,
                    8, 9, 5,
                    9, 10, 4,
                    10, 11, 4,
                    11, 7, 1,
                }
            ){ TestName = "Test case 5" }
        };

        [Test, TestCaseSource(nameof(edgeConstraintsTestData))]
        public void ConstraintDelaunayTriangulationTest(float2[] managedPositions, int[] constraints, int[] expected)
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

            Assert.That(triangulator.Output.Triangles.AsArray(), Is.EqualTo(expected).Using(TrianglesComparer.Instance));
        }

        [Test]
        public void BoundaryReconstructionWithoutRefinementTest()
        {
            // 7 -------------- 6       3 ----- 2
            // |                |       |       |
            // |                |       |       |
            // |                5 ----- 4       |
            // |                                |
            // |                                |
            // 0 ------------------------------ 1
            var managedPositions = new[]
            {
                math.float2(0, 0),
                math.float2(4, 0),
                math.float2(4, 2),
                math.float2(3, 2),
                math.float2(3, 1),
                math.float2(2, 1),
                math.float2(2, 2),
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
                1, 0, 5,
                1, 5, 4,
                2, 1, 4,
                4, 3, 2,
                5, 0, 7,
                5, 7, 6,
            };
            Assert.That(triangulator.Output.Triangles.AsArray(), Is.EqualTo(expected).Using(TrianglesComparer.Instance));
        }

        private static readonly TestCaseData[] triangulationWithHolesWithoutRefinementTestData =
        {
            //   3 --------------------- 2
            //   |                       |
            //   |                       |
            //   |       7 ----- 6       |
            //   |       |   X   |       |
            //   |       |       |       |
            //   |       4 ----- 5       |
            //   |                       |
            //   |                       |
            //   0 --------------------- 1
            new(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(6, 0),
                    math.float2(6, 6),
                    math.float2(0, 6),

                    math.float2(2, 2),
                    math.float2(4, 2),
                    math.float2(4, 4),
                    math.float2(2, 4),
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
                new[] { (float2)3f },
                new[]
                {
                    0, 3, 7,
                    4, 0, 7,
                    5, 0, 4,
                    5, 1, 0,
                    6, 1, 5,
                    6, 2, 1,
                    7, 2, 6,
                    7, 3, 2,
                }
            ){ TestName = "Test Case 1" },
            new(
                new[]
                {
                    math.float2(0, 0),
                    math.float2(6, 0),
                    math.float2(6, 6),
                    math.float2(0, 6),

                    math.float2(2, 2),
                    math.float2(4, 2),
                    math.float2(4, 4),
                    math.float2(2, 4),
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
                new[] { (float2)3f, (float2)3f },
                new[]
                {
                    0, 3, 7,
                    4, 0, 7,
                    5, 0, 4,
                    5, 1, 0,
                    6, 1, 5,
                    6, 2, 1,
                    7, 2, 6,
                    7, 3, 2,
                }
            ){ TestName = "Test Case 2 (duplicated hole)" },
            new(
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
                new[] { (float2)1000 },
                new[]
                {
                    0, 3, 7,
                    4, 0, 7,
                    4, 6, 5,
                    4, 7, 6,
                    5, 0, 4,
                    5, 1, 0,
                    6, 1, 5,
                    6, 2, 1,
                    7, 2, 6,
                    7, 3, 2,
                }
            ){ TestName = "Test Case 3 (hole out of range)" },
            new(
                //   3 --------------------- 2
                //   |                       |
                //   |                       |
                //   |                       |
                //   |    7 ----- 6          |
                //   |    |     X |          |
                //   |    | X     |          |
                //   |    4 ----- 5          |
                //   |                       |
                //   0 --------------------- 1
                new[]
                {
                    math.float2(0, 0),
                    math.float2(9, 0),
                    math.float2(9, 9),
                    math.float2(0, 9),

                    math.float2(2, 2),
                    math.float2(5, 2),
                    math.float2(5, 5),
                    math.float2(2, 5),
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
                new[] { math.float2(3, 3), math.float2(4, 4) },
                new[]
                {
                    0, 3, 7,
                    0, 4, 5,
                    0, 5, 1,
                    0, 7, 4,
                    1, 5, 6,
                    1, 6, 2,
                    2, 6, 3,
                    3, 6, 7,
                }
            ){ TestName = "Test Case 4 (hole seeds in the same area)" },
        };

        [Test, TestCaseSource(nameof(triangulationWithHolesWithoutRefinementTestData))]
        public void TriangulationWithHolesWithoutRefinementTest(float2[] managedPositions, int[] constraints, float2[] holeSeeds, int[] expected)
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

            Assert.That(triangulator.Output.Triangles.AsArray(), Is.EqualTo(expected).Using(TrianglesComparer.Instance));
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
            typeof(TriangulatorGenericsEditorTests<Vector2>.DeferredArraySupportInputJobVector2),
            typeof(TriangulatorGenericsEditorTests<int2>.DeferredArraySupportInputJobInt2),
#if UNITY_MATHEMATICS_FIXEDPOINT
            typeof(TriangulatorGenericsEditorTests<fp2>.DeferredArraySupportInputJobFp2),
#endif
        };

        [BurstCompile]
        private struct DeferredArraySupportInputJobDouble2 : IJob
        {
            private NativeList<double2> positions;
            private NativeList<int> constraints;
            private NativeList<double2> holes;

            public DeferredArraySupportInputJobDouble2(NativeList<T> positions, NativeList<int> constraints, NativeList<T> holes)
            {
                this.positions = (dynamic)positions;
                this.constraints = constraints;
                this.holes = (dynamic)holes;
            }

            public void Execute()
            {
                positions.Add(new(0, 0));
                positions.Add(new(4, 0));
                positions.Add(new(4, 4));
                positions.Add(new(0, 4));
                positions.Add(new(1, 1));
                positions.Add(new(3, 1));
                positions.Add(new(3, 3));
                positions.Add(new(1, 3));

                constraints.Add(0); constraints.Add(1);
                constraints.Add(1); constraints.Add(2);
                constraints.Add(2); constraints.Add(3);
                constraints.Add(3); constraints.Add(0);
                constraints.Add(4); constraints.Add(5);
                constraints.Add(5); constraints.Add(6);
                constraints.Add(6); constraints.Add(7);
                constraints.Add(7); constraints.Add(4);

                holes.Add(new(2, 2));
            }
        }

        [BurstCompile]
        private struct DeferredArraySupportInputJobFloat2 : IJob
        {
            private NativeList<float2> positions;
            private NativeList<int> constraints;
            private NativeList<float2> holes;

            public DeferredArraySupportInputJobFloat2(NativeList<T> positions, NativeList<int> constraints, NativeList<T> holes)
            {
                this.positions = (dynamic)positions;
                this.constraints = constraints;
                this.holes = (dynamic)holes;
            }

            public void Execute()
            {
                positions.Add(new(0, 0));
                positions.Add(new(4, 0));
                positions.Add(new(4, 4));
                positions.Add(new(0, 4));
                positions.Add(new(1, 1));
                positions.Add(new(3, 1));
                positions.Add(new(3, 3));
                positions.Add(new(1, 3));

                constraints.Add(0); constraints.Add(1);
                constraints.Add(1); constraints.Add(2);
                constraints.Add(2); constraints.Add(3);
                constraints.Add(3); constraints.Add(0);
                constraints.Add(4); constraints.Add(5);
                constraints.Add(5); constraints.Add(6);
                constraints.Add(6); constraints.Add(7);
                constraints.Add(7); constraints.Add(4);

                holes.Add(new(2, 2));
            }
        }

        [BurstCompile]
        private struct DeferredArraySupportInputJobVector2 : IJob
        {
            private NativeList<Vector2> positions;
            private NativeList<int> constraints;
            private NativeList<Vector2> holes;

            public DeferredArraySupportInputJobVector2(NativeList<T> positions, NativeList<int> constraints, NativeList<T> holes)
            {
                this.positions = (dynamic)positions;
                this.constraints = constraints;
                this.holes = (dynamic)holes;
            }

            public void Execute()
            {
                positions.Add(new(0, 0));
                positions.Add(new(4, 0));
                positions.Add(new(4, 4));
                positions.Add(new(0, 4));
                positions.Add(new(1, 1));
                positions.Add(new(3, 1));
                positions.Add(new(3, 3));
                positions.Add(new(1, 3));

                constraints.Add(0); constraints.Add(1);
                constraints.Add(1); constraints.Add(2);
                constraints.Add(2); constraints.Add(3);
                constraints.Add(3); constraints.Add(0);
                constraints.Add(4); constraints.Add(5);
                constraints.Add(5); constraints.Add(6);
                constraints.Add(6); constraints.Add(7);
                constraints.Add(7); constraints.Add(4);

                holes.Add(new(2, 2));
            }
        }

        [BurstCompile]
        private struct DeferredArraySupportInputJobInt2 : IJob
        {
            private NativeList<int2> positions;
            private NativeList<int> constraints;
            private NativeList<int2> holes;

            public DeferredArraySupportInputJobInt2(NativeList<T> positions, NativeList<int> constraints, NativeList<T> holes)
            {
                this.positions = (dynamic)positions;
                this.constraints = constraints;
                this.holes = (dynamic)holes;
            }

            public void Execute()
            {
                positions.Add(new(0, 0));
                positions.Add(new(4, 0));
                positions.Add(new(4, 4));
                positions.Add(new(0, 4));
                positions.Add(new(1, 1));
                positions.Add(new(3, 1));
                positions.Add(new(3, 3));
                positions.Add(new(1, 3));

                constraints.Add(0); constraints.Add(1);
                constraints.Add(1); constraints.Add(2);
                constraints.Add(2); constraints.Add(3);
                constraints.Add(3); constraints.Add(0);
                constraints.Add(4); constraints.Add(5);
                constraints.Add(5); constraints.Add(6);
                constraints.Add(6); constraints.Add(7);
                constraints.Add(7); constraints.Add(4);

                holes.Add(new(2, 2));
            }
        }

#if UNITY_MATHEMATICS_FIXEDPOINT
        [BurstCompile]
        private struct DeferredArraySupportInputJobFp2 : IJob
        {
            private NativeList<fp2> positions;
            private NativeList<int> constraints;
            private NativeList<fp2> holes;

            public DeferredArraySupportInputJobFp2(NativeList<T> positions, NativeList<int> constraints, NativeList<T> holes)
            {
                this.positions = (dynamic)positions;
                this.constraints = constraints;
                this.holes = (dynamic)holes;
            }

            public void Execute()
            {
                positions.Add(new(0, 0));
                positions.Add(new(4, 0));
                positions.Add(new(4, 4));
                positions.Add(new(0, 4));
                positions.Add(new(1, 1));
                positions.Add(new(3, 1));
                positions.Add(new(3, 3));
                positions.Add(new(1, 3));

                constraints.Add(0); constraints.Add(1);
                constraints.Add(1); constraints.Add(2);
                constraints.Add(2); constraints.Add(3);
                constraints.Add(3); constraints.Add(0);
                constraints.Add(4); constraints.Add(5);
                constraints.Add(5); constraints.Add(6);
                constraints.Add(6); constraints.Add(7);
                constraints.Add(7); constraints.Add(4);

                holes.Add(new(2, 2));
            }
        }
#endif

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
                    RestoreBoundary = true,
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
                float2 _ => new DeferredArraySupportInputJobFloat2(positions, constraints, holes).Schedule(dependencies),
                double2 _ => new DeferredArraySupportInputJobDouble2(positions, constraints, holes).Schedule(dependencies),
                Vector2 _ => new DeferredArraySupportInputJobVector2(positions, constraints, holes).Schedule(dependencies),
                int2 _ => new DeferredArraySupportInputJobInt2(positions, constraints, holes).Schedule(dependencies),
#if UNITY_MATHEMATICS_FIXEDPOINT
                fp2 _ => new DeferredArraySupportInputJobFp2(positions, constraints, holes).Schedule(dependencies),
#endif
                _ => throw new NotImplementedException(),
            };

            dependencies = triangulator.Schedule(dependencies);
            dependencies.Complete();

            var expectedPositions = new float2[]
            {
                new(0, 0),
                new(4, 0),
                new(4, 4),
                new(0, 4),
                new(1, 1),
                new(3, 1),
                new(3, 3),
                new(1, 3),
            }.DynamicCast<T>();
            var expectedTriangles = new[]
            {
                4, 1, 0,
                4, 5, 1,
                5, 6, 1,
                0, 7, 4,
                7, 2, 6,
                6, 2, 1,
                0, 3, 7,
                7, 3, 2,
            };

            Assert.That(triangulator.Output.Positions.AsArray(), Is.EqualTo(expectedPositions));
            Assert.That(triangulator.Output.Triangles.AsArray(), Is.EqualTo(expectedTriangles).Using(TrianglesComparer.Instance));
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
            var nonLocalTriangles = triangulator.Output.Triangles.AsArray().ToArray();

            triangulator.Settings.Preprocessor = Preprocessor.COM;
            triangulator.Run();
            var localTriangles = triangulator.Output.Triangles.AsArray().ToArray();

            Assert.That(nonLocalTriangles, Has.Length.LessThanOrEqualTo(2 * 3));
            Assert.That(localTriangles, Is.EqualTo(new[] { 0, 3, 1, 3, 2, 1 }).Using(TrianglesComparer.Instance));
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
            var nonLocalTriangles = triangulator.Output.Triangles.AsArray().ToArray();

            triangulator.Settings.Preprocessor = Preprocessor.COM;
            triangulator.Run();
            var localTriangles = triangulator.Output.Triangles.AsArray().ToArray();

            var ratio = localTriangles.Intersect(nonLocalTriangles).Count() / (float)localTriangles.Length;
            Assert.That(ratio, Is.GreaterThan(0.80), message: "Only few triangles may be flipped.");
            Assert.That(localTriangles, Has.Length.EqualTo(nonLocalTriangles.Length));
        }

        [Test]
        public void LocalTransformationWithHolesTest()
        {
            var scaleFactor = typeof(T) == typeof(int2) ? 1000 : 0.1f;

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
            managedPositions = managedPositions.Select(x => scaleFactor * x + 5f).ToArray();

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
            var nonLocalTriangles = triangulator.Output.Triangles.AsArray().ToArray();

            triangulator.Settings.Preprocessor = Preprocessor.COM;
            triangulator.Run();
            var localTriangles = triangulator.Output.Triangles.AsArray().ToArray();

            Assert.That(localTriangles, Is.EqualTo(nonLocalTriangles).Using(TrianglesComparer.Instance));
            Assert.That(localTriangles, Has.Length.EqualTo(3 * 24));
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
                LogAssert.Expect(LogType.Error, new Regex("Sloan max iterations exceeded.*"));
            }

            triangulator.Run();

            Assert.That(triangulator.Output.Status.Value, Is.EqualTo(Status.ERR_SLOAN_ITERS_EXCEEDED));
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
            Assert.That(triangulator.Output.ConstrainedHalfedges.AsArray(), Is.EqualTo(new[]
            {
                false, false, false, false, false, false, false, false, false, false, false, false,
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
            Assert.That(triangulator.Output.ConstrainedHalfedges.AsArray(), Is.EqualTo(new[]
            {
                false, true, false, false, false, false, false, false, false, false, true, false,
            }));
        }

        [Test]
        public void HalfedgesForConstrainedDelaunayTriangulationWithHolesTest()
        {
            using var positions = new NativeArray<T>(new float2[]
            {
                math.float2(0, 0),
                math.float2(8, 0),
                math.float2(4, 8),
                math.float2(4, 4),
                math.float2(3, 2),
                math.float2(5, 2),
            }.DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(new[]
            {
                0, 1, 1, 2, 2, 0,
                3, 4, 4, 5, 5, 3,
            }, Allocator.Persistent);
            using var holes = new NativeArray<T>(new[] { math.float2(4, 3) }.DynamicCast<T>(), Allocator.Persistent);
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraints, HoleSeeds = holes }
            };

            triangulator.Run();

            Assert.That(triangulator.Output.Halfedges.AsArray(), Is.EqualTo(new[]
            {
                11, 3, -1, 1, 14, -1, 17, 9, -1, 7, -1, 0, -1, 15, 4, 13, -1, 6,
            }));
            Assert.That(triangulator.Output.ConstrainedHalfedges.AsArray(), Is.EqualTo(new[]
            {
                false, false, true, false, false, true,
                false, false, true, false, true, false,
                true, false, false, false, true, false,
            }));
        }

        [Test]
        public void AutoHolesTest()
        {
            var scaleFactor = typeof(T) == typeof(int2) ? 1000.0f : 1f;
            var scaledPoints = LakeSuperior.Points.Select(p => p * scaleFactor).ToArray();
            var scaledHoles = LakeSuperior.Holes.Select(h => h * scaleFactor).ToArray();
            using var positions = new NativeArray<T>(scaledPoints.DynamicCast<T>(), Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);
            using var holes = new NativeArray<T>(scaledHoles.DynamicCast<T>(), Allocator.Persistent);

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

            triangulator.Draw(color: Color.green);

            triangulator.Input.HoleSeeds = holes;
            triangulator.Settings.AutoHolesAndBoundary = false;
            triangulator.Settings.RestoreBoundary = true;
            triangulator.Run();

            var manualResult = triangulator.Output.Triangles.AsArray().ToArray();
            Assert.That(autoResult, Is.EqualTo(manualResult));
        }

        [Test]
        public void AutoHolesWithIgnoredConstraintsTest()
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
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
            {
                Input = {
                    Positions = positions,
                    ConstraintEdges = constraintEdges,
                    IgnoreConstraintForPlantingSeeds = ignoreConstraints
                },
                Settings = { AutoHolesAndBoundary = true, },
            };

            triangulator.Run();

            Assert.That(triangulator.Output.Triangles.AsArray(), Has.Length.EqualTo(3 * 12));
            Assert.That(triangulator.Output.ConstrainedHalfedges.AsArray().Count(i => i), Is.EqualTo(10));
            Assert.That(triangulator.Output.IgnoredHalfedgesForPlantingSeeds.AsArray().Count(i => i), Is.EqualTo(2));
        }

        [Test]
        public void AlphaShapeTest()
        {
            var alpha = 1e-5f;
            var count = 64;
            var points = new float2[count];
            var random = new Unity.Mathematics.Random(42);
            for (int i = 0; i < count; i++)
            {
                points[i] = random.NextInt2(0, 1000);
            }

            using var positions = new NativeArray<T>(points.DynamicCast<T>(), Allocator.Persistent);
            using var triangulator = new Triangulator<T>(Allocator.Persistent)
            {
                Input = { Positions = positions },
                Settings = { UseAlphaShapeFilter = true, AlphaShapeSettings = { Alpha = alpha } }
            };
            triangulator.Run();

            triangulator.Draw();

            var p = triangulator.Output.Positions.AsReadOnly().Select(i => i.ToFloat2()).ToArray();
            var t = triangulator.Output.Triangles.AsReadOnly().ToArray();
            static float R(float2 a, float2 b, float2 c) => math.distancesq(default(LowLevel.Unsafe.UtilsFloat).CircumCenter(a, b, c), a);
            var radius = new float[t.Length / 3];
            for (int i = 0; i < radius.Length; i++)
            {
                var (a, b, c) = (p[t[3 * i + 0]], p[t[3 * i + 1]], p[t[3 * i + 2]]);
                radius[i] = R(a, b, c);
            }

            Assert.That(radius, Has.All.LessThan(1 / alpha));
        }

        [Test]
        public void AlphaShapeOptionsTest([Values] bool protectPoints, [Values] bool protectConstraints, [Values] bool preventWindmills)
        {
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
                Settings = { UseAlphaShapeFilter = true, AlphaShapeSettings =
                    {
                        Alpha = alpha,
                        ProtectConstraints = protectConstraints,
                        PreventWindmills = preventWindmills,
                        ProtectPoints = protectPoints,
                    }
                }
            };
            triangulator.Run();

            triangulator.Draw();

            if (preventWindmills)
            {
                GenerateTriangleColors(colors: new int[triangulator.Output.Triangles.Length / 3], halfedges: triangulator.Output.Halfedges.AsReadOnly(), out var colorsCount, Allocator.Persistent);
                Assert.That(colorsCount, Is.EqualTo(1));
            }

            if (protectPoints)
            {
                var pointTriangleCount = new int[triangulator.Output.Positions.Length];
                GeneratePointTriangleCount(pointTriangleCount, triangulator.Output.Triangles.AsReadOnly());
                Assert.That(pointTriangleCount, Has.All.GreaterThan(0));
            }

            if (protectConstraints)
            {
                Assert.That(triangulator.Output.ConstrainedHalfedges.AsReadOnly().Count(i => i), Is.EqualTo(4));
            }
        }

        [Test]
        public void AlphaShapeWithPreprocessorTest()
        {
            var alpha = 1e-4f;
            var count = 64;
            var points = new float2[count];
            var random = new Unity.Mathematics.Random(42);
            for (int i = 0; i < count; i++)
            {
                points[i] = random.NextInt2(0, 1000);
            }

            int[] triangulate(Preprocessor preprocessor)
            {
                using var positions = new NativeArray<T>(points.DynamicCast<T>(), Allocator.Persistent);
                using var triangulator = new Triangulator<T>(Allocator.Persistent)
                {
                    Input = { Positions = positions },
                    Settings =
                    {
                        UseAlphaShapeFilter = true,
                        AlphaShapeSettings = { Alpha = alpha },
                        Preprocessor = preprocessor
                    }
                };
                triangulator.Run();

                return triangulator.Output.Triangles.AsReadOnly().ToArray();
            }

            var trianglesNone = triangulate(Preprocessor.None);
            var trianglesCOM = triangulate(Preprocessor.COM);

            TestUtils.Draw(points, trianglesNone, Color.red, duration: 5);
            TestUtils.Draw(points, trianglesCOM, Color.blue, duration: 5);

            Assert.That(trianglesNone, Is.EqualTo(trianglesCOM).Using(TrianglesComparer.Instance));
        }
    }

    [TestFixture(typeof(float2))]
    [TestFixture(typeof(Vector2))]
    [TestFixture(typeof(double2))]
#if UNITY_MATHEMATICS_FIXEDPOINT
    [TestFixture(typeof(fp2))]
#endif
    public class TriangulatorGenericsEditorTestsWithPCA<T> where T : unmanaged
    {
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
            //   3 --------------------- 2
            //   |                       |
            //   |                       |
            //   |                       |
            //   |     7 ----- 6         |
            //   |     |   X   |         |
            //   |     |       |         |
            //   |     4 ----- 5         |
            //   |                       |
            //   0 --------------------- 1
            using var positions = new NativeArray<T>(new[]
            {
                math.float2(0, 0), math.float2(6, 0), math.float2(6, 6), math.float2(0, 6),
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

            var expected = new[]
            {
                0, 3, 7,
                0, 4, 5,
                0, 5, 1,
                0, 7, 4,
                1, 5, 6,
                1, 6, 2,
                2, 6, 3,
                3, 6, 7,
            };
            Assert.That(triangulator.Output.Triangles.AsArray(), Is.EqualTo(expected).Using(TrianglesComparer.Instance));
        }
    }

    [TestFixture(typeof(float2))]
    [TestFixture(typeof(Vector2))]
    [TestFixture(typeof(double2))]
#if UNITY_MATHEMATICS_FIXEDPOINT
    [TestFixture(typeof(fp2))]
#endif
    public class TriangulatorGenericsEditorTestsWithRefinement<T> where T : unmanaged
    {
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
                5, 1, 0,
                5, 2, 4,
                5, 3, 2,
                5, 4, 1,
            };
            Assert.That(triangulator.Output.Positions.AsArray(), Is.EqualTo(expectedPositions));
            Assert.That(triangulator.Output.Triangles.AsArray(), Is.EqualTo(expectedTriangles).Using(TrianglesComparer.Instance));
        }

        private static readonly TestCaseData[] constraintDelaunayTriangulationWithRefinementTestData =
        {
            //  3 -------- 2
            //  | ' .      |
            //  |    ' .   |
            //  |       ' .|
            //  0 -------- 1
            new((
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
                    math.float2(0.5f, 1f),
                    math.float2(0.8189806f, 0.8189806f),
                    math.float2(0.1810194f, 0.1810194f),
                    math.float2(0.744f, 1f),
                    math.float2(1f, 0.744f),
                    math.float2(0.256f, 0f),
                    math.float2(0f, 0.256f),
                },
                new []
                {
                    6, 4, 5,
                    6, 5, 1,
                    8, 4, 7,
                    8, 7, 3,
                    9, 5, 4,
                    9, 4, 8,
                    10, 7, 4,
                    10, 4, 6,
                    11, 2, 9,
                    11, 9, 8,
                    12, 5, 9,
                    12, 9, 2,
                    13, 0, 10,
                    13, 10, 6,
                    14, 7, 10,
                    14, 10, 0,
                }
            )){ TestName = "Test case 1 (square)" },

            //  5 -------- 4 -------- 3
            //  |       . '|      . ' |
            //  |    . '   |   . '    |
            //  |. '      .|. '       |
            //  0 -------- 1 -------- 2
            new((
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
                },
                new []
                {
                    7, 0, 5,
                    7, 4, 6,
                    7, 5, 4,
                    7, 6, 1,
                    8, 1, 6,
                    8, 2, 1,
                    8, 3, 2,
                    8, 6, 4,
                    9, 0, 7,
                    9, 7, 1,
                    10, 3, 8,
                    10, 8, 4,
                }
            )){ TestName = "Test case 2 (rectangle)" },
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
                },
                triangles: new[]
                {
                    0, 4, 3,
                    5, 0, 3,
                    5, 1, 0,
                    6, 3, 2,
                    6, 5, 3,
                }
            )){ TestName = "Test case 3 (strange box)" },
        };

        [Test, TestCaseSource(nameof(constraintDelaunayTriangulationWithRefinementTestData))]
        public void ConstraintDelaunayTriangulationWithRefinementTest((float2[] managedPositions, int[] constraints, float2[] insertedPoints, int[] triangles) input)
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
            Assert.That(triangulator.Output.Triangles.AsArray(), Is.EqualTo(input.triangles).Using(TrianglesComparer.Instance));
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
                2, 1, 3,
                3, 0, 4,
                5, 0, 3,
                5, 3, 1,
            };
            Assert.That(triangulator.Output.Positions.AsArray(), Is.EqualTo(expectedPositions));
            Assert.That(triangulator.Output.Triangles.AsArray(), Is.EqualTo(expectedTriangles).Using(TrianglesComparer.Instance));
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
                8, 0, 4,
                8, 4, 5,
                8, 5, 1,
                9, 1, 5,
                9, 5, 6,
                9, 6, 2,
                10, 3, 7,
                10, 4, 0,
                10, 7, 4,
                11, 2, 6,
                11, 6, 7,
                11, 7, 3,
            };

            Assert.That(triangulator.Output.Positions.AsArray(), Is.EquivalentTo(expectedPositions));
            Assert.That(triangulator.Output.Triangles.AsArray(), Is.EqualTo(expectedTriangles).Using(TrianglesComparer.Instance));
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

            var trianglesWithConstraints = triangulator.Output.Triangles.AsArray().ToArray();

            triangulator.Input.ConstraintEdges = default;
            triangulator.Run();
            var trianglesWithoutConstraints = triangulator.Output.Triangles.AsArray().ToArray();

            Assert.That(trianglesWithConstraints, Is.EqualTo(trianglesWithoutConstraints));
        }

        [Test(Description = "Checks if triangulator passes for `very` accute angle input")]
        public void AccuteInputAngleTest()
        {
            using var positions = new NativeArray<T>(new[] {
                math.float2(0, 0),
                math.float2(10, 0),
                math.float2(10, 1f),
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
                        Area = 100f,
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
            Assert.That(triangulator.Output.ConstrainedHalfedges.AsArray(), Is.EqualTo(new[]
            {
                // without edge constraints, only boundary halfedges are constrained!
                true, true, false, true, true, false,
                false, false, false, false, true, true,
            }));
        }

        [Test]
        public void ConstraintsArePresentWithRefinementButWithoutHolesTest()
        {
#if UNITY_MATHEMATICS_FIXEDPOINT
            if (typeof(T) == typeof(fp2))
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
            using var holes = new NativeArray<T>(LakeSuperior.Holes.DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);

            using var triangulator = new Triangulator<T>(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraints, HoleSeeds = default },
                Settings = { RefineMesh = true, Preprocessor = Preprocessor.None, ValidateInput = true }
            };

            triangulator.Run();

            var triangles = triangulator.Output.Triangles;
            var constrainedHalfedges = triangulator.Output.ConstrainedHalfedges;
            var visited = new bool[triangulator.Output.Positions.Length];
            for (int he = 0; he < constrainedHalfedges.Length; he++)
            {
                if (constrainedHalfedges[he])
                {
                    visited[triangles[he]] = visited[triangles[NextHalfedge(he)]] = true;
                }
            }
            // Naive check if all points are "visited".
            Assert.That(visited[..LakeSuperior.Points.Length], Has.All.True);
        }

        [Test]
        public void RefinementWithNeedleConstraintClusterTest()
        {
            using var positions = new NativeArray<T>(new[] {
                math.float2(0, 0),
                math.float2(9, 0),
                math.float2(9, 7),
                math.float2(-1, 7),
                math.float2(-1, .2f),
            }.DynamicCast<T>(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(new[] { 0, 1, 1, 4, }, Allocator.Persistent);

            using var triangulator = new Triangulator<T>(Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraints, HoleSeeds = default },
                Settings = { RefineMesh = true, Preprocessor = Preprocessor.None, ValidateInput = true, RefinementThresholds = { Area = 10, Angle = math.radians(25) } }
            };

            triangulator.Run();
            triangulator.Draw(color: Color.blue);

            var result = triangulator.Output.Positions.AsReadOnly().CastToFloat2().Select(i => math.all(i >= math.float2(-1, 0) & i <= math.float2(9, 7)));
            Assert.That(result, Has.All.True);
            TestUtils.AssertValidTriangulation(triangulator);
        }
    }
}
