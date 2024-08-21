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

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    public class TrianglesComparer : IEqualityComparer<IEnumerable<int>>
    {
        public static readonly TrianglesComparer Instance = new();

        private readonly struct Int3Comparer : IComparer<int3>
        {
            public readonly int Compare(int3 a, int3 b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y != b.y ? a.y.CompareTo(b.y) : a.z.CompareTo(b.z);
        }

        public bool Equals(IEnumerable<int> x, IEnumerable<int> y)
        {
            using var _x = new NativeArray<int>(x.ToArray(), Allocator.Persistent);
            using var _y = new NativeArray<int>(y.ToArray(), Allocator.Persistent);

            if (_x.Length != _y.Length) return false;

            static NativeArray<int> sort(NativeArray<int> t)
            {
                for (int i = 0; i < t.Length / 3; i++)
                {
                    var (t0, t1, t2) = (t[3 * i + 0], t[3 * i + 1], t[3 * i + 2]);
                    var (id, min) = (0, t0);
                    (id, min) = t1 < min ? (1, t1) : (id, min);
                    (id, min) = t2 < min ? (2, t2) : (id, min);
                    (t[3 * i + 0], t[3 * i + 1], t[3 * i + 2]) = (t[3 * i + id], t[3 * i + (id + 1) % 3], t[3 * i + (id + 2) % 3]);
                }

                t.Reinterpret<int3>(4).Sort(default(Int3Comparer));
                return t;
            }

            return Enumerable.SequenceEqual(sort(_x), sort(_y));
        }

        public int GetHashCode(IEnumerable<int> obj) => default;
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

    public static class TestExtensions
    {
        public static void Triangulate<T>(this LowLevel.Unsafe.UnsafeTriangulator<T> triangulator, LowLevel.Unsafe.InputData<T> input, LowLevel.Unsafe.OutputData<T> output, LowLevel.Unsafe.Args args, Allocator allocator) where T : unmanaged =>
            LowLevel.Unsafe.Extensions.Triangulate((dynamic)triangulator, (dynamic)input, (dynamic)output, args, allocator);
        public static void PlantHoleSeeds<T>(this LowLevel.Unsafe.UnsafeTriangulator<T> triangulator, LowLevel.Unsafe.InputData<T> input, LowLevel.Unsafe.OutputData<T> output, LowLevel.Unsafe.Args args, Allocator allocator) where T : unmanaged =>
            LowLevel.Unsafe.Extensions.PlantHoleSeeds((dynamic)triangulator, (dynamic)input, (dynamic)output, args, allocator);
        public static void Run<T>(this Triangulator<T> triangulator) where T : unmanaged =>
            Extensions.Run((dynamic)triangulator);
        public static JobHandle Schedule<T>(this Triangulator<T> triangulator, JobHandle dependencies = default) where T : unmanaged =>
            Extensions.Schedule((dynamic)triangulator, dependencies);
        public static T[] DynamicCast<T>(this IEnumerable<float2> data) where T : unmanaged =>
            data.Select(i => (T)(dynamic)i).ToArray();
        public static IEnumerable<float2> Scale(this IEnumerable<float2> data, float s, bool predicate) => data.Select(i => (predicate ? s : 1f) * i);
        public static T[] DynamicCast<T>(this IEnumerable<double2> data) where T : unmanaged => default(T) switch
        {
            // TODO: this extension can be removed entirely.
            Vector2 _ => data.Select(i => (T)(dynamic)(float2)i).ToArray(),
            _ => data.Select(i => (T)(dynamic)i).ToArray()
        };
        public static IEqualityComparer<T> Comparer<T>(float epsilon = 0.0001f) => default(T) switch
        {
            float2 _ => Float2Comparer.With(epsilon) as IEqualityComparer<T>,
            double2 _ => Double2Comparer.With(epsilon) as IEqualityComparer<T>,
            Vector2 _ => new Vector2EqualityComparer(epsilon) as IEqualityComparer<T>,
            _ => throw new NotImplementedException()
        };
        public static void Draw(this Triangulator triangulator, Color? color = null, float duration = 5f) => TestUtils.Draw(triangulator.Output.Positions.AsArray().Select(i => (float2)i).ToArray(), triangulator.Output.Triangles.AsArray().AsReadOnlySpan(), color ?? Color.red, duration);
        public static void Draw<T>(this Triangulator<T> triangulator, Color? color = null, float duration = 5f) where T : unmanaged =>
            TestUtils.Draw(triangulator.Output.Positions.AsArray().Select(i => (float2)(dynamic)i).ToArray(), triangulator.Output.Triangles.AsArray().AsReadOnlySpan(), color ?? Color.red, duration);
    }

    public static class TestUtils
    {
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
}