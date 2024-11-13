using NUnit.Framework;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
#if UNITY_MATHEMATICS_FIXEDPOINT
using Unity.Mathematics.FixedPoint;
#endif

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    [Explicit, Category("Benchmark")]
    public class TriangulatorBenchmarkTests
    {
        static TestCaseData DelaunayCase(int count, int N) => new((count: count, N: N))
        {
            TestName = $"Points: {count * count} (N: {N})"
        };
        private static readonly TestCaseData[] delaunayBenchmarkTestData =
        {
            DelaunayCase(count: 10, N: 1000),
            DelaunayCase(count: 20, N: 1000),
            DelaunayCase(count: 31, N: 1000),
            DelaunayCase(count: 50, N: 100),
            DelaunayCase(count: 100, N: 100),
            DelaunayCase(count: 200, N: 100),
            DelaunayCase(count: 300, N: 100),
            DelaunayCase(count: 400, N: 100),
            DelaunayCase(count: 500, N: 100),
            DelaunayCase(count: 600, N: 10),
            DelaunayCase(count: 700, N: 10),
            DelaunayCase(count: 800, N: 10),
            DelaunayCase(count: 900, N: 10),
            DelaunayCase(count: 1000, N: 10),
        };

        [Test, TestCaseSource(nameof(delaunayBenchmarkTestData))]
        public void DelaunayBenchmarkFloat2Test((int count, int N) input)
        {
            var (count, N) = input;
            var debuggerInitialValue = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled;
            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = false;

            var points = new List<float2>(count * count);
            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    var p = math.float2(i / (float)(count - 1), j / (float)(count - 1));
                    points.Add(p);
                }
            }

            using var positions = new NativeArray<float2>(points.ToArray(), Allocator.Persistent);

            var stopwatch = Stopwatch.StartNew();
            using var triangulator = new Triangulator<float2>(capacity: count * count, Allocator.Persistent)
            {
                Input = { Positions = positions },
                Settings = {
                    RefineMesh = false,
                    RestoreBoundary = false,
                    ValidateInput = false,
                },
            };

            var dependencies = default(JobHandle);
            for (int i = 0; i < N; i++) dependencies = triangulator.Schedule(dependencies);
            dependencies.Complete();
            stopwatch.Stop();
            UnityEngine.Debug.Log($"{count * count} {stopwatch.Elapsed.TotalMilliseconds / N}");

            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = debuggerInitialValue;
        }

        private static readonly TestCaseData[] delaunayBenchmarkDouble2TestData = delaunayBenchmarkTestData
            .Select(i => new TestCaseData(i.Arguments) { TestName = i.TestName + " (double2)" })
            .ToArray();

        [Test, TestCaseSource(nameof(delaunayBenchmarkDouble2TestData))]
        public void DelaunayBenchmarkDouble2Test((int count, int N) input)
        {
            var (count, N) = input;
            var debuggerInitialValue = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled;
            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = false;

            var points = new List<double2>(count * count);
            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    var p = math.float2(i / (float)(count - 1), j / (float)(count - 1));
                    points.Add(p);
                }
            }

            using var positions = new NativeArray<double2>(points.ToArray(), Allocator.Persistent);

            var stopwatch = Stopwatch.StartNew();
            using var triangulator = new Triangulator<double2>(capacity: count * count, Allocator.Persistent)
            {
                Input = { Positions = positions },
                Settings = {
                    RefineMesh = false,
                    RestoreBoundary = false,
                    ValidateInput = false,
                },
            };

            var dependencies = default(JobHandle);
            for (int i = 0; i < N; i++) dependencies = triangulator.Schedule(dependencies);
            dependencies.Complete();
            stopwatch.Stop();
            UnityEngine.Debug.Log($"{count * count} {stopwatch.Elapsed.TotalMilliseconds / N}");

            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = debuggerInitialValue;
        }

        private static readonly TestCaseData[] delaunayBenchmarkInt2TestData = delaunayBenchmarkTestData
            .Select(i => new TestCaseData(i.Arguments) { TestName = i.TestName + " (int2)" })
            .ToArray();

        [Test, TestCaseSource(nameof(delaunayBenchmarkInt2TestData))]
        public void DelaunayBenchmarkInt2Test((int count, int N) input)
        {
            var (count, N) = input;
            var debuggerInitialValue = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled;
            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = false;

            var points = new List<int2>(count * count);
            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    var p = math.int2(i, j);
                    points.Add(p);
                }
            }

            using var positions = new NativeArray<int2>(points.ToArray(), Allocator.Persistent);

            var stopwatch = Stopwatch.StartNew();
            using var triangulator = new Triangulator<int2>(capacity: count * count, Allocator.Persistent)
            {
                Input = { Positions = positions },
                Settings = {
                    RefineMesh = false,
                    RestoreBoundary = false,
                    ValidateInput = false,
                },
            };

            var dependencies = default(JobHandle);
            for (int i = 0; i < N; i++) dependencies = triangulator.Schedule(dependencies);
            dependencies.Complete();
            stopwatch.Stop();
            UnityEngine.Debug.Log($"{count * count} {stopwatch.Elapsed.TotalMilliseconds / N}");

            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = debuggerInitialValue;
        }

#if UNITY_MATHEMATICS_FIXEDPOINT
        private static readonly TestCaseData[] delaunayBenchmarkFp2TestData = delaunayBenchmarkTestData
            .Select(i => new TestCaseData(i.Arguments) { TestName = i.TestName + " (fp2)" })
            .ToArray();

        [Test, TestCaseSource(nameof(delaunayBenchmarkFp2TestData))]
        public void DelaunayBenchmarkFp2Test((int count, int N) input)
        {
            var (count, N) = input;
            var debuggerInitialValue = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled;
            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = false;

            var points = new List<fp2>(count * count);
            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    var p = fpmath.fp2(i / (fp)(count - 1), j / (fp)(count - 1));
                    points.Add(p);
                }
            }

            using var positions = new NativeArray<fp2>(points.ToArray(), Allocator.Persistent);

            var stopwatch = Stopwatch.StartNew();
            using var triangulator = new Triangulator<fp2>(capacity: count * count, Allocator.Persistent)
            {
                Input = { Positions = positions },
                Settings = {
                    RefineMesh = false,
                    RestoreBoundary = false,
                    ValidateInput = false,
                },
            };

            var dependencies = default(JobHandle);
            for (int i = 0; i < N; i++) dependencies = triangulator.Schedule(dependencies);
            dependencies.Complete();
            stopwatch.Stop();
            UnityEngine.Debug.Log($"{count * count} {stopwatch.Elapsed.TotalMilliseconds / N}");

            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = debuggerInitialValue;
        }
#endif

        private static readonly TestCaseData[] constraintBenchmarkTestData = Enumerable
            .Range(0, 80)
            .Select(i => new TestCaseData((100, 3 * (i + 1))))
            .ToArray();

        [Test, TestCaseSource(nameof(constraintBenchmarkTestData))]
        public void ConstraintBenchmarkTest((int count, int N) input)
        {
            var (count, N) = input;
            var debuggerInitialValue = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled;
            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = false;

            var points = new List<float2>(count * count);
            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    var p = math.float2(i / (float)(count - 1), j / (float)(count - 1));
                    points.Add(p);
                }
            }

            var offset = points.Count;
            var constraints = new List<int>(N + 1);
            for (int i = 0; i < N; i++)
            {
                var phi = 2 * math.PI / N * i + 0.1452f;
                var p = 0.2f * math.float2(math.cos(phi), math.sin(phi)) + 0.5f;
                points.Add(p);
                constraints.Add(offset + i);
                constraints.Add(offset + (i + 1) % N);
            }

            using var positions = new NativeArray<float2>(points.ToArray(), Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(constraints.ToArray(), Allocator.Persistent);

            var stopwatch = Stopwatch.StartNew();
            using var triangulator = new Triangulator<float2>(capacity: count * count + N, Allocator.Persistent)
            {
                Input = { Positions = positions, ConstraintEdges = constraintEdges },
                Settings = {
                    RefineMesh = false,
                    RestoreBoundary = false,
                    ValidateInput = false
                },
            };

            var dependencies = default(JobHandle);
            var rep = 300;
            for (int i = 0; i < rep; i++) dependencies = triangulator.Schedule(dependencies);
            dependencies.Complete();
            stopwatch.Stop();
            var log = $"{N} {stopwatch.Elapsed.TotalMilliseconds / rep}";
            UnityEngine.Debug.Log(log);

            // NOTE: Uncomment this to write all the test cases into a file.
            //using var writer = new System.IO.StreamWriter("tmp.txt", true);
            //writer.WriteLine(log);

            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = debuggerInitialValue;
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
            new((area: 0.0030f, N: 100)),
            new((area: 0.0010f, N: 100)),
            new((area: 0.0007f, N: 010)),
            new((area: 0.0005f, N: 010)),
            new((area: 0.0004f, N: 010)),
            new((area: 0.0003f, N: 010)),
            new((area: 0.0002f, N: 005)),
        };

        [Test, TestCaseSource(nameof(refineMeshBenchmarkTestData))]
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
            using var triangulator = new Triangulator<float2>(capacity: 64 * 1024, Allocator.Persistent)
            {
                Input = { Positions = points },
                Settings = {
                    RefineMesh = true,
                    RestoreBoundary = false,
                    ValidateInput = false,
                    RefinementThresholds = { Area = area },
                },
            };

            var dependencies = default(JobHandle);
            for (int i = 0; i < N; i++) dependencies = triangulator.Schedule(dependencies);
            dependencies.Complete();
            stopwatch.Stop();
            UnityEngine.Debug.Log($"{triangulator.Output.Triangles.Length} {stopwatch.Elapsed.TotalMilliseconds / N}");

            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = debuggerInitialValue;
        }

        private static readonly TestCaseData[] refineMeshLakeBenchmarkTestData =
        {
            new(00.00f, 100),
            new(05.00f, 100),
            new(10.00f, 100),
            new(15.00f, 100),
            new(20.00f, 100),
            new(25.00f, 100),
            new(28.00f, 100),
            new(30.00f, 100),
            new(31.50f, 100),
            new(33.00f, 100),
            new(33.50f, 100),
            new(33.70f, 100),
            new(33.72f, 100),
            new(33.73f, 100),
            //new(33.75f, 1), // Limit
        };

        [Test, TestCaseSource(nameof(refineMeshLakeBenchmarkTestData))]
        public void RefineMeshLakeBenchmarkTest(float angle, int N)
        {
            var debuggerInitialValue = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled;
            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = false;

            using var positions = new NativeArray<float2>(LakeSuperior.Points, Allocator.Persistent);
            using var constraintEdges = new NativeArray<int>(LakeSuperior.Constraints, Allocator.Persistent);
            using var holeSeeds = new NativeArray<float2>(LakeSuperior.Holes, Allocator.Persistent);
            using var triangulator = new Triangulator<float2>(1024 * 1024, Allocator.Persistent)
            {
                Settings =
                {
                    ValidateInput = false,
                    RefineMesh = true,
                    RestoreBoundary = true,
                    RefinementThresholds =
                    {
                        Area = 100f,
                        Angle = math.radians(angle),
                    }
                },
                Input =
                {
                    Positions = positions,
                    ConstraintEdges = constraintEdges,
                    HoleSeeds = holeSeeds,
                }
            };

            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < N; i++)
            {
                triangulator.Run();
            }
            stopwatch.Stop();

            triangulator.Draw();

            UnityEngine.Debug.Log($"{triangulator.Output.Triangles.Length / 3} {stopwatch.Elapsed.TotalMilliseconds / N}");

            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = debuggerInitialValue;
        }

        private static TestCaseData PlantingHolesCase(int n, int N) => new((n, N)) { TestName = $"Holes: {n * n} (N: {N})" };

        private static readonly TestCaseData[] plantingAutoHolesBenchmarkTestData =
        {
            PlantingHolesCase(1, 10000),
            PlantingHolesCase(2, 10000),
            PlantingHolesCase(3,10000),
            PlantingHolesCase(4, 5000),
            PlantingHolesCase(5, 5000),
            PlantingHolesCase(7, 5000),
            PlantingHolesCase(10, 2000),
            PlantingHolesCase(15, 2000),
            PlantingHolesCase(20, 2000),
            PlantingHolesCase(25, 2000),
            PlantingHolesCase(30, 2000),
            PlantingHolesCase(35, 2000),
            PlantingHolesCase(40, 2000),
            PlantingHolesCase(45, 2000),
            PlantingHolesCase(50, 2000),
        };

        [Test, TestCaseSource(nameof(plantingAutoHolesBenchmarkTestData))]
        public void PlantingAutoHolesBenchmarkTest((int n, int N) input)
        {
            var (n, N) = input;
            var debuggerInitialValue = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled;
            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = false;

            var points = new List<double2>();
            var managedConstraints = new List<int>();

            var dx = 11f / (n + 1);
            for (int i = 0; i < n + 1; i++)
            {
                points.Add(math.double2(i * dx, 0));
            }

            for (int i = 0; i < n + 1; i++)
            {
                points.Add(math.double2(11, i * dx));
            }

            for (int i = 0; i < n + 1; i++)
            {
                points.Add(math.double2(11 - i * dx, 11));
            }

            for (int i = 0; i < n + 1; i++)
            {
                points.Add(math.double2(0, 11 - i * dx));
            }

            for (int i = 0; i < points.Count - 1; i++)
            {
                managedConstraints.Add(i);
                managedConstraints.Add(i + 1);
            }
            managedConstraints.Add(points.Count - 1);
            managedConstraints.Add(0);

            dx = 11f / (2 * n + 1);
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    var c0 = points.Count;
                    points.Add(math.double2(2 * i + 1, 2 * j + 1) * dx);
                    points.Add(math.double2(2 * i + 2, 2 * j + 1) * dx);
                    points.Add(math.double2(2 * i + 2, 2 * j + 2) * dx);
                    points.Add(math.double2(2 * i + 1, 2 * j + 2) * dx);
                    managedConstraints.Add(c0);
                    managedConstraints.Add(c0 + 1);
                    managedConstraints.Add(c0 + 1);
                    managedConstraints.Add(c0 + 2);
                    managedConstraints.Add(c0 + 2);
                    managedConstraints.Add(c0 + 3);
                    managedConstraints.Add(c0 + 3);
                    managedConstraints.Add(c0 + 0);
                }
            }

            using var positions = new NativeArray<double2>(points.ToArray(), Allocator.Persistent);
            using var constraints = new NativeArray<int>(managedConstraints.ToArray(), Allocator.Persistent);
            var stopwatch = Stopwatch.StartNew();
            using var triangulator = new Triangulator<double2>(capacity: 2 * positions.Length, Allocator.Persistent)
            {
                Input = {
                    Positions = positions,
                    ConstraintEdges = constraints,
                },
                Settings = {
                    AutoHolesAndBoundary = true,
                    RefineMesh = false,
                    RestoreBoundary = false,
                    ValidateInput = false,
                },
            };

            var dependencies = default(JobHandle);
            for (int i = 0; i < N; i++) dependencies = triangulator.Schedule(dependencies);
            dependencies.Complete();
            stopwatch.Stop();
            UnityEngine.Debug.Log($"{n * n} {stopwatch.Elapsed.TotalMilliseconds / N}");

            Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = debuggerInitialValue;
        }
    }
}