using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools.Utils;
using andywiecko.BurstTriangulator.LowLevel.Unsafe;

#if UNITY_MATHEMATICS_FIXEDPOINT
using Unity.Mathematics.FixedPoint;
#endif

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    public class TrianglesComparer : IEqualityComparer<IEnumerable<int>>
    {
        public static readonly TrianglesComparer Instance = new();

        public int MaxErrorLogElements = 8;

        private readonly struct Int3Comparer : IComparer<int3>
        {
            public readonly int Compare(int3 a, int3 b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y != b.y ? a.y.CompareTo(b.y) : a.z.CompareTo(b.z);
        }

        private readonly struct Int3print
        {
            private readonly int3 t;
            public Int3print(int3 t) => this.t = t;
            public override string ToString() => $"({t.x}, {t.y}, {t.z})";
        }

        public bool Equals(IEnumerable<int> expected, IEnumerable<int> actual)
        {
            if (actual.Count() % 3 != 0)
            {
                throw new AssertionException("Actual's length is not modulo 3!");
            }

            if (expected.Count() % 3 != 0)
            {
                throw new AssertionException("Expected's length is not modulo 3!");
            }

            using var expectedNative = new NativeArray<int>(expected.ToArray(), Allocator.Persistent).Reinterpret<int3>(4);
            using var actualNative = new NativeArray<int>(actual.ToArray(), Allocator.Persistent).Reinterpret<int3>(4);

            static NativeArray<int3> sort(NativeArray<int3> t)
            {
                for (int i = 0; i < t.Length; i++)
                {
                    var min = math.cmin(t[i]);
                    t[i] = min switch
                    {
                        _ when min == t[i].x => t[i].xyz,
                        _ when min == t[i].y => t[i].yzx,
                        _ when min == t[i].z => t[i].zxy,
                        _ => throw new Exception(),
                    };
                }

                t.Sort(default(Int3Comparer));
                return t;
            }

            if (Enumerable.SequenceEqual(sort(expectedNative), sort(actualNative)))
            {
                return true;
            }

            string print(ReadOnlySpan<Int3print> triangles)
            {
                static string join<T>(string sep, ReadOnlySpan<T> span)
                {
                    if (span.Length == 0)
                    {
                        return "";
                    }
                    var b = new StringBuilder();
                    b.Append(span[0]);
                    foreach (var s in span[1..])
                    {
                        b.Append($"{sep}{s}");
                    }
                    return b.ToString();
                }

                var trimmed = triangles.Length > MaxErrorLogElements;
                var trim = trimmed ? MaxErrorLogElements : triangles.Length;
                var dots = trimmed ? ", ..." : "";
                return $"<▲[{triangles.Length}]: {join(", ", triangles[..trim])}{dots}>";
            }

            var missing = expectedNative.Except(actualNative).Select(i => new Int3print(i)).ToArray();
            var extra = actualNative.Except(expectedNative).Select(i => new Int3print(i)).ToArray();
            var details = string.Join("",
                extra.Length > 0 ? $"\n  Extra: {print(extra)}" : "",
                missing.Length > 0 ? $"\n  Missing: {print(missing)}" : ""
            );

            throw new AssertionException($"Expected is {print(expectedNative.Reinterpret<Int3print>())}, actual is {print(actualNative.Reinterpret<Int3print>())}.{details}");
        }

        public int GetHashCode(IEnumerable<int> obj) => default;
    }

    public class TrianglesComparerTests
    {
        private static void AssertThrowsAndLog(TestDelegate test) => Debug.Log(Assert.Throws<AssertionException>(test).Message);
        [Test] public void LogExpectedLengthNotMod3Test() => AssertThrowsAndLog(() => Assert.That(new[] { 0, 1, 2 }, Is.EqualTo(new[] { 0 }).Using(TrianglesComparer.Instance)));
        [Test] public void LogActualLengthNotMod3Test() => AssertThrowsAndLog(() => Assert.That(new[] { 0, 1, }, Is.EqualTo(new[] { 0 }).Using(TrianglesComparer.Instance)));
        [Test] public void LogDifferentTrianglesSameLengthTest() => AssertThrowsAndLog(() => Assert.That(new[] { 0, 1, 2 }, Is.EqualTo(new[] { 3, 4, 5 }).Using(TrianglesComparer.Instance)));
        [Test] public void LogDifferentTrianglesSameLengthWithDotsTest() => AssertThrowsAndLog(() => Assert.That(new[] { 0, 1, 2, 3, 4, 5 }, Is.EqualTo(new[] { 6, 7, 8, 9, 10, 11 }).Using(new TrianglesComparer() { MaxErrorLogElements = 1 })));
        [Test] public void LogDifferentTrianglesSameLengthWithDotsDefaultsTest() => AssertThrowsAndLog(() => Assert.That(Enumerable.Range(0, 32 * 3), Is.EqualTo(Enumerable.Range(32, 32 * 3)).Using(TrianglesComparer.Instance)));
        [Test] public void LogDifferentTrianglesMissingTrianglesTest() => AssertThrowsAndLog(() => Assert.That(new[] { 0, 1, 2 }, Is.EqualTo(new[] { 3, 4, 5, 6, 7, 8 }).Using(TrianglesComparer.Instance)));
        [Test] public void LogDifferentTrianglesExtraTrianglesTest() => AssertThrowsAndLog(() => Assert.That(new[] { 3, 4, 5, 6, 7, 8 }, Is.EqualTo(new[] { 0, 1, 2 }).Using(TrianglesComparer.Instance)));
        [Test] public void TrianglesInnerIndicesShuffledTest() => Assert.That(new[] { 0, 1, 2, 3, 4, 5 }, Is.EqualTo(new[] { 1, 2, 0, 5, 3, 4 }).Using(TrianglesComparer.Instance));
        [Test] public void TrianglesOuterIndicesShuffledTest() => Assert.That(new[] { 0, 1, 2, 3, 4, 5 }, Is.EqualTo(new[] { 3, 4, 5, 0, 1, 2 }).Using(TrianglesComparer.Instance));
        [Test] public void TrianglesInnerAndOuterIndicesShuffledTest() => Assert.That(new[] { 0, 1, 2, 3, 4, 5 }, Is.EqualTo(new[] { 5, 3, 4, 2, 0, 1 }).Using(TrianglesComparer.Instance));
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

    public class Double2Comparer : IEqualityComparer<double2>
    {
        private readonly double epsilon;
        public static readonly Double2Comparer Instance = new(0.0001f);
        public Double2Comparer(double epsilon) => this.epsilon = epsilon;
        public static Double2Comparer With(double epsilon) => new(epsilon);
        public bool Equals(double2 expected, double2 actual) => Equals(expected.x, actual.x) && Equals(expected.y, actual.y);
        public int GetHashCode(double2 _) => 0;

        private bool Equals(double expected, double actual)
        {
            if (expected == double.PositiveInfinity || actual == double.PositiveInfinity || expected == double.NegativeInfinity || actual == double.NegativeInfinity)
                return expected == actual;

            return math.abs(actual - expected) <= epsilon * math.max(math.max(math.abs(actual), math.abs(expected)), 1.0f);
        }
    }

#if UNITY_MATHEMATICS_FIXEDPOINT
    public class Fp2Comparer : IEqualityComparer<fp2>
    {
        private readonly fp epsilon;
        public static readonly Fp2Comparer Instance = new((fp)0.0001f);
        public Fp2Comparer(fp epsilon) => this.epsilon = epsilon;
        public static Fp2Comparer With(float epsilon) => new((fp)epsilon);
        public bool Equals(fp2 expected, fp2 actual) => Equals(expected.x, actual.x) && Equals(expected.y, actual.y);
        public int GetHashCode(fp2 _) => 0;
        private bool Equals(fp expected, fp actual) => fpmath.abs(actual - expected) <= epsilon * fpmath.max(fpmath.max(fpmath.abs(actual), fpmath.abs(expected)), (fp)1.0f);
    }
#endif

    public static class TestExtensions
    {
#if UNITY_MATHEMATICS_FIXEDPOINT
        // TODO: add cast in the fork repo.
        public static fp2 ToFp2(this float2 v) => new((fp)v.x, (fp)v.y);
#endif
        public static float2 ToFloat2<T2>(this T2 v) where T2 : unmanaged => default(T2) switch
        {
#if UNITY_MATHEMATICS_FIXEDPOINT
            fp2 => math.float2((float)((dynamic)v).x, (float)((dynamic)v).y),
#endif
            _ => (float2)(dynamic)v,
        };
        public static void AlphaShapeFilter<T>(this UnsafeTriangulator<T> triangulator, NativeOutputData<T> output, Allocator allocator, float alpha = 1, bool protectPoints = false, bool preventWindmills = false, bool protectConstraints = false) where T : unmanaged => LowLevel.Unsafe.Extensions.AlphaShapeFilter((dynamic)triangulator, (dynamic)output, allocator, default(T) switch
        {
#if UNITY_MATHEMATICS_FIXEDPOINT
            fp2 => (fp)alpha,
#endif
            _ => (dynamic)alpha,
        }
        , protectPoints, preventWindmills, protectConstraints);
        public static void Triangulate<T>(this UnsafeTriangulator<T> triangulator, NativeInputData<T> input, NativeOutputData<T> output, Args args, Allocator allocator) where T : unmanaged => LowLevel.Unsafe.Extensions.Triangulate((dynamic)triangulator, (dynamic)input, (dynamic)output, args, allocator);
        public static void ConstrainEdge<T>(this UnsafeTriangulator<T> triangulator, NativeOutputData<T> output, int pi, int pj, Args args, Allocator allocator, bool ignoreForPlantingSeeds = false) where T : unmanaged => LowLevel.Unsafe.Extensions.ConstrainEdge((dynamic)triangulator, (dynamic)output, pi, pj, args, allocator, ignoreForPlantingSeeds);
        public static void PlantHoleSeeds<T>(this UnsafeTriangulator<T> triangulator, NativeInputData<T> input, NativeOutputData<T> output, Args args, Allocator allocator) where T : unmanaged => LowLevel.Unsafe.Extensions.PlantHoleSeeds((dynamic)triangulator, (dynamic)input, (dynamic)output, args, allocator);
        public static void DynamicInsertPoint<T2, T3>(this UnsafeTriangulator<T2> triangulator, NativeOutputData<T2> output, int tId, T3 bar, Allocator allocator) where T2 : unmanaged where T3 : unmanaged => LowLevel.Unsafe.Extensions.DynamicInsertPoint((dynamic)triangulator, (dynamic)output, tId, default(T2) switch
        {
#if UNITY_MATHEMATICS_FIXEDPOINT
            fp2 => new fp3((fp)((dynamic)bar).x, (fp)((dynamic)bar).y, (fp)((dynamic)bar).z),
#endif
            _ => (dynamic)bar
        },
            allocator);
        public static void DynamicSplitHalfedge<T2>(this UnsafeTriangulator<T2> triangulator, NativeOutputData<T2> output, int he, float alpha, Allocator allocator) where T2 : unmanaged => LowLevel.Unsafe.Extensions.DynamicSplitHalfedge((dynamic)triangulator, (dynamic)output, he, default(T2) switch
        {
#if UNITY_MATHEMATICS_FIXEDPOINT
            fp2 => (fp)alpha,
#endif
            _ => (dynamic)alpha,
        }, allocator);
        public static void DynamicRemoveBulkPoint<T>(this UnsafeTriangulator<T> triangulator, NativeOutputData<T> output, int pId, Allocator allocator) where T : unmanaged => LowLevel.Unsafe.Extensions.DynamicRemoveBulkPoint((dynamic)triangulator, (dynamic)output, pId, allocator);
        public static void Run<T>(this Triangulator<T> triangulator) where T : unmanaged =>
            Extensions.Run((dynamic)triangulator);
        public static JobHandle Schedule<T>(this Triangulator<T> triangulator, JobHandle dependencies = default) where T : unmanaged =>
            Extensions.Schedule((dynamic)triangulator, dependencies);
        public static float3 NextBarcoords3(ref this Unity.Mathematics.Random random)
        {
            var bx = random.NextFloat();
            var by = random.NextFloat();
            if (bx + by > 1)
            {
                bx = 1 - bx;
                by = 1 - by;
            }
            var bz = 1 - bx - by;
            return new(bx, by, bz);
        }
        public static float2[] CastToFloat2<T>(this IEnumerable<T> data) where T : unmanaged => data.Select(i => i.ToFloat2()).ToArray();
        public static T[] DynamicCast<T>(this IEnumerable<float2> data) where T : unmanaged => default(T) switch
        {
#if UNITY_MATHEMATICS_FIXEDPOINT
            fp2 => data.Select(i => (T)(dynamic)i.ToFp2()).ToArray(),
#endif
            _ => data.Select(i => (T)(dynamic)i).ToArray()
        };
        public static IEnumerable<float2> Scale(this IEnumerable<float2> data, float s, bool predicate) => data.Select(i => (predicate ? s : 1f) * i);
        public static T[] DynamicCast<T>(this IEnumerable<double2> data) where T : unmanaged => default(T) switch
        {
            // TODO: this extension can be removed entirely.
            Vector2 => data.Select(i => (T)(dynamic)(float2)i).ToArray(),
#if UNITY_MATHEMATICS_FIXEDPOINT
            fp2 => data.Select(i => (T)(dynamic)((float2)i).ToFp2()).ToArray(),
#endif
            _ => data.Select(i => (T)(dynamic)i).ToArray()
        };
        public static IEqualityComparer<T> Comparer<T>(float epsilon = 0.0001f) => default(T) switch
        {
            float2 => Float2Comparer.With(epsilon) as IEqualityComparer<T>,
            double2 => Double2Comparer.With(epsilon) as IEqualityComparer<T>,
#if UNITY_MATHEMATICS_FIXEDPOINT
            fp2 => Fp2Comparer.With(epsilon) as IEqualityComparer<T>,
#endif
            Vector2 => new Vector2EqualityComparer(epsilon) as IEqualityComparer<T>,
            _ => throw new NotImplementedException()
        };
        public static void Draw(this Triangulator triangulator, Color? color = null, float duration = 5f) => TestUtils.Draw(triangulator.Output.Positions.AsArray().Select(i => (float2)i).ToArray(), triangulator.Output.Triangles.AsArray().AsReadOnlySpan(), color ?? Color.red, duration);
        public static void Draw<T>(this Triangulator<T> triangulator, Color? color = null, float duration = 5f) where T : unmanaged => TestUtils.Draw(
                triangulator.Output.Positions.AsArray().CastToFloat2(),
                triangulator.Output.Triangles.AsArray().AsReadOnlySpan(), color ?? Color.red, duration
        );
    }

    public static class TestUtils
    {
        /// <summary>
        /// Asserts that all triangles are oriented clockwise and do not intersect with one another.
        /// </summary>
        /// <exception cref="AssertionException"></exception>
        public static void AssertValidTriangulation<T2>(Triangulator<T2> triangulator) where T2 : unmanaged => AssertValidTriangulation(
            positions: triangulator.Output.Positions.AsReadOnly().CastToFloat2(),
            triangles: triangulator.Output.Triangles.AsReadOnly().AsReadOnlySpan()
        );

        /// <summary>
        /// Asserts that all triangles are oriented clockwise and do not intersect with one another.
        /// </summary>
        /// <exception cref="AssertionException"></exception>
        public static void AssertValidTriangulation(Triangulator triangulator) => AssertValidTriangulation(
            positions: triangulator.Output.Positions.AsReadOnly().Select(i => (float2)i).ToArray(),
            triangles: triangulator.Output.Triangles.AsReadOnly().AsReadOnlySpan()
        );

        /// <summary>
        /// Asserts that all triangles are oriented clockwise and do not intersect with one another.
        /// </summary>
        /// <exception cref="AssertionException"></exception>
        public static void AssertValidTriangulation(Mesh mesh, Axis axis = Axis.XY) => AssertValidTriangulation(
            positions: mesh.vertices.Select(i =>
            {
                var p = (float3)i;
                return axis switch
                {
                    Axis.XY => p.xy,
                    Axis.XZ => p.xz,
                    Axis.YX => p.yx,
                    Axis.YZ => p.yx,
                    Axis.ZX => p.zx,
                    Axis.ZY => p.zy,
                    _ => throw new(),
                };
            }).ToArray(),
            triangles: mesh.triangles
        );

        /// <summary>
        /// Asserts that all <paramref name="triangles"/> are oriented clockwise and do not intersect with one another.
        /// </summary>
        /// <exception cref="AssertionException"></exception>
        public static void AssertValidTriangulation(ReadOnlySpan<float2> positions, ReadOnlySpan<int> triangles)
        {
            static float cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;

            static bool inside(float2 p, float2 t0, float2 t1, float2 t2)
            {
                var (v0, v1, v2) = (t1 - t0, t2 - t0, p - t0);
                var denInv = 1 / cross(v0, v1);
                var v = denInv * cross(v2, v1);
                var w = denInv * cross(v0, v2);
                var u = 1f - v - w;
                var bar = math.float3(u, v, w);
                return math.cmax(-bar) <= 0;
            }

            static bool intersects(int i, int j, ReadOnlySpan<float2> positions, ReadOnlySpan<int> triangles)
            {
                var (p0, p1, p2) = (triangles[3 * i + 0], triangles[3 * i + 1], triangles[3 * i + 2]);
                var (x0, x1, x2) = (positions[p0], positions[p1], positions[p2]);

                var (q0, q1, q2) = (triangles[3 * j + 0], triangles[3 * j + 1], triangles[3 * j + 2]);
                var (y0, y1, y2) = (positions[q0], positions[q1], positions[q2]);

                return false
                    || p0 != q0 && p0 != q1 && p0 != q2 && inside(x0, y0, y1, y2)
                    || p1 != q0 && p1 != q1 && p1 != q2 && inside(x1, y0, y1, y2)
                    || p2 != q0 && p2 != q1 && p2 != q2 && inside(x2, y0, y1, y2)
                    || q0 != p0 && q0 != p1 && q0 != p2 && inside(y0, x0, x1, x2)
                    || q1 != p0 && q1 != p1 && q1 != p2 && inside(y1, x0, x1, x2)
                    || q2 != p0 && q2 != p1 && q2 != p2 && inside(y2, x0, x1, x2)
                ;
            }

            var notclockwise = new List<int>();
            for (int i = 0; i < triangles.Length / 3; i++)
            {
                var (p0, p1, p2) = (triangles[3 * i + 0], triangles[3 * i + 1], triangles[3 * i + 2]);
                var (x0, x1, x2) = (positions[p0], positions[p1], positions[p2]);

                if ((x2 - x0).y * (x1 - x0).x > (x1 - x0).y * (x2 - x0).x)
                {
                    notclockwise.Add(i);
                }
            }

            var intersecting = new List<(int, int)>();
            for (int i = 0; i < triangles.Length / 3; i++)
            {
                for (int j = i + 1; j < triangles.Length / 3; j++)
                {
                    if (intersects(i, j, positions, triangles))
                    {
                        intersecting.Add((i, j));
                    }
                }
            }

            var msg = "Triangulation is invalid!";
            msg += notclockwise.Count == 0 ? "" : $"\n  - Some triangles are not oriented clockwise: [{string.Join(", ", notclockwise)}].";
            msg += intersecting.Count == 0 ? "" : $"\n  - Some triangles are intersecting: [{string.Join(",", intersecting)}].";

            if (notclockwise.Count != 0 || intersecting.Count != 0)
            {
                throw new AssertionException(msg);
            }
        }

        public static void Draw(ReadOnlySpan<float2> positions, ReadOnlySpan<int> triangles, Color color, float duration)
        {
            for (int i = 0; i < triangles.Length / 3; i++)
            {
                var x = math.float3(positions[triangles[3 * i + 0]], 0);
                var y = math.float3(positions[triangles[3 * i + 1]], 0);
                var z = math.float3(positions[triangles[3 * i + 2]], 0);

                Debug.DrawLine(x, y, color, duration);
                Debug.DrawLine(x, z, color, duration);
                Debug.DrawLine(z, y, color, duration);
            }
        }

        public static void Draw(ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> triangles, Color color, float duration)
        {
            for (int i = 0; i < triangles.Length / 3; i++)
            {
                var x = positions[triangles[3 * i + 0]];
                var y = positions[triangles[3 * i + 1]];
                var z = positions[triangles[3 * i + 2]];

                Debug.DrawLine(x, y, color, duration);
                Debug.DrawLine(x, z, color, duration);
                Debug.DrawLine(z, y, color, duration);
            }
        }

        public static string LaTeXify(this Triangulator<float2> triangulator,
            string path = default,
            string appendTikz = "",
            int[][] loops = default
        )
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");

            var builder = new StringBuilder();
            builder.Append(
@"\documentclass[border=1mm]{standalone}
\usepackage{tikz}

\begin{document}

\tikzset{
    every picture/.style={
        line width=.1pt
    }
}

\begin{tikzpicture}[scale=10,inner sep=0pt]
"
            );

            var p = triangulator.Output.Positions;
            var t = triangulator.Output.Triangles;
            for (int tId = 0; tId < t.Length / 3; tId++)
            {
                var (i, j, k) = (t[3 * tId + 0], t[3 * tId + 1], t[3 * tId + 2]);
                builder.AppendLine(
$@"\draw[gray]({p[i].x}, {p[i].y})--({p[j].x}, {p[j].y});
\draw[gray]({p[i].x}, {p[i].y})--({p[k].x}, {p[k].y});
\draw[gray]({p[j].x}, {p[j].y})--({p[k].x}, {p[k].y});"
                );
            }

            if (loops != default)
            {
                foreach (var loop in loops)
                {
                    builder.Append('\n');
                    builder.Append(@$"\draw[black, line width = 0.2pt]({p[loop[0]].x}, {p[loop[0]].y})");
                    for (int i = 0; i < loop.Length / 2 - 1; i++)
                    {
                        builder.Append($"--({p[loop[2 * i + 1]].x}, {p[loop[2 * i + 1]].y})");
                    }
                    builder.Append("--cycle;\n");
                }
            }

            builder.AppendLine(appendTikz);

            builder.AppendLine(@"
\end{tikzpicture}
\end{document}"
                );

            if (path != default)
            {
                using var writer = new System.IO.StreamWriter(path);
                writer.WriteLine(builder.ToString());
            }

            return builder.ToString();
        }
    }

    public class AssertValidTriangulationTests
    {
        private static readonly TestCaseData[] validTestData =
        {
            new(new float2[]
            {
                new(0, 0), new(1, 0), new(1, 1),
                new(2, 0), new(3, 0), new(3, 2),
            },
            new int[]
            {
                0, 2, 1,
                3, 5, 4,
            }) { TestName = "Case 1 (fully separated triangles)" },
            new(new float2[]
            {
                new(0, 0), new(1, 0), new(1, 1),
                new(-1, 0), new(-1, 1),
            },
            new int[]
            {
                0, 2, 1,
                0, 3, 4,
            }) { TestName = "Case 2 (triangles with one common point)" },
            new(new float2[]
            {
                new(0, 0), new(1, 0), new(1, 1),
                new(-1, 1),
            },
            new int[]
            {
                0, 2, 1,
                0, 3, 2,
            }) { TestName = "Case 3 (triangles with two common points)" },
        };

        [Test, TestCaseSource(nameof(validTestData))]
        public void ValidTest(float2[] positions, int[] triangles) => TestUtils.AssertValidTriangulation(positions, triangles);

        private static readonly TestCaseData[] throwAssertionExpectionTestData =
        {
            new(new float2[]
            {
                new(0, 0), new(1, 0), new(1, 1),
                new(2, 0), new(3, 0), new(2f / 3, 1f / 3),
            },
            new int[]
            {
                0, 1, 2,
                3, 4, 5
            }) { TestName = "Case 1 (intersecting triangles, no common points)" },
            new(new float2[]
            {
                new(0, 0), new(1, 0), new(1, 1),
                new(0, 1), new(2f / 3, 1f / 3),
            },
            new int[]
            {
                0, 2, 1,
                0, 3, 4
            }) { TestName = "Case 2 (intersecting triangles, one common point)" },
            new(new float2[]
            {
                new(0, 0), new(1, 0), new(1, 1),
                new(2f / 3, 1f / 3),
            },
            new int[]
            {
                0, 2, 1,
                0, 2, 3
            }) { TestName = "Case 3 (intersecting triangles, two common points)" },

            new(new float2[]
            {
                new(3, 0), new(4, 0), new(4, 4),
                new(0, 0), new(1, 0), new(1, 1),
            },
            new int[]
            {
                0, 2, 1,
                3, 4, 5
            }) { TestName = "Case 4 (counterclockwise triangles)" },
        };

        [Test, TestCaseSource(nameof(throwAssertionExpectionTestData))]
        public void ThrowAssertionExpectionTest(float2[] positions, int[] triangles) => Debug.Log(
            Assert.Throws<AssertionException>(() => TestUtils.AssertValidTriangulation(positions, triangles)).Message
        );
    }
}