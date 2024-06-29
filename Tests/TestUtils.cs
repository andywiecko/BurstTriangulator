using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools.Utils;

namespace andywiecko.BurstTriangulator.Editor.Tests
{
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

    public static class TestUtils
    {
        public static (int, int, int)[] GetTrisTuple(this Triangulator triangulator) =>
            triangulator.Output.Triangles.ToTrisTuple();
        public static (int, int, int)[] GetTrisTuple<T>(this Triangulator<T> triangulator) where T : unmanaged =>
            triangulator.Output.Triangles.ToTrisTuple();
        private static (int, int, int)[] ToTrisTuple(this NativeList<int> triangles) => Enumerable
            .Range(0, triangles.Length / 3)
            .Select(i => (triangles[3 * i], triangles[3 * i + 1], triangles[3 * i + 2]))
            .OrderBy(i => i.Item1).ThenBy(i => i.Item2).ThenBy(i => i.Item3)
            .ToArray();

        public static void Draw(this Triangulator triangulator, float duration = 5f) => Draw(triangulator, Color.red, duration);
        public static void Draw(this Triangulator<float2> triangulator, float duration = 5f) => Draw(triangulator, Color.red, duration);
        public static void Draw(this Triangulator<double2> triangulator, float duration = 5f) => Draw(triangulator, Color.red, duration);
        public static void Draw(this Triangulator triangulator, Color color, float duration = 5f) =>
            Draw(triangulator.Output.Positions.AsArray().Select(i => (float2)i).ToArray(), triangulator.GetTrisTuple(), color, duration);
        public static void Draw(this Triangulator<float2> triangulator, Color color, float duration = 5f) =>
            Draw(triangulator.Output.Positions.AsArray(), triangulator.GetTrisTuple(), color, duration);
        public static void Draw(this Triangulator<double2> triangulator, Color color, float duration = 5f) =>
            Draw(triangulator.Output.Positions.AsArray().Select(i => (float2)i).ToArray(), triangulator.GetTrisTuple(), color, duration);

        private static void Draw(ReadOnlySpan<float2> positions, ReadOnlySpan<(int, int, int)> triangles, Color color, float duration)
        {
            var p = positions;
            foreach (var (i, j, k) in triangles)
            {
                var x = math.float3(p[i], 0);
                var y = math.float3(p[j], 0);
                var z = math.float3(p[k], 0);

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
            foreach (var (i, j, k) in triangulator.GetTrisTuple())
            {
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

        public static (int i, int j, int k)[] SortTrianglesIds((int i, int j, int k)[] triangles)
        {
            var copy = triangles.ToArray();
            for (int i = 0; i < triangles.Length; i++)
            {
                var tri = triangles[i];
                var t = math.int3(tri.i, tri.j, tri.k);
                var min = math.cmin(t);
                var id = -1;
                for (int ti = 0; ti < 3; ti++)
                {
                    if (min == t[ti])
                    {
                        id = ti;
                        break;
                    }
                }

                copy[i] = (min, t[(id + 1) % 3], t[(id + 2) % 3]);
            }
            return copy;
        }
    }
}