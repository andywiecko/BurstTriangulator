using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
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

    public static class TestUtils
    {
        public static (int, int, int)[] GetTrisTuple(this Triangulator triangulator) =>
            triangulator.Output.Triangles.ToTrisTuple();

        private static (int, int, int)[] ToTrisTuple(this NativeList<int> triangles) => Enumerable
            .Range(0, triangles.Length / 3)
            .Select(i => (triangles[3 * i], triangles[3 * i + 1], triangles[3 * i + 2]))
            .OrderBy(i => i.Item1).ThenBy(i => i.Item2).ThenBy(i => i.Item3)
            .ToArray();

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