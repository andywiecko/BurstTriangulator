using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace andywiecko.BurstTriangulator
{
    public class Triangulator : IDisposable
    {
        #region Primitives
        private readonly struct Triangle
        {
            public readonly int IdA;
            public readonly int IdB;
            public readonly int IdC;

            public Triangle(int idA, int idB, int idC)
            {
                IdA = idA;
                IdB = idB;
                IdC = idC;
            }

            public static implicit operator Triangle((int a, int b, int c) ids) => new Triangle(ids.a, ids.b, ids.c);
            public void Deconstruct(out int idA, out int idB, out int idC) => _ = (idA = IdA, idB = IdB, idC = IdC);
            public bool Contains(int id) => IdA == id || IdB == id || IdC == id;
            public bool ContainsCommonPointWith(Triangle t) => Contains(t.IdA) || Contains(t.IdB) || Contains(t.IdC);
            public bool ContainsCommonPointWith(Edge e) => Contains(e.IdA) || Contains(e.IdB);

            public int UnsafeOtherPoint(Edge edge)
            {
                if (!edge.Contains(IdA))
                {
                    return IdA;
                }
                else if (!edge.Contains(IdB))
                {
                    return IdB;
                }
                else
                {
                    return IdC;
                }
            }

            public Triangle Flip() => (IdC, IdB, IdA);
            public Triangle WithShift(int i) => (IdA + i, IdB + i, IdC + i);
            public float GetSignedArea2(NativeList<float2> positions)
            {
                var (pA, pB, pC) = (positions[IdA], positions[IdB], positions[IdC]);
                var pAB = pB - pA;
                var pAC = pC - pA;
                return Cross(pAB, pAC);
            }
            public float GetArea2(NativeList<float2> positions) => math.abs(GetSignedArea2(positions));
        }

        private readonly struct Circle
        {
            public readonly float2 Center;
            public readonly float Radius;
            public readonly float RadiusSq;

            public Circle(float2 center, float radius)
            {
                Center = center;
                Radius = radius;
                RadiusSq = Radius * Radius;
            }

            public void Deconstruct(out float2 center, out float radius)
            {
                center = Center;
                radius = Radius;
            }
        }

        private readonly struct Edge : IEquatable<Edge>
        {
            public readonly int IdA;
            public readonly int IdB;

            public Edge(int idA, int idB)
            {
                IdA = idA < idB ? idA : idB;
                IdB = idA < idB ? idB : idA;
            }

            public static implicit operator Edge((int a, int b) ids) => new Edge(ids.a, ids.b);
            public void Deconstruct(out int idA, out int idB) => _ = (idA = IdA, idB = IdB);
            public bool Equals(Edge other) => IdA == other.IdA && IdB == other.IdB;
            public bool Contains(int id) => IdA == id || IdB == id;
            // Elegant pairing function: https://en.wikipedia.org/wiki/Pairing_function
            public override int GetHashCode() => IdA < IdB ? IdB * IdB + IdA : IdA * IdA + IdA + IdB;
        }

        private readonly struct Edge3
        {
            public readonly Edge EdgeA;
            public readonly Edge EdgeB;
            public readonly Edge EdgeC;

            public Edge3(Edge edgeA, Edge edgeB, Edge edgeC)
            {
                EdgeA = edgeA;
                EdgeB = edgeB;
                EdgeC = edgeC;
            }

            public struct Iterator
            {
                private readonly Edge3 owner;
                private int current;
                public Iterator(Edge3 owner)
                {
                    this.owner = owner;
                    current = -1;
                }

                public Edge Current => current switch
                {
                    0 => owner.EdgeA,
                    1 => owner.EdgeB,
                    2 => owner.EdgeC,
                    _ => throw new Exception()
                };

                public bool MoveNext() => ++current < 3;
            }

            public Iterator GetEnumerator() => new Iterator(this);
            public bool Contains(Edge edge) => EdgeA.Equals(edge) || EdgeB.Equals(edge) || EdgeC.Equals(edge);

            public static implicit operator Edge3((Edge eA, Edge eB, Edge eC) v) => new Edge3(v.eA, v.eB, v.eC);
        }
        #endregion

        #region Triangulation native data
        private struct TriangulatorNativeData : IDisposable
        {
            private struct DescendingComparer : IComparer<int>
            {
                public int Compare(int x, int y) => x < y ? 1 : -1;
            }

            public NativeList<float2> outputPositions;
            public NativeList<int> outputTriangles;
            public NativeList<Triangle> triangles;
            public NativeList<Circle> circles;
            public NativeList<Edge3> trianglesToEdges;
            public NativeHashMap<Edge, FixedList32<int>> edgesToTriangles;

            private NativeList<Edge> tmpPolygon;
            private NativeList<int> badTriangles;

            private static readonly DescendingComparer comparer = new DescendingComparer();

            public TriangulatorNativeData(int capacity, Allocator allocator)
            {
                outputPositions = new NativeList<float2>(capacity, allocator);
                outputTriangles = new NativeList<int>(3 * capacity, allocator);
                triangles = new NativeList<Triangle>(capacity, allocator);
                circles = new NativeList<Circle>(capacity, allocator);
                trianglesToEdges = new NativeList<Edge3>(capacity, allocator);
                edgesToTriangles = new NativeHashMap<Edge, FixedList32<int>>(capacity, allocator);

                tmpPolygon = new NativeList<Edge>(capacity, allocator);
                badTriangles = new NativeList<int>(capacity, allocator);
            }

            public void Dispose()
            {
                outputPositions.Dispose();
                outputTriangles.Dispose();
                triangles.Dispose();
                circles.Dispose();
                trianglesToEdges.Dispose();
                edgesToTriangles.Dispose();

                tmpPolygon.Dispose();
                badTriangles.Dispose();
            }

            public void Clear()
            {
                outputPositions.Clear();
                outputTriangles.Clear();
                triangles.Clear();
                circles.Clear();
                trianglesToEdges.Clear();

                tmpPolygon.Clear();
                badTriangles.Clear();
            }

            public void AddTriangle(Triangle t)
            {
                triangles.Add(t);

                var (idA, idB, idC) = t;

                var pA = outputPositions[idA];
                var pB = outputPositions[idB];
                var pC = outputPositions[idC];

                circles.Add(GetCircumcenter(pA, pB, pC));
                trianglesToEdges.Add(((idA, idB), (idA, idC), (idB, idC)));
            }

            private void RegisterEdgeData(Edge edge, int triangleId)
            {
                if (edgesToTriangles.TryGetValue(edge, out var tris))
                {
                    tris.Add(triangleId);
                    edgesToTriangles[edge] = tris;
                }
                else
                {
                    edgesToTriangles.Add(edge, new FixedList32<int>() { triangleId });
                }
            }

            public void RemoveTriangle(int id)
            {
                triangles.RemoveAt(id);
                circles.RemoveAt(id);
                trianglesToEdges.RemoveAt(id);
            }

            public int InsertPoint(float2 p)
            {
                var pId = outputPositions.Length;

                outputPositions.Add(p);

                badTriangles.Clear();
                for (int tId = 0; tId < triangles.Length; tId++)
                {
                    var circle = circles[tId];
                    if (math.distancesq(circle.Center, p) <= circle.RadiusSq)
                    {
                        badTriangles.Add(tId);
                    }
                }

                tmpPolygon.Clear();
                foreach (var t1 in badTriangles)
                {
                    foreach (var edge in trianglesToEdges[t1])
                    {
                        if (tmpPolygon.Contains(edge))
                        {
                            continue;
                        }

                        var edgeFound = false;
                        foreach (var t2 in badTriangles)
                        {
                            if (t1 == t2)
                            {
                                continue;
                            }

                            if (trianglesToEdges[t2].Contains(edge))
                            {
                                edgeFound = true;
                                break;
                            }
                        }

                        if (edgeFound == false)
                        {
                            tmpPolygon.Add(edge);
                        }
                    }
                }

                badTriangles.Sort(comparer);
                foreach (var tId in badTriangles)
                {
                    RemoveTriangle(tId);
                }

                foreach (var (e1, e2) in tmpPolygon)
                {
                    AddTriangle((pId, e1, e2));
                }

                edgesToTriangles.Clear();
                for (int tId = 0; tId < triangles.Length; tId++)
                {
                    foreach (var edge in trianglesToEdges[tId])
                    {
                        RegisterEdgeData(edge, tId);
                    }
                }

                return pId;
            }
        }
        #endregion

        public class TriangulationSettings
        {
            /// <summary>
            /// Triangle is considered as <em>bad</em> if any of its angles is smaller than <see cref="MinimumAngle"/>.
            /// </summary>
            /// <remarks>
            /// Expressed in <em>radians</em>.
            /// </remarks>
            public float MinimumAngle { get; set; } = math.radians(33);
            /// <summary>
            /// Triangle is <b>not</b> considered as <em>bad</em> if its area is smaller than <see cref="MinimumArea"/>.
            /// </summary>
            public float MinimumArea { get; set; } = 0.015f;
            /// <summary>
            /// Triangle is considered as <em>bad</em> if its area is greater than <see cref="MaximumArea"/>.
            /// </summary>
            public float MaximumArea { get; set; } = 0.5f;
            /// <summary>
            /// If <see langword="true"/> refines mesh using 
            /// <see href="https://en.wikipedia.org/wiki/Delaunay_refinement#Ruppert's_algorithm">Ruppert's algorithm</see>.
            /// </summary>
            public bool RefineMesh { get; set; } = true;
            /// <summary>
            /// Batch count used in parallel jobs.
            /// </summary>
            public int BatchCount { get; set; } = 64;
        }

        public TriangulationSettings Settings { get; } = new TriangulationSettings();
        public NativeArray<float2>.ReadOnly Positions => data.outputPositions.AsArray().AsReadOnly();
        public NativeArray<int>.ReadOnly Triangles => data.outputTriangles.AsArray().AsReadOnly();
        public NativeArray<float2> PositionsDeferred => data.outputPositions.AsDeferredJobArray();
        public NativeArray<int> TrianglesDeferred => data.outputTriangles.AsDeferredJobArray();

        private static readonly Triangle SuperTriangle = new Triangle(0, 1, 2);
        private TriangulatorNativeData data;

        public Triangulator(int capacity, Allocator allocator) => data = new TriangulatorNativeData(capacity, allocator);
        public Triangulator(Allocator allocator) : this(capacity: 16 * 1024, allocator) { }

        public JobHandle Schedule(NativeArray<float2>.ReadOnly positions, JobHandle dependencies)
        {
            CheckPositionsCount(positions.Length);

            dependencies = new ClearDataJob(this).Schedule(dependencies);
            dependencies = new RegisterSuperTriangleJob(this, positions).Schedule(dependencies);
            dependencies = new DelaunayTriangulationJob(this, positions).Schedule(dependencies);

            if (Settings.RefineMesh)
            {
                dependencies = new RefineMeshJob(this).Schedule(dependencies);
            }

            dependencies = new CleanupTrianglesJob(this).Schedule(this, dependencies);
            dependencies = new CleanupPositionsJob(this).Schedule(dependencies);

            return dependencies;
        }

        public void Dispose() => data.Dispose();

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckPositionsCount(int length)
        {
            if (length < 3)
            {
                throw new ArgumentException("Cannot schedule triangulation without at least 3 points", nameof(length));
            }
        }

        #region Jobs
        [BurstCompile]
        private struct ClearDataJob : IJob
        {
            private TriangulatorNativeData data;

            public ClearDataJob(Triangulator triangulator)
            {
                data = triangulator.data;
            }

            public void Execute() => data.Clear();
        }

        [BurstCompile]
        private struct RegisterSuperTriangleJob : IJob
        {
            private NativeArray<float2>.ReadOnly inputPositions;
            private TriangulatorNativeData data;

            public RegisterSuperTriangleJob(Triangulator triangulator, NativeArray<float2>.ReadOnly inputPositions)
            {
                this.inputPositions = inputPositions;
                data = triangulator.data;
            }

            public void Execute()
            {
                var min = (float2)float.MaxValue;
                var max = (float2)float.MinValue;

                for (int i = 0; i < inputPositions.Length; i++)
                {
                    var p = inputPositions[i];
                    min = math.min(min, p);
                    max = math.max(max, p);
                }

                var center = 0.5f * (min + max);
                var r = 0.5f * math.distance(min, max);

                var pA = center + r * math.float2(0, 2);
                var pB = center + r * math.float2(-math.sqrt(3), -1);
                var pC = center + r * math.float2(+math.sqrt(3), -1);

                var idA = data.InsertPoint(pA);
                var idB = data.InsertPoint(pB);
                var idC = data.InsertPoint(pC);

                data.AddTriangle((idA, idB, idC));
            }
        }

        [BurstCompile]
        private struct DelaunayTriangulationJob : IJob
        {
            private NativeArray<float2>.ReadOnly inputPositions;
            private TriangulatorNativeData data;

            public DelaunayTriangulationJob(Triangulator triangluator, NativeArray<float2>.ReadOnly inputPositions)
            {
                this.inputPositions = inputPositions;
                data = triangluator.data;
            }

            public void Execute()
            {
                for (int i = 0; i < inputPositions.Length; i++)
                {
                    data.InsertPoint(inputPositions[i]);
                }
            }
        }

        [BurstCompile]
        private struct RefineMeshJob : IJob
        {
            private TriangulatorNativeData data;
            private readonly float minimumArea2;
            private readonly float maximumArea2;
            private readonly float minimumAngle;

            public RefineMeshJob(Triangulator triangulator)
            {
                data = triangulator.data;
                minimumArea2 = 2 * triangulator.Settings.MinimumArea;
                maximumArea2 = 2 * triangulator.Settings.MaximumArea;
                minimumAngle = triangulator.Settings.MinimumAngle;
            }

            public void Execute()
            {
                while (TrySplitEncroachedEdge() || TryRemoveBadTriangle()) { }
            }

            private bool TrySplitEncroachedEdge()
            {
                foreach (var edge3 in data.trianglesToEdges)
                {
                    foreach (var edge in edge3)
                    {
                        if (!SuperTriangle.ContainsCommonPointWith(edge) && EdgeIsEncroached(edge))
                        {
                            if (AnyEdgeTriangleAreaIsTooSmall(edge))
                            {
                                continue;
                            }

                            var (e1, e2) = edge;
                            var (pA, pB) = (data.outputPositions[e1], data.outputPositions[e2]);
                            data.InsertPoint(0.5f * (pA + pB));
                            return true;
                        }
                    }
                }

                return false;
            }

            private bool AnyEdgeTriangleAreaIsTooSmall(Edge edge)
            {
                foreach (var tId in data.edgesToTriangles[edge])
                {
                    var area2 = data.triangles[tId].GetArea2(data.outputPositions);
                    if (area2 < minimumArea2)
                    {
                        return true;
                    }
                }
                return false;
            }

            private bool EdgeIsEncroached(Edge edge)
            {
                var (e1, e2) = edge;
                var (pA, pB) = (data.outputPositions[e1], data.outputPositions[e2]);
                var circle = new Circle(0.5f * (pA + pB), 0.5f * math.distance(pA, pB));
                foreach (var tId in data.edgesToTriangles[edge])
                {
                    var triangle = data.triangles[tId];
                    var pointId = triangle.UnsafeOtherPoint(edge);
                    var pC = data.outputPositions[pointId];
                    if (math.distancesq(circle.Center, pC) < circle.RadiusSq)
                    {
                        return true;
                    }
                }
                return false;
            }

            private bool TryRemoveBadTriangle()
            {
                for (int tId = 0; tId < data.triangles.Length; tId++)
                {
                    var triangle = data.triangles[tId];
                    if (!SuperTriangle.ContainsCommonPointWith(triangle) && !TriangleIsEncroached(tId) && TriangleIsBad(triangle))
                    {
                        var circle = data.circles[tId];
                        data.InsertPoint(circle.Center);
                        return true;
                    }
                }
                return false;
            }

            private bool TriangleIsEncroached(int triangleId)
            {
                var edges = data.trianglesToEdges[triangleId];
                foreach (var edgeId in edges)
                {
                    if (EdgeIsEncroached(edgeId))
                    {
                        return true;
                    }
                }
                return false;
            }

            private bool TriangleIsBad(Triangle triangle)
            {
                var area2 = triangle.GetArea2(data.outputPositions);
                if (area2 < minimumArea2)
                {
                    return false;
                }

                if (area2 > maximumArea2)
                {
                    return true;
                }

                return AngleIsTooSmall(triangle);
            }

            private bool AngleIsTooSmall(Triangle triangle)
            {
                var (a, b, c) = triangle;
                var (pA, pB, pC) = (data.outputPositions[a], data.outputPositions[b], data.outputPositions[c]);

                var pAB = pB - pA;
                var pBC = pC - pB;
                var pCA = pA - pC;

                var angles = math.float3
                (
                    Angle(pAB, -pCA),
                    Angle(pBC, -pAB),
                    Angle(pCA, -pBC)
                );

                return math.any(math.abs(angles) < minimumAngle);
            }
        }

        [BurstCompile]
        private unsafe struct CleanupTrianglesJob : IJobParallelForDefer
        {
            [ReadOnly]
            private NativeList<Triangle> triangles;

            [ReadOnly]
            private NativeList<float2> outputPositions;

            [WriteOnly]
            private NativeList<int>.ParallelWriter outputTriangles;

            public CleanupTrianglesJob(Triangulator triangulator)
            {
                triangles = triangulator.data.triangles;
                outputPositions = triangulator.data.outputPositions;
                outputTriangles = triangulator.data.outputTriangles.AsParallelWriter();
            }

            public JobHandle Schedule(Triangulator triangulator, JobHandle dependencies)
            {
                return this.Schedule(triangulator.data.triangles, triangulator.Settings.BatchCount, dependencies);
            }

            public void Execute(int i)
            {
                var t = triangles[i];
                if (t.ContainsCommonPointWith(SuperTriangle))
                {
                    return;
                }

                t = t.GetSignedArea2(outputPositions) < 0 ? t : t.Flip();
                t = t.WithShift(-3); // Due to supertriangle verticies.
                var ptr = UnsafeUtility.AddressOf(ref t);
                outputTriangles.AddRangeNoResize(ptr, 3);
            }
        }

        [BurstCompile]
        private struct CleanupPositionsJob : IJob
        {
            private NativeList<float2> positions;

            public CleanupPositionsJob(Triangulator triangulator)
            {
                positions = triangulator.data.outputPositions;
            }

            public void Execute()
            {
                // Remove Super Triangle positions
                positions.RemoveAt(0);
                positions.RemoveAt(0);
                positions.RemoveAt(0);
            }
        }
        #endregion

        #region Utils
        private static float Angle(float2 a, float2 b) => math.atan2(Cross(a, b), math.dot(a, b));
        private static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;
        private static Circle GetCircumcenter(float2 pA, float2 pB, float2 pC)
        {
            var pAB = pB - pA;
            var pAC = pC - pA;
            var mat = math.transpose(math.float2x2(pAB, pAC));
            var det = math.determinant(mat);

            var pBC = pC - pB;

            var pABlenSq = math.lengthsq(pAB);
            var pAClenSq = math.lengthsq(pAC);
            var pBClenSq = math.lengthsq(pBC);

            var r = 0.5f * math.sqrt(pABlenSq * pAClenSq * pBClenSq) / det;

            var lengths = math.float2(pABlenSq, pAClenSq);
            var offset = 0.5f * math.float2
            (
                x: -math.determinant(math.float2x2(mat.c1, lengths)),
                y: +math.determinant(math.float2x2(mat.c0, lengths))
            ) / det;

            var c = pA + offset;
            return new Circle(center: c, radius: r);
        }
        #endregion
    }
}
