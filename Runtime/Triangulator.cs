using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace andywiecko.BurstTriangulator
{
    public class Triangulator : IDisposable
    {
        #region Primitives
        private readonly struct Triangle : IEquatable<Triangle>
        {
            public readonly int IdA, IdB, IdC;
            public Triangle(int idA, int idB, int idC) => (IdA, IdB, IdC) = (idA, idB, idC);
            public static implicit operator Triangle((int a, int b, int c) ids) => new Triangle(ids.a, ids.b, ids.c);
            public void Deconstruct(out int idA, out int idB, out int idC) => (idA, idB, idC) = (IdA, IdB, IdC);
            public bool Contains(int id) => IdA == id || IdB == id || IdC == id;
            public bool ContainsCommonPointWith(Triangle t) => Contains(t.IdA) || Contains(t.IdB) || Contains(t.IdC);
            public bool ContainsCommonPointWith(Edge e) => Contains(e.IdA) || Contains(e.IdB);
            public bool Equals(Triangle other) => IdA == other.IdA && IdB == other.IdB && IdC == other.IdC;
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
            public override string ToString() => $"({IdA}, {IdB}, {IdC})";
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

        private readonly struct Edge : IEquatable<Edge>, IComparable<Edge>
        {
            public readonly int IdA, IdB;
            public Edge(int idA, int idB) => (IdA, IdB) = idA < idB ? (idA, idB) : (idB, idA);
            public static implicit operator Edge((int a, int b) ids) => new Edge(ids.a, ids.b);
            public void Deconstruct(out int idA, out int idB) => (idA, idB) = (IdA, IdB);
            public bool Equals(Edge other) => IdA == other.IdA && IdB == other.IdB;
            public bool Contains(int id) => IdA == id || IdB == id;
            public bool ContainsCommonPointWith(Edge other) => Contains(other.IdA) || Contains(other.IdB);
            // Elegant pairing function: https://en.wikipedia.org/wiki/Pairing_function
            public override int GetHashCode() => IdA < IdB ? IdB * IdB + IdA : IdA * IdA + IdA + IdB;
            public int CompareTo(Edge other) => IdA != other.IdA ? IdA.CompareTo(other.IdA) : IdB.CompareTo(other.IdB);
            public override string ToString() => $"({IdA}, {IdB})";
        }

        private readonly struct Edge3
        {
            public readonly Edge EdgeA, EdgeB, EdgeC;
            public Edge3(Edge edgeA, Edge edgeB, Edge edgeC) => (EdgeA, EdgeB, EdgeC) = (edgeA, edgeB, edgeC);

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
            public NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles;
            public NativeList<Edge> constraintEdges;

            private NativeList<Edge> tmpPolygon;
            private NativeList<int> badTriangles;
            private NativeQueue<int> trianglesQueue;
            private NativeList<bool> visitedTriangles;

            private static readonly DescendingComparer comparer = new DescendingComparer();

            public TriangulatorNativeData(int capacity, Allocator allocator)
            {
                outputPositions = new NativeList<float2>(capacity, allocator);
                outputTriangles = new NativeList<int>(3 * capacity, allocator);
                triangles = new NativeList<Triangle>(capacity, allocator);
                circles = new NativeList<Circle>(capacity, allocator);
                trianglesToEdges = new NativeList<Edge3>(capacity, allocator);
                edgesToTriangles = new NativeHashMap<Edge, FixedList32Bytes<int>>(capacity, allocator);
                constraintEdges = new NativeList<Edge>(capacity, allocator);

                tmpPolygon = new NativeList<Edge>(capacity, allocator);
                badTriangles = new NativeList<int>(capacity, allocator);
                trianglesQueue = new NativeQueue<int>(allocator);
                visitedTriangles = new NativeList<bool>(capacity, allocator);
            }

            public void Dispose()
            {
                outputPositions.Dispose();
                outputTriangles.Dispose();
                triangles.Dispose();
                circles.Dispose();
                trianglesToEdges.Dispose();
                edgesToTriangles.Dispose();
                constraintEdges.Dispose();

                tmpPolygon.Dispose();
                badTriangles.Dispose();
                trianglesQueue.Dispose();
                visitedTriangles.Dispose();
            }

            public void Clear()
            {
                outputPositions.Clear();
                outputTriangles.Clear();
                triangles.Clear();
                circles.Clear();
                trianglesToEdges.Clear();
                edgesToTriangles.Clear();
                constraintEdges.Clear();

                tmpPolygon.Clear();
                badTriangles.Clear();
            }

            public void AddTriangle(Triangle t)
            {
                triangles.Add(t);
                var (idA, idB, idC) = t;
                circles.Add(CalculateCircumcenter(t));
                trianglesToEdges.Add(((idA, idB), (idA, idC), (idB, idC)));
            }

            private Circle CalculateCircumcenter(Triangle triangle)
            {
                var (idA, idB, idC) = triangle;
                var (pA, pB, pC) = (outputPositions[idA], outputPositions[idB], outputPositions[idC]);
                return GetCircumcenter(pA, pB, pC);
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
                    edgesToTriangles.Add(edge, new FixedList32Bytes<int>() { triangleId });
                }
            }

            public void RemoveTriangle(int id)
            {
                triangles.RemoveAt(id);
                circles.RemoveAt(id);
                trianglesToEdges.RemoveAt(id);
            }

            private void SearchForFirstBadTriangle(float2 p)
            {
                for (int tId = 0; tId < triangles.Length; tId++)
                {
                    var circle = circles[tId];
                    if (math.distancesq(circle.Center, p) <= circle.RadiusSq)
                    {
                        trianglesQueue.Enqueue(tId);
                        badTriangles.Add(tId);
                        return;
                    }
                }
            }

            private void RecalculateBadTriangles(float2 p, bool constraint = false)
            {
                while (trianglesQueue.TryDequeue(out var tId))
                {
                    visitedTriangles[tId] = true;

                    foreach (var e in trianglesToEdges[tId])
                    {
                        if (constraint && constraintEdges.Contains(e))
                        {
                            continue;
                        }

                        foreach (var otherId in edgesToTriangles[e])
                        {
                            if (visitedTriangles[otherId])
                            {
                                continue;
                            }

                            var circle = circles[otherId];
                            if (math.distancesq(circle.Center, p) <= circle.RadiusSq)
                            {
                                badTriangles.Add(otherId);
                                trianglesQueue.Enqueue(otherId);
                            }
                        }
                    }
                }
            }

            private void CalculateStarPolygon()
            {
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
            }

            private void RemoveBadTriangles()
            {
                badTriangles.Sort(comparer);
                foreach (var tId in badTriangles)
                {
                    RemoveTriangle(tId);
                }
            }

            private void CreatePolygonTriangles(int pId)
            {
                foreach (var (e1, e2) in tmpPolygon)
                {
                    AddTriangle((pId, e1, e2));
                }
            }

            private void RecalculateEdgeToTrianglesMapping()
            {
                edgesToTriangles.Clear();
                for (int tId = 0; tId < triangles.Length; tId++)
                {
                    foreach (var edge in trianglesToEdges[tId])
                    {
                        RegisterEdgeData(edge, tId);
                    }
                }
            }

            private void ClearVisitedTriangles()
            {
                visitedTriangles.Clear();
                visitedTriangles.Length = triangles.Length;
            }

            public int InsertPoint(float2 p)
            {
                var pId = outputPositions.Length;
                outputPositions.Add(p);
                badTriangles.Clear();
                ClearVisitedTriangles();
                SearchForFirstBadTriangle(p);
                RecalculateBadTriangles(p);
                ProcessBadTriangles(pId);
                return pId;
            }

            private void ProcessBadTriangles(int pId)
            {
                CalculateStarPolygon();
                RemoveBadTriangles();
                CreatePolygonTriangles(pId);
                RecalculateEdgeToTrianglesMapping();
            }

            public void UnsafeSwapEdge(Edge edge)
            {
                var (e0, e1) = edge;
                var tris = edgesToTriangles[edge];
                var t0 = tris[0];
                var t1 = tris[1];
                var triangle0 = triangles[t0];
                var triangle1 = triangles[t1];
                var q0 = triangle0.UnsafeOtherPoint(edge);
                var q1 = triangle1.UnsafeOtherPoint(edge);

                triangles[t0] = (q0, e0, q1);
                triangles[t1] = (q1, e1, q0);
                trianglesToEdges[t0] = ((q0, q1), (q0, e0), (q1, e0));
                trianglesToEdges[t1] = ((q0, q1), (q0, e1), (q1, e1));

                var eTot0 = edgesToTriangles[(e0, q1)];
                eTot0.Remove(t1);
                eTot0.Add(t0);
                edgesToTriangles[(e0, q1)] = eTot0;

                var eTot1 = edgesToTriangles[(e1, q0)];
                eTot1.Remove(t0);
                eTot1.Add(t1);
                edgesToTriangles[(e1, q0)] = eTot1;

                edgesToTriangles.Remove(edge);
                edgesToTriangles.Add((q0, q1), new FixedList32Bytes<int>() { t0, t1 });

                circles[t0] = CalculateCircumcenter(triangles[t0]);
                circles[t1] = CalculateCircumcenter(triangles[t1]);
            }

            public int SplitEdge(Edge edge, bool constraint = false)
            {
                var (e0, e1) = edge;
                var (pA, pB) = (outputPositions[e0], outputPositions[e1]);
                var p = 0.5f * (pA + pB);

                var pId = outputPositions.Length;
                outputPositions.Add(p);
                badTriangles.Clear();

                ClearVisitedTriangles();
                foreach (var tId in edgesToTriangles[edge])
                {
                    if (!visitedTriangles[tId])
                    {
                        trianglesQueue.Enqueue(tId);
                        badTriangles.Add(tId);
                        RecalculateBadTriangles(p, constraint);
                    }
                }
                ProcessBadTriangles(pId);
                return pId;
            }

            public int InsertPointAtTriangleCircumcenter(float2 p, int tId, bool constraint = false)
            {
                var pId = outputPositions.Length;
                outputPositions.Add(p);
                badTriangles.Clear();
                ClearVisitedTriangles();
                badTriangles.Add(tId);
                trianglesQueue.Enqueue(tId);
                RecalculateBadTriangles(p, constraint);
                ProcessBadTriangles(pId);
                return pId;
            }

            public bool EdgeIsEncroached(Edge edge)
            {
                var (e1, e2) = edge;
                var (pA, pB) = (outputPositions[e1], outputPositions[e2]);
                var circle = new Circle(0.5f * (pA + pB), 0.5f * math.distance(pA, pB));
                foreach (var tId in edgesToTriangles[edge])
                {
                    var triangle = triangles[tId];
                    var pointId = triangle.UnsafeOtherPoint(edge);
                    var pC = outputPositions[pointId];
                    if (math.distancesq(circle.Center, pC) < circle.RadiusSq)
                    {
                        return true;
                    }
                }
                return false;
            }

            public bool TriangleIsEncroached(int triangleId)
            {
                var edges = trianglesToEdges[triangleId];
                foreach (var e in edges)
                {
                    if (EdgeIsEncroached(e))
                    {
                        return true;
                    }
                }
                return false;
            }

            public int SplitConstraint(Edge edge)
            {
                var (e0, e1) = edge;
                var pId = outputPositions.Length;
                var eId = constraintEdges.IndexOf(edge);
                constraintEdges.RemoveAt(eId);
                constraintEdges.Add((pId, e0));
                constraintEdges.Add((pId, e1));

                return SplitEdge(edge, constraint: true);
            }

            public bool TriangleIsBad(Triangle triangle, float minimumArea2, float maximumArea2, float minimumAngle)
            {
                var area2 = triangle.GetArea2(outputPositions);
                if (area2 < minimumArea2)
                {
                    return false;
                }

                if (area2 > maximumArea2)
                {
                    return true;
                }

                return AngleIsTooSmall(triangle, minimumAngle);
            }

            private bool AngleIsTooSmall(Triangle triangle, float minimumAngle)
            {
                var (a, b, c) = triangle;
                var (pA, pB, pC) = (outputPositions[a], outputPositions[b], outputPositions[c]);

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
        #endregion

        [Serializable]
        public class TriangulationSettings
        {
            /// <summary>
            /// Batch count used in parallel jobs.
            /// </summary>
            [field: SerializeField]
            public int BatchCount { get; set; } = 64;
            /// <summary>
            /// Triangle is considered as <em>bad</em> if any of its angles is smaller than <see cref="MinimumAngle"/>.
            /// </summary>
            /// <remarks>
            /// Expressed in <em>radians</em>.
            /// </remarks>
            [field: SerializeField]
            public float MinimumAngle { get; set; } = math.radians(33);
            /// <summary>
            /// Triangle is <b>not</b> considered as <em>bad</em> if its area is smaller than <see cref="MinimumArea"/>.
            /// </summary>
            [field: SerializeField]
            public float MinimumArea { get; set; } = 0.015f;
            /// <summary>
            /// Triangle is considered as <em>bad</em> if its area is greater than <see cref="MaximumArea"/>.
            /// </summary>
            [field: SerializeField]
            public float MaximumArea { get; set; } = 0.5f;
            /// <summary>
            /// If <see langword="true"/> refines mesh using 
            /// <see href="https://en.wikipedia.org/wiki/Delaunay_refinement#Ruppert's_algorithm">Ruppert's algorithm</see>.
            /// </summary>
            [field: SerializeField]
            public bool RefineMesh { get; set; } = true;
            /// <summary>
            /// If <see langword="true"/> constrains edges defined in <see cref="Input"/> using
            /// <see href="https://www.sciencedirect.com/science/article/abs/pii/004579499390239A">Sloan's algorithm</see>.
            /// </summary>
            [field: SerializeField]
            public bool ConstrainEdges { get; set; } = false;
            /// <summary>
            /// If <see langword="true"/> and provided <see cref="Triangulator.Input"/> is not valid, it will throw an exception.
            /// </summary>
            /// <remarks>
            /// Input validation is enabled only at Editor.
            /// </remarks>
            [field: SerializeField]
            public bool ValidateInput = true;
        }

        public class InputData
        {
            public NativeArray<float2> Positions { get; set; }
            public NativeArray<int> ConstraintEdges { get; set; }
        }

        public class OutputData
        {
            public NativeList<float2> Positions => owner.data.outputPositions;
            public NativeList<int> Triangles => owner.data.outputTriangles;
            private readonly Triangulator owner;
            public OutputData(Triangulator triangulator) => owner = triangulator;
        }

        public TriangulationSettings Settings { get; } = new TriangulationSettings();
        public InputData Input { get; set; } = new InputData();
        public OutputData Output { get; }

        [Obsolete("To get the result use this.Output instead.")]
        public NativeArray<float2>.ReadOnly Positions => data.outputPositions.AsArray().AsReadOnly();
        [Obsolete("To get the result use this.Output instead.")]
        public NativeArray<int>.ReadOnly Triangles => data.outputTriangles.AsArray().AsReadOnly();
        [Obsolete("To get the result use this.Output instead.")]
        public NativeArray<float2> PositionsDeferred => data.outputPositions.AsDeferredJobArray();
        [Obsolete("To get the result use this.Output instead.")]
        public NativeArray<int> TrianglesDeferred => data.outputTriangles.AsDeferredJobArray();

        private static readonly Triangle SuperTriangle = new Triangle(0, 1, 2);
        private TriangulatorNativeData data;

        public Triangulator(int capacity, Allocator allocator)
        {
            data = new TriangulatorNativeData(capacity, allocator);
            Output = new OutputData(this);
        }
        public Triangulator(Allocator allocator) : this(capacity: 16 * 1024, allocator) { }

        public void Dispose() => data.Dispose();

        public void Run() => Schedule().Complete();

        public JobHandle Schedule(JobHandle dependencies = default)
        {
#pragma warning disable CS0618
            return Schedule(Input.Positions.AsReadOnly(), dependencies);
#pragma warning restore CS0618 
        }

        [Obsolete("Use this.Input to provide data and Schedule(JobHandle) or Run() to collect the result.")]
        public JobHandle Schedule(NativeArray<float2>.ReadOnly positions, JobHandle dependencies)
        {
            if (Settings.ValidateInput)
            {
                RunValidateInputPositions(this, dependencies);
            }

            dependencies = new ClearDataJob(this).Schedule(dependencies);
            dependencies = new RegisterSuperTriangleJob(this, positions).Schedule(dependencies);
            dependencies = new DelaunayTriangulationJob(this, positions).Schedule(dependencies);

            switch (Settings)
            {
                case { ConstrainEdges: true, RefineMesh: false }:
                    dependencies = ScheduleConstrainEdges(dependencies);
                    break;

                case { ConstrainEdges: false, RefineMesh: true }:
                    dependencies = new RefineMeshJob<ConstraintDisable>(this).Schedule(dependencies);
                    break;

                case { ConstrainEdges: true, RefineMesh: true }:
                    dependencies = ScheduleConstrainEdges(dependencies);
                    dependencies = new RefineMeshJob<ConstraintEnable>(this).Schedule(dependencies);
                    break;
            }

            dependencies = new CleanupTrianglesJob(this).Schedule(this, dependencies);
            dependencies = new CleanupPositionsJob(this).Schedule(dependencies);

            return dependencies;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void RunValidateInputPositions(Triangulator @this, JobHandle dependencies)
        {
            using var isValid = new NativeReference<bool>(Allocator.TempJob);
            new ValidateInputPositionsJob(@this, isValid).Schedule(dependencies).Complete();
            if (isValid.Value == false)
            {
                throw new ArgumentException(
                    $"The provided input {nameof(Triangulator.Input.Positions)} is not supported!\n" +
                    $"1. Points count must be greater/equal 3.\n" +
                    $"2. Input positions cannot contain duplicated entries.\n" +
                    $"3. Input positions cannot contain NaN or infinities."
                );
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void RunValidateConstraintEdges(Triangulator @this, JobHandle dependencies)
        {
            using var isValid = new NativeReference<bool>(Allocator.TempJob);
            new ValidateInputConstraintEdges(@this, isValid).Schedule(dependencies).Complete();
            if (isValid.Value == false)
            {
                throw new ArgumentException(
                    $"The provided input {nameof(Triangulator.Input.ConstraintEdges)} is not supported!\n" +
                    $"1. Edges mustn't intersect.\n" +
                    $"2. Edges mustn't be duplicated, e.g. (a0, a1), (a1, a0) cannot be present.\n" +
                    $"3. Edges mustn't intersect point other than two points which they are defined.\n" +
                    $"4. Edges cannot be zero length, i.e. (a0, a0) is forbiden.\n" +
                    $"5. Constraint input buffer must contain even number of elements."
                );
            }
        }

        private JobHandle ScheduleConstrainEdges(JobHandle dependencies)
        {
            if (Settings.ValidateInput)
            {
                RunValidateConstraintEdges(this, dependencies);
            }

            var edges = new NativeList<Edge>(Allocator.TempJob);
            var constraints = new NativeList<Edge>(Allocator.TempJob);
            dependencies = new CopyEdgesJob(this, edges).Schedule(dependencies);
            dependencies = new ResizeEdgeConstraintsJob(this).Schedule(dependencies);
            dependencies = new ConstructConstraintEdgesJob(this).Schedule(data.constraintEdges, Settings.BatchCount, dependencies);
            dependencies = new CopyConstraintsJob(this, constraints).Schedule(dependencies);
            dependencies = new FilterAlreadyConstraintEdges(edges, constraints).Schedule(dependencies);
            dependencies = new ConstrainEdgesJob(this, edges, constraints).Schedule(dependencies);
            dependencies = constraints.Dispose(dependencies);
            dependencies = edges.Dispose(dependencies);

            return dependencies;
        }

        #region Jobs
        [BurstCompile]
        private struct ValidateInputPositionsJob : IJob
        {
            private NativeArray<float2>.ReadOnly positions;
            private NativeReference<bool> isValidRef;

            public ValidateInputPositionsJob(Triangulator triangulator, NativeReference<bool> isValid)
            {
                positions = triangulator.Input.Positions.AsReadOnly();
                isValidRef = isValid;
            }

            public void Execute() => isValidRef.Value = ValidatePositions();

            private bool ValidatePositions()
            {
                if (positions.Length < 3)
                {
                    return false;
                }

                for (int i = 0; i < positions.Length; i++)
                {
                    if (!PointValidation(i) ||
                        !PointPointValidation(i))
                    {
                        return false;
                    }
                }
                return true;
            }

            private bool PointValidation(int i) => math.all(math.isfinite(positions[i]));

            private bool PointPointValidation(int i)
            {
                var pi = positions[i];
                for (int j = i + 1; j < positions.Length; j++)
                {
                    var pj = positions[j];
                    if (math.all(pi == pj))
                    {
                        return false;
                    }
                }
                return true;
            }
        }

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
                data.edgesToTriangles.Add((idA, idB), new FixedList32Bytes<int> { 0 });
                data.edgesToTriangles.Add((idB, idC), new FixedList32Bytes<int> { 0 });
                data.edgesToTriangles.Add((idC, idA), new FixedList32Bytes<int> { 0 });
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
        private struct ValidateInputConstraintEdges : IJob
        {
            private NativeArray<int>.ReadOnly constraints;
            private NativeArray<float2>.ReadOnly positions;
            private NativeReference<bool> isValidRef;

            public ValidateInputConstraintEdges(Triangulator triangulator, NativeReference<bool> isValid)
            {
                constraints = triangulator.Input.ConstraintEdges.AsReadOnly();
                positions = triangulator.Input.Positions.AsReadOnly();
                isValidRef = isValid;
            }

            public void Execute() => isValidRef.Value = ValidateEdgeConstraint();

            private bool ValidateEdgeConstraint()
            {
                if (constraints.Length % 2 == 1)
                {
                    return false;
                }

                for (int i = 0; i < constraints.Length / 2; i++)
                {
                    if (!EdgeValidation(i) ||
                        !EdgePointValidation(i) ||
                        !EdgeEdgeValidation(i))
                    {
                        return false;
                    }
                }

                return true;
            }

            private bool EdgeValidation(int i)
            {
                var (a0Id, a1Id) = (constraints[2 * i], constraints[2 * i + 1]);
                return a0Id != a1Id;
            }

            private bool EdgePointValidation(int i)
            {
                var (a0Id, a1Id) = (constraints[2 * i], constraints[2 * i + 1]);
                var (a0, a1) = (positions[a0Id], positions[a1Id]);

                for (int j = 0; j < positions.Length; j++)
                {
                    if (j == a0Id || j == a1Id)
                    {
                        continue;
                    }

                    var p = positions[j];
                    if (PointLineSegmentIntersection(p, a0, a1))
                    {
                        return false;
                    }
                }

                return true;
            }

            private bool EdgeEdgeValidation(int i)
            {
                var (a0Id, a1Id) = (constraints[2 * i], constraints[2 * i + 1]);
                for (int j = i + 1; j < constraints.Length / 2; j++)
                {
                    var (b0Id, b1Id) = (constraints[2 * j], constraints[2 * j + 1]);
                    if (!ValidatePair(a0Id, a1Id, b0Id, b1Id))
                    {
                        return false;
                    }
                }

                return true;
            }

            private bool ValidatePair(int a0Id, int a1Id, int b0Id, int b1Id)
            {
                // Repeated indicies
                if (a0Id == b0Id && a1Id == b1Id ||
                    a0Id == b1Id && a1Id == b0Id)
                {
                    return false;
                }

                // One common point, cases should be filtered out at EdgePointValidation
                if (a0Id == b0Id || a0Id == b1Id || a1Id == b0Id || a1Id == b1Id)
                {
                    return true;
                }

                var (a0, a1, b0, b1) = (positions[a0Id], positions[a1Id], positions[b0Id], positions[b1Id]);
                if (EdgeEdgeIntersection(a0, a1, b0, b1))
                {
                    return false;
                }

                return true;
            }
        }

        [BurstCompile]
        private struct CopyEdgesJob : IJob
        {
            [ReadOnly]
            private NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles;
            private NativeList<Edge> edges;

            public CopyEdgesJob(Triangulator triangulator, NativeList<Edge> edges)
            {
                edgesToTriangles = triangulator.data.edgesToTriangles;
                this.edges = edges;
            }

            public void Execute()
            {
                using var tmp = edgesToTriangles.GetKeyArray(Allocator.Temp);
                edges.CopyFrom(tmp);
            }
        }

        [BurstCompile]
        private struct CopyConstraintsJob : IJob
        {
            [ReadOnly]
            private NativeArray<Edge> internalConstraints;
            private NativeList<Edge> constraints;

            public CopyConstraintsJob(Triangulator triangulator, NativeList<Edge> constraints)
            {
                internalConstraints = triangulator.data.constraintEdges.AsDeferredJobArray();
                this.constraints = constraints;
            }

            public void Execute()
            {
                constraints.CopyFrom(internalConstraints);
            }
        }

        [BurstCompile]
        private struct ResizeEdgeConstraintsJob : IJob
        {
            private NativeArray<int>.ReadOnly inputConstraintEdges;
            private NativeList<Edge> constraintEdges;

            public ResizeEdgeConstraintsJob(Triangulator triangulator)
            {
                inputConstraintEdges = triangulator.Input.ConstraintEdges.AsReadOnly();
                constraintEdges = triangulator.data.constraintEdges;
            }

            public void Execute()
            {
                constraintEdges.Length = inputConstraintEdges.Length / 2;
            }
        }

        [BurstCompile]
        private struct ConstructConstraintEdgesJob : IJobParallelForDefer
        {
            private NativeArray<int>.ReadOnly inputConstraintEdges;
            private NativeArray<Edge> constraintEdges;

            public ConstructConstraintEdgesJob(Triangulator triangulator)
            {
                inputConstraintEdges = triangulator.Input.ConstraintEdges.AsReadOnly();
                constraintEdges = triangulator.data.constraintEdges.AsDeferredJobArray();
            }

            public void Execute(int index)
            {
                var i = inputConstraintEdges[2 * index];
                var j = inputConstraintEdges[2 * index + 1];
                // Note: +3 due to supertriangle points
                constraintEdges[index] = new Edge(i + 3, j + 3);
            }
        }

        [BurstCompile]
        private struct FilterAlreadyConstraintEdges : IJob
        {
            private NativeList<Edge> edges;
            private NativeList<Edge> constraints;

            public FilterAlreadyConstraintEdges(NativeList<Edge> edges, NativeList<Edge> constraints)
            {
                this.edges = edges;
                this.constraints = constraints;
            }

            public void Execute()
            {
                edges.Sort();
                for (int i = constraints.Length - 1; i >= 0; i--)
                {
                    var id = edges.BinarySearch(constraints[i]);
                    if (id >= 0)
                    {
                        constraints.RemoveAtSwapBack(i);
                    }
                }
            }
        }

        [BurstCompile]
        private struct ConstrainEdgesJob : IJob
        {
            private TriangulatorNativeData data;
            private NativeList<Edge> edges;
            private NativeList<Edge> constraints;

            public ConstrainEdgesJob(Triangulator triangluator, NativeList<Edge> edges, NativeList<Edge> constraints)
            {
                data = triangluator.data;
                this.edges = edges;
                this.constraints = constraints;
            }

            private bool EdgeEdgeIntersection(Edge e1, Edge e2)
            {
                var (a0, a1) = (data.outputPositions[e1.IdA], data.outputPositions[e1.IdB]);
                var (b0, b1) = (data.outputPositions[e2.IdA], data.outputPositions[e2.IdB]);
                return Triangulator.EdgeEdgeIntersection(a0, a1, b0, b1);
            }

            private void CollectIntersections(Edge edge, NativeQueue<Edge> intersections)
            {
                foreach (var otherEdge in edges)
                {
                    if (otherEdge.ContainsCommonPointWith(edge))
                    {
                        continue;
                    }

                    if (EdgeEdgeIntersection(otherEdge, edge))
                    {
                        intersections.Enqueue(otherEdge);
                    }
                }
            }

            public void Execute()
            {
                edges.Sort();
                constraints.Sort();
                using var intersections = new NativeQueue<Edge>(Allocator.Temp);
                for (int i = constraints.Length - 1; i >= 0; i--)
                {
                    intersections.Clear();

                    var c = constraints[i];
                    CollectIntersections(c, intersections);

                    while (intersections.TryDequeue(out var e))
                    {
                        var tris = data.edgesToTriangles[e];
                        var t0 = tris[0];
                        var t1 = tris[1];

                        var q0 = data.triangles[t0].UnsafeOtherPoint(e);
                        var q1 = data.triangles[t1].UnsafeOtherPoint(e);
                        var swapped = new Edge(q0, q1);

                        if (!intersections.IsEmpty())
                        {
                            if (!c.ContainsCommonPointWith(swapped))
                            {
                                if (EdgeEdgeIntersection(c, swapped))
                                {
                                    intersections.Enqueue(e);
                                    continue;
                                }
                            }

                            var (e0, e1) = e;
                            var (p0, p1, p2, p3) = (data.outputPositions[e0], data.outputPositions[q0], data.outputPositions[e1], data.outputPositions[q1]);
                            if (!IsConvexQuadrilateral(p0, p1, p2, p3))
                            {
                                intersections.Enqueue(e);
                                continue;
                            }

                            var id = constraints.BinarySearch(swapped);
                            if (id >= 0)
                            {
                                constraints.RemoveAt(id);
                                i--;
                            }
                        }

                        data.UnsafeSwapEdge(e);
                        var eId = edges.BinarySearch(e);
                        edges[eId] = swapped;
                        edges.Sort();
                    }

                    constraints.RemoveAtSwapBack(i);
                }
            }
        }

        private interface IRefineMeshJobMode
        {
            void SplitEdge(Edge edge, TriangulatorNativeData data);
            void InsertPointAtTriangleCircumcenter(float2 p, int tId, TriangulatorNativeData data);
        }

        private readonly struct ConstraintDisable : IRefineMeshJobMode
        {
            public void InsertPointAtTriangleCircumcenter(float2 p, int tId, TriangulatorNativeData data)
            {
                data.InsertPointAtTriangleCircumcenter(p, tId);
            }

            public void SplitEdge(Edge edge, TriangulatorNativeData data)
            {
                data.SplitEdge(edge);
            }
        }

        private readonly struct ConstraintEnable : IRefineMeshJobMode
        {
            public void InsertPointAtTriangleCircumcenter(float2 p, int tId, TriangulatorNativeData data)
            {
                foreach (var e in data.constraintEdges)
                {
                    var (e0, e1) = (data.outputPositions[e.IdA], data.outputPositions[e.IdB]);
                    var circle = new Circle(0.5f * (e0 + e1), 0.5f * math.distance(e0, e1));
                    if (math.distancesq(circle.Center, p) < circle.RadiusSq)
                    {
                        data.SplitConstraint(e);
                        return;
                    }
                }

                data.InsertPointAtTriangleCircumcenter(p, tId, constraint: true);
            }

            public void SplitEdge(Edge edge, TriangulatorNativeData data)
            {
                if (data.constraintEdges.Contains(edge))
                {
                    data.SplitConstraint(edge);
                }
                else
                {
                    data.SplitEdge(edge, constraint: true);
                }
            }
        }

        [BurstCompile]
        private struct RefineMeshJob<T> : IJob where T : struct, IRefineMeshJobMode
        {
            private TriangulatorNativeData data;
            private readonly float minimumArea2;
            private readonly float maximumArea2;
            private readonly float minimumAngle;
            private readonly T mode;

            public RefineMeshJob(Triangulator triangulator)
            {
                data = triangulator.data;
                minimumArea2 = 2 * triangulator.Settings.MinimumArea;
                maximumArea2 = 2 * triangulator.Settings.MaximumArea;
                minimumAngle = triangulator.Settings.MinimumAngle;
                mode = default;
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
                        if (!SuperTriangle.ContainsCommonPointWith(edge) && data.EdgeIsEncroached(edge))
                        {
                            if (AnyEdgeTriangleAreaIsTooSmall(edge))
                            {
                                continue;
                            }

                            mode.SplitEdge(edge, data);
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

            private bool TryRemoveBadTriangle()
            {
                for (int tId = 0; tId < data.triangles.Length; tId++)
                {
                    var triangle = data.triangles[tId];
                    if (!SuperTriangle.ContainsCommonPointWith(triangle) && !data.TriangleIsEncroached(tId) &&
                        data.TriangleIsBad(triangle, minimumArea2, maximumArea2, minimumAngle))
                    {
                        var circle = data.circles[tId];
                        mode.InsertPointAtTriangleCircumcenter(circle.Center, tId, data);
                        return true;
                    }
                }
                return false;
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
        private static Circle GetCircumcenter(float2 a, float2 b, float2 c)
        {
            var aLenSq = math.lengthsq(a);
            var bLenSq = math.lengthsq(b);
            var cLenSq = math.lengthsq(c);

            var d = 2 * (a.x * (b.y - c.y) + b.x * (c.y - a.y) + c.x * (a.y - b.y));
            var p = math.float2
            (
                x: aLenSq * (b.y - c.y) + bLenSq * (c.y - a.y) + cLenSq * (a.y - b.y),
                y: aLenSq * (c.x - b.x) + bLenSq * (a.x - c.x) + cLenSq * (b.x - a.x)
            ) / d;
            var r = math.distance(p, a);

            return new Circle(center: p, radius: r);
        }
        private static float CCW(float2 a, float2 b, float2 c) => math.sign(Cross(b - a, b - c));
        private static bool PointLineSegmentIntersection(float2 a, float2 b0, float2 b1) =>
            CCW(b0, b1, a) == 0 && math.all(a >= math.min(b0, b1) & a <= math.max(b0, b1));
        private static bool EdgeEdgeIntersection(float2 a0, float2 a1, float2 b0, float2 b1) =>
            CCW(a0, a1, b0) != CCW(a0, a1, b1) && CCW(b0, b1, a0) != CCW(b0, b1, a1);
        private static bool IsConvexQuadrilateral(float2 a, float2 b, float2 c, float2 d) =>
            CCW(a, c, b) != 0 && CCW(a, c, d) != 0 && CCW(b, d, a) != 0 && CCW(b, d, c) != 0 &&
            CCW(a, c, b) != CCW(a, c, d) && CCW(b, d, a) != CCW(b, d, c);
        #endregion
    }
}
