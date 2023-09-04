using System;
using System.Collections.Generic;
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
            public static implicit operator Triangle((int a, int b, int c) ids) => new(ids.a, ids.b, ids.c);
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
            public Triangle WithShift(int i, int j, int k) => (IdA + i, IdB + j, IdC + k);
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
            public readonly float Radius, RadiusSq;
            public Circle(float2 center, float radius) => (Center, Radius, RadiusSq) = (center, radius, radius * radius);
            public void Deconstruct(out float2 center, out float radius) => (center, radius) = (Center, Radius);
        }

        private readonly struct Edge : IEquatable<Edge>, IComparable<Edge>
        {
            public readonly int IdA, IdB;
            public Edge(int idA, int idB) => (IdA, IdB) = idA < idB ? (idA, idB) : (idB, idA);
            public static implicit operator Edge((int a, int b) ids) => new(ids.a, ids.b);
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

            public Iterator GetEnumerator() => new(this);
            public bool Contains(Edge edge) => EdgeA.Equals(edge) || EdgeB.Equals(edge) || EdgeC.Equals(edge);
            public static implicit operator Edge3((Edge eA, Edge eB, Edge eC) v) => new(v.eA, v.eB, v.eC);
        }
        #endregion

        private struct DescendingComparer : IComparer<int>
        {
            public int Compare(int x, int y) => x < y ? 1 : -1;
        }
        private static readonly DescendingComparer comparer = new();

        private static Circle CalculateCircumcenter(Triangle triangle, NativeArray<float2> outputPositions)
        {
            var (idA, idB, idC) = triangle;
            var (pA, pB, pC) = (outputPositions[idA], outputPositions[idB], outputPositions[idC]);
            return GetCircumcenter(pA, pB, pC);
        }

        private static int InsertPoint(float2 p,
            FixedList128Bytes<int> initTriangles,
            NativeList<Triangle> triangles,
            NativeList<Circle> circles,
            NativeList<Edge3> trianglesToEdges,
            NativeList<float2> outputPositions,
            NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles
        ) => UnsafeInsertPoint(p, initTriangles, triangles, circles, trianglesToEdges, outputPositions, constraintEdges: default, edgesToTriangles, constraint: false);

        private static int InsertPoint(float2 p,
            FixedList128Bytes<int> initTriangles,
            NativeList<Triangle> triangles,
            NativeList<Circle> circles,
            NativeList<Edge3> trianglesToEdges,
            NativeList<float2> outputPositions,
            NativeList<Edge> constraintEdges,
            NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles
        ) => UnsafeInsertPoint(p, initTriangles, triangles, circles, trianglesToEdges, outputPositions, constraintEdges, edgesToTriangles, constraint: true);

        private static int UnsafeInsertPoint(float2 p,
            FixedList128Bytes<int> initTriangles,
            NativeList<Triangle> triangles,
            NativeList<Circle> circles,
            NativeList<Edge3> trianglesToEdges,
            NativeList<float2> outputPositions,
            NativeList<Edge> constraintEdges,
            NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles,
            bool constraint = false
        )
        {
            var pId = outputPositions.Length;
            outputPositions.Add(p);

            var visitedTriangles = new NativeArray<bool>(triangles.Length, Allocator.Temp);
            using var badTriangles = new NativeList<int>(triangles.Length, Allocator.Temp);
            using var trianglesQueue = new NativeQueue<int>(Allocator.Temp);
            using var tmpPolygon = new NativeList<Edge>(Allocator.Temp);

            foreach (var tId in initTriangles)
            {
                if (!visitedTriangles[tId])
                {
                    trianglesQueue.Enqueue(tId);
                    badTriangles.Add(tId);
                    RecalculateBadTriangles(p);
                }
            }

            ProcessBadTriangles(pId);

            visitedTriangles.Dispose();
            return pId;

            void RecalculateBadTriangles(float2 p)
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

            void ProcessBadTriangles(int pId)
            {
                CalculateStarPolygon();
                RemoveBadTriangles(badTriangles, triangles, circles, trianglesToEdges, edgesToTriangles);
                CreatePolygonTriangles(pId);

                void CreatePolygonTriangles(int pId)
                {
                    foreach (var (e1, e2) in tmpPolygon.AsReadOnly())
                    {
                        AddTriangle((pId, e1, e2), triangles, outputPositions, circles, trianglesToEdges, edgesToTriangles);
                    }
                }

                void CalculateStarPolygon()
                {
                    tmpPolygon.Clear();
                    foreach (var t1 in badTriangles.AsReadOnly())
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
            }
        }

        private static void RemoveBadTriangles(
            NativeList<int> badTriangles,
            NativeList<Triangle> triangles,
            NativeList<Circle> circles,
            NativeList<Edge3> trianglesToEdges,
            NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles
        )
        {
            badTriangles.Sort(comparer);
            foreach (var tId in badTriangles)
            {
                triangles.RemoveAt(tId);
                circles.RemoveAt(tId);
                trianglesToEdges.RemoveAt(tId);
            }

            edgesToTriangles.Clear();
            for (int tId = 0; tId < triangles.Length; tId++)
            {
                foreach (var edge in trianglesToEdges[tId])
                {
                    RegisterEdgeData(edge, tId);
                }
            }

            void RegisterEdgeData(Edge edge, int triangleId)
            {
                if (edgesToTriangles.TryGetValue(edge, out var tris))
                {
                    tris.Add(triangleId);
                    edgesToTriangles[edge] = tris;
                }
                else
                {
                    edgesToTriangles.Add(edge, new() { triangleId });
                }
            }
        }

        private static void AddTriangle(Triangle t,
            NativeList<Triangle> triangles,
            NativeList<float2> outputPositions,
            NativeList<Circle> circles,
            NativeList<Edge3> trianglesToEdges,
            NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles
        )
        {
            var tId = triangles.Length;
            triangles.Add(t);
            var (idA, idB, idC) = t;
            var circle = CalculateCircumcenter(t, outputPositions.AsArray());
            circles.Add(circle);
            trianglesToEdges.Add(((idA, idB), (idA, idC), (idB, idC)));

            RegisterEdgeData((idA, idB), tId);
            RegisterEdgeData((idB, idC), tId);
            RegisterEdgeData((idC, idA), tId);

            void RegisterEdgeData(Edge edge, int triangleId)
            {
                if (edgesToTriangles.TryGetValue(edge, out var tris))
                {
                    tris.Add(triangleId);
                    edgesToTriangles[edge] = tris;
                }
                else
                {
                    edgesToTriangles.Add(edge, new() { triangleId });
                }
            }
        }

        public enum Status
        {
            /// <summary>
            /// State corresponds to triangulation completed successfully.
            /// </summary>
            OK = 0,
            /// <summary>
            /// State may suggest that some error occurs during triangulation. See console for more details.
            /// </summary>
            ERR = 1,
        }

        public enum Preprocessor
        {
            None = 0,
            /// <summary>
            /// Transforms <see cref="Input"/> to local coordinate system using <em>center of mass</em>.
            /// </summary>
            COM,
            /// <summary>
            /// Transforms <see cref="Input"/> using coordinate system obtained from <em>principal component analysis</em>.
            /// </summary>
            PCA
        }

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
            /// If <see langword="true"/> and provided <see cref="Input"/> is not valid, it will throw an exception.
            /// </summary>
            /// <remarks>
            /// Input validation is enabled only at Editor.
            /// </remarks>
            [field: SerializeField]
            public bool ValidateInput { get; set; } = true;
            /// <summary>
            /// If <see langword="true"/> the mesh boundary is restored using <see cref="Input"/> constraint edges.
            /// </summary>
            [field: SerializeField]
            public bool RestoreBoundary { get; set; } = false;
            /// <summary>
            /// Max iteration count during Sloan's algorithm (constraining edges). 
            /// <b>Modify this only if you know what you are doing.</b>
            /// </summary>
            [field: SerializeField]
            public int SloanMaxIters { get; set; } = 1_000_000;
            /// <summary>
            /// Preprocessing algorithm for the input data. Default is <see cref="Preprocessor.None"/>.
            /// </summary>
            [field: SerializeField]
            public Preprocessor Preprocessor { get; set; } = Preprocessor.None;
        }

        public class InputData
        {
            public NativeArray<float2> Positions { get; set; }
            public NativeArray<int> ConstraintEdges { get; set; }
            public NativeArray<float2> HoleSeeds { get; set; }
        }

        public class OutputData
        {
            public NativeList<float2> Positions => owner.outputPositions;
            public NativeList<int> Triangles => owner.outputTriangles;
            public NativeReference<Status> Status => owner.status;
            private readonly Triangulator owner;
            public OutputData(Triangulator triangulator) => owner = triangulator;
        }

        public TriangulationSettings Settings { get; } = new();
        public InputData Input { get; set; } = new();
        public OutputData Output { get; }

        private static readonly Triangle SuperTriangle = new(0, 1, 2);

        private NativeList<float2> outputPositions;
        private NativeList<int> outputTriangles;
        private NativeList<Triangle> triangles;
        private NativeList<Circle> circles;
        private NativeList<Edge3> trianglesToEdges;
        private NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles;
        private NativeList<Edge> constraintEdges;
        private NativeReference<Status> status;

        private NativeList<int> pointsToRemove;
        private NativeList<int> pointsOffset;

        private NativeArray<float2> tmpInputPositions;
        private NativeArray<float2> tmpInputHoleSeeds;
        private NativeList<float2> localPositions;
        private NativeList<float2> localHoleSeeds;
        private NativeReference<float2> com;
        private NativeReference<float2> scale;
        private NativeReference<float2> pcaCenter;
        private NativeReference<float2x2> pcaMatrix;

        public Triangulator(int capacity, Allocator allocator)
        {
            outputPositions = new(capacity, allocator);
            outputTriangles = new(3 * capacity, allocator);
            triangles = new(capacity, allocator);
            circles = new(capacity, allocator);
            trianglesToEdges = new(capacity, allocator);
            edgesToTriangles = new(capacity, allocator);
            constraintEdges = new(capacity, allocator);
            status = new(Status.OK, allocator);

            pointsToRemove = new(capacity, allocator);
            pointsOffset = new(capacity, allocator);

            localPositions = new(capacity, allocator);
            localHoleSeeds = new(capacity, allocator);
            com = new(allocator);
            scale = new(1, allocator);
            pcaCenter = new(allocator);
            pcaMatrix = new(float2x2.identity, allocator);
            Output = new(this);
        }
        public Triangulator(Allocator allocator) : this(capacity: 16 * 1024, allocator) { }

        public void Dispose()
        {
            outputPositions.Dispose();
            outputTriangles.Dispose();
            triangles.Dispose();
            circles.Dispose();
            trianglesToEdges.Dispose();
            edgesToTriangles.Dispose();
            constraintEdges.Dispose();
            status.Dispose();

            pointsToRemove.Dispose();
            pointsOffset.Dispose();

            localPositions.Dispose();
            localHoleSeeds.Dispose();
            com.Dispose();
            scale.Dispose();
            pcaCenter.Dispose();
            pcaMatrix.Dispose();
        }

        public void Run() => Schedule().Complete();

        public JobHandle Schedule(JobHandle dependencies = default)
        {
            dependencies = new ClearDataJob(this).Schedule(dependencies);

            dependencies = Settings.Preprocessor switch
            {
                Preprocessor.PCA => SchedulePCATransformation(dependencies),
                Preprocessor.COM => ScheduleWorldToLocalTransformation(dependencies),
                Preprocessor.None => dependencies,
                _ => throw new NotImplementedException()
            };

            if (Settings.ValidateInput)
            {
                dependencies = new ValidateInputPositionsJob(this).Schedule(dependencies);
                dependencies = Settings.ConstrainEdges ? new ValidateInputConstraintEdges(this).Schedule(dependencies) : dependencies;
            }

            dependencies = new DelaunayTriangulationJob(this).Schedule(dependencies);
            dependencies = Settings.ConstrainEdges ? ScheduleConstrainEdges(dependencies) : dependencies;

            dependencies = (Settings.RefineMesh, Settings.ConstrainEdges) switch
            {
                (true, true) => new RefineMeshJob<ConstraintEnable>(this).Schedule(dependencies),
                (true, false) => new RefineMeshJob<ConstraintDisable>(this).Schedule(dependencies),
                (false, _) => dependencies
            };

            dependencies = new ResizePointsOffsetsJob(this).Schedule(dependencies);

            var holes = Input.HoleSeeds;
            if (Settings.RestoreBoundary && Settings.ConstrainEdges)
            {
                dependencies = holes.IsCreated ?
                    new PlantingSeedsJob<PlantBoundaryAndHoles>(this).Schedule(dependencies) :
                    new PlantingSeedsJob<PlantBoundary>(this).Schedule(dependencies);
            }
            else if (holes.IsCreated && Settings.ConstrainEdges)
            {
                dependencies = new PlantingSeedsJob<PlantHoles>(this).Schedule(dependencies);
            }

            dependencies = new CleanupTrianglesJob(this).Schedule(this, dependencies);
            dependencies = new CleanupPositionsJob(this).Schedule(dependencies);

            dependencies = Settings.Preprocessor switch
            {
                Preprocessor.PCA => SchedulePCAInverseTransformation(dependencies),
                Preprocessor.COM => ScheduleLocalToWorldTransformation(dependencies),
                Preprocessor.None => dependencies,
                _ => throw new NotImplementedException()
            };

            return dependencies;
        }

        private JobHandle SchedulePCATransformation(JobHandle dependencies)
        {
            tmpInputPositions = Input.Positions;
            Input.Positions = localPositions.AsDeferredJobArray();
            if (Input.HoleSeeds.IsCreated)
            {
                tmpInputHoleSeeds = Input.HoleSeeds;
                Input.HoleSeeds = localHoleSeeds.AsDeferredJobArray();
            }

            dependencies = new PCATransformationJob(this).Schedule(dependencies);
            if (tmpInputHoleSeeds.IsCreated)
            {
                dependencies = new PCATransformationHolesJob(this).Schedule(dependencies);
            }
            return dependencies;
        }

        private JobHandle SchedulePCAInverseTransformation(JobHandle dependencies)
        {
            dependencies = new PCAInverseTransformationJob(this).Schedule(this, dependencies);

            Input.Positions = tmpInputPositions;
            tmpInputPositions = default;

            if (tmpInputHoleSeeds.IsCreated)
            {
                Input.HoleSeeds = tmpInputHoleSeeds;
                tmpInputHoleSeeds = default;
            }

            return dependencies;
        }

        private JobHandle ScheduleWorldToLocalTransformation(JobHandle dependencies)
        {
            tmpInputPositions = Input.Positions;
            Input.Positions = localPositions.AsDeferredJobArray();
            if (Input.HoleSeeds.IsCreated)
            {
                tmpInputHoleSeeds = Input.HoleSeeds;
                Input.HoleSeeds = localHoleSeeds.AsDeferredJobArray();
            }

            dependencies = new InitialLocalTransformationJob(this).Schedule(dependencies);
            if (tmpInputHoleSeeds.IsCreated)
            {
                dependencies = new CalculateLocalHoleSeedsJob(this).Schedule(dependencies);
            }
            dependencies = new CalculateLocalPositionsJob(this).Schedule(this, dependencies);

            return dependencies;
        }

        private JobHandle ScheduleLocalToWorldTransformation(JobHandle dependencies)
        {
            dependencies = new LocalToWorldTransformationJob(this).Schedule(this, dependencies);

            Input.Positions = tmpInputPositions;
            tmpInputPositions = default;

            if (tmpInputHoleSeeds.IsCreated)
            {
                Input.HoleSeeds = tmpInputHoleSeeds;
                tmpInputHoleSeeds = default;
            }

            return dependencies;
        }

        private JobHandle ScheduleConstrainEdges(JobHandle dependencies)
        {
            var edges = new NativeList<Edge>(Allocator.TempJob);
            var constraints = new NativeList<Edge>(Allocator.TempJob);
            dependencies = new CopyEdgesJob(this, edges).Schedule(dependencies);
            dependencies = new ResizeEdgeConstraintsJob(this).Schedule(dependencies);
            dependencies = new ConstructConstraintEdgesJob(this).Schedule(this, dependencies);
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
            [ReadOnly]
            private NativeArray<float2> positions;
            private NativeReference<Status> status;

            public ValidateInputPositionsJob(Triangulator triangulator)
            {
                positions = triangulator.Input.Positions;
                status = triangulator.status;
            }

            public void Execute()
            {
                if (positions.Length < 3)
                {
                    Debug.LogError($"[Triangulator]: Positions.Length is less then 3!");
                    status.Value |= Status.ERR;
                }

                for (int i = 0; i < positions.Length; i++)
                {
                    if (!PointValidation(i))
                    {
                        Debug.LogError($"[Triangulator]: Positions[{i}] does not contain finite value: {positions[i]}!");
                        status.Value |= Status.ERR;
                    }
                    if (!PointPointValidation(i))
                    {
                        status.Value |= Status.ERR;
                    }
                }
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
                        Debug.LogError($"[Triangulator]: Positions[{i}] and [{j}] are duplicated with value: {pi}!");
                        return false;
                    }
                }
                return true;
            }
        }

        [BurstCompile]
        private struct PCATransformationJob : IJob
        {
            [ReadOnly]
            private NativeArray<float2> positions;
            private NativeReference<float2> scaleRef;
            private NativeReference<float2> comRef;
            private NativeReference<float2> cRef;
            private NativeReference<float2x2> URef;

            private NativeList<float2> localPositions;

            public PCATransformationJob(Triangulator triangulator)
            {
                positions = triangulator.tmpInputPositions;
                scaleRef = triangulator.scale;
                comRef = triangulator.com;
                cRef = triangulator.pcaCenter;
                URef = triangulator.pcaMatrix;
                localPositions = triangulator.localPositions;
            }

            public void Execute()
            {
                var n = positions.Length;

                var com = (float2)0;
                foreach (var p in positions)
                {
                    com += p;
                }
                com /= n;
                comRef.Value = com;

                var cov = float2x2.zero;
                foreach (var p in positions)
                {
                    var q = p - com;
                    localPositions.Add(q);
                    cov += Kron(q, q);
                }
                cov /= n;

                Eigen(cov, out _, out var U);
                URef.Value = U;
                for (int i = 0; i < n; i++)
                {
                    localPositions[i] = math.mul(math.transpose(U), localPositions[i]);
                }

                float2 min = float.MaxValue;
                float2 max = float.MinValue;
                foreach (var p in localPositions)
                {
                    min = math.min(p, min);
                    max = math.max(p, max);
                }
                var c = cRef.Value = 0.5f * (min + max);
                var s = scaleRef.Value = 2f / (max - min);

                for (int i = 0; i < n; i++)
                {
                    var p = localPositions[i];
                    localPositions[i] = (p - c) * s;
                }
            }
        }

        [BurstCompile]
        private struct PCATransformationHolesJob : IJob
        {
            [ReadOnly]
            private NativeArray<float2> holeSeeds;
            private NativeList<float2> localHoleSeeds;
            private NativeReference<float2>.ReadOnly scaleRef;
            private NativeReference<float2>.ReadOnly comRef;
            private NativeReference<float2>.ReadOnly cRef;
            private NativeReference<float2x2>.ReadOnly URef;

            public PCATransformationHolesJob(Triangulator triangulator)
            {
                holeSeeds = triangulator.tmpInputHoleSeeds;
                localHoleSeeds = triangulator.localHoleSeeds;
                scaleRef = triangulator.scale.AsReadOnly();
                comRef = triangulator.com.AsReadOnly();
                cRef = triangulator.pcaCenter.AsReadOnly();
                URef = triangulator.pcaMatrix.AsReadOnly();
            }

            public void Execute()
            {
                var com = comRef.Value;
                var s = scaleRef.Value;
                var c = cRef.Value;
                var U = URef.Value;
                var UT = math.transpose(U);

                localHoleSeeds.Resize(holeSeeds.Length, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < holeSeeds.Length; i++)
                {
                    var h = holeSeeds[i];
                    localHoleSeeds[i] = s * (math.mul(UT, h - com) - c);
                }
            }
        }

        [BurstCompile]
        private struct PCAInverseTransformationJob : IJobParallelForDefer
        {
            private NativeArray<float2> positions;
            private NativeReference<float2>.ReadOnly comRef;
            private NativeReference<float2>.ReadOnly scaleRef;
            private NativeReference<float2>.ReadOnly cRef;
            private NativeReference<float2x2>.ReadOnly URef;

            public PCAInverseTransformationJob(Triangulator triangulator)
            {
                positions = triangulator.Output.Positions.AsDeferredJobArray();
                comRef = triangulator.com.AsReadOnly();
                scaleRef = triangulator.scale.AsReadOnly();
                cRef = triangulator.pcaCenter.AsReadOnly();
                URef = triangulator.pcaMatrix.AsReadOnly();
            }

            public JobHandle Schedule(Triangulator triangulator, JobHandle dependencies)
            {
                return this.Schedule(triangulator.Output.Positions, triangulator.Settings.BatchCount, dependencies);
            }

            public void Execute(int i)
            {
                var p = positions[i];
                var com = comRef.Value;
                var s = scaleRef.Value;
                var c = cRef.Value;
                var U = URef.Value;
                positions[i] = math.mul(U, p / s + c) + com;
            }
        }

        [BurstCompile]
        private struct InitialLocalTransformationJob : IJob
        {
            [ReadOnly]
            private NativeArray<float2> positions;
            private NativeReference<float2> comRef;
            private NativeReference<float2> scaleRef;
            private NativeList<float2> localPositions;

            public InitialLocalTransformationJob(Triangulator triangulator)
            {
                positions = triangulator.tmpInputPositions;
                comRef = triangulator.com;
                scaleRef = triangulator.scale;
                localPositions = triangulator.localPositions;
            }

            public void Execute()
            {
                float2 min = 0, max = 0, com = 0;
                foreach (var p in positions)
                {
                    min = math.min(p, min);
                    max = math.max(p, max);
                    com += p;
                }

                com /= positions.Length;
                comRef.Value = com;
                scaleRef.Value = 1 / math.cmax(math.max(math.abs(max - com), math.abs(min - com)));

                localPositions.Resize(positions.Length, NativeArrayOptions.UninitializedMemory);
            }
        }

        [BurstCompile]
        private struct CalculateLocalHoleSeedsJob : IJob
        {
            [ReadOnly]
            private NativeArray<float2> holeSeeds;
            private NativeList<float2> localHoleSeeds;
            private NativeReference<float2>.ReadOnly comRef;
            private NativeReference<float2>.ReadOnly scaleRef;

            public CalculateLocalHoleSeedsJob(Triangulator triangulator)
            {
                holeSeeds = triangulator.tmpInputHoleSeeds;
                localHoleSeeds = triangulator.localHoleSeeds;
                comRef = triangulator.com.AsReadOnly();
                scaleRef = triangulator.scale.AsReadOnly();
            }

            public void Execute()
            {
                var com = comRef.Value;
                var s = scaleRef.Value;

                localHoleSeeds.Resize(holeSeeds.Length, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < holeSeeds.Length; i++)
                {
                    localHoleSeeds[i] = s * (holeSeeds[i] - com);
                }
            }
        }

        [BurstCompile]
        private struct CalculateLocalPositionsJob : IJobParallelForDefer
        {
            private NativeReference<float2>.ReadOnly comRef;
            private NativeReference<float2>.ReadOnly scaleRef;
            private NativeArray<float2> localPositions;
            [ReadOnly]
            private NativeArray<float2> positions;

            public CalculateLocalPositionsJob(Triangulator triangulator)
            {
                comRef = triangulator.com.AsReadOnly();
                scaleRef = triangulator.scale.AsReadOnly();
                localPositions = triangulator.localPositions.AsDeferredJobArray();
                positions = triangulator.tmpInputPositions;
            }

            public JobHandle Schedule(Triangulator triangulator, JobHandle dependencies)
            {
                return this.Schedule(triangulator.localPositions, triangulator.Settings.BatchCount, dependencies);
            }

            public void Execute(int i)
            {
                var p = positions[i];
                var com = comRef.Value;
                var s = scaleRef.Value;
                localPositions[i] = s * (p - com);
            }
        }

        [BurstCompile]
        private struct LocalToWorldTransformationJob : IJobParallelForDefer
        {
            private NativeArray<float2> positions;
            private NativeReference<float2>.ReadOnly comRef;
            private NativeReference<float2>.ReadOnly scaleRef;

            public LocalToWorldTransformationJob(Triangulator triangulator)
            {
                positions = triangulator.Output.Positions.AsDeferredJobArray();
                comRef = triangulator.com.AsReadOnly();
                scaleRef = triangulator.scale.AsReadOnly();
            }

            public JobHandle Schedule(Triangulator triangulator, JobHandle dependencies)
            {
                return this.Schedule(triangulator.Output.Positions, triangulator.Settings.BatchCount, dependencies);
            }

            public void Execute(int i)
            {
                var p = positions[i];
                var com = comRef.Value;
                var s = scaleRef.Value;
                positions[i] = p / s + com;
            }
        }

        [BurstCompile]
        private struct ClearDataJob : IJob
        {
            private NativeReference<float2> scaleRef;
            private NativeReference<float2> comRef;
            private NativeReference<float2> cRef;
            private NativeReference<float2x2> URef;
            private NativeList<float2> outputPositions;
            private NativeList<int> outputTriangles;
            private NativeList<Triangle> triangles;
            private NativeList<Circle> circles;
            private NativeList<Edge3> trianglesToEdges;
            private NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles;
            private NativeList<Edge> constraintEdges;
            private NativeList<int> pointsToRemove;
            private NativeList<int> pointsOffset;
            private NativeReference<Status> status;

            public ClearDataJob(Triangulator triangulator)
            {
                outputPositions = triangulator.outputPositions;
                outputTriangles = triangulator.outputTriangles;
                triangles = triangulator.triangles;
                circles = triangulator.circles;
                trianglesToEdges = triangulator.trianglesToEdges;
                edgesToTriangles = triangulator.edgesToTriangles;
                constraintEdges = triangulator.constraintEdges;
                pointsToRemove = triangulator.pointsToRemove;
                pointsOffset = triangulator.pointsOffset;

                status = triangulator.status;
                scaleRef = triangulator.scale;
                comRef = triangulator.com;
                cRef = triangulator.pcaCenter;
                URef = triangulator.pcaMatrix;
            }
            public void Execute()
            {
                outputPositions.Clear();
                outputTriangles.Clear();
                triangles.Clear();
                circles.Clear();
                trianglesToEdges.Clear();
                edgesToTriangles.Clear();
                constraintEdges.Clear();

                pointsToRemove.Clear();
                pointsOffset.Clear();

                status.Value = Status.OK;
                scaleRef.Value = 1;
                comRef.Value = 0;
                cRef.Value = 0;
                URef.Value = float2x2.identity;
            }
        }

        [BurstCompile]
        private struct DelaunayTriangulationJob : IJob
        {
            [ReadOnly]
            private NativeArray<float2> inputPositions;
            private NativeReference<Status>.ReadOnly status;
            private NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles;
            private NativeList<float2> outputPositions;
            private NativeList<Triangle> triangles;
            private NativeList<Circle> circles;
            private NativeList<Edge3> trianglesToEdges;

            public DelaunayTriangulationJob(Triangulator triangluator)
            {
                inputPositions = triangluator.Input.Positions;
                status = triangluator.status.AsReadOnly();
                edgesToTriangles = triangluator.edgesToTriangles;
                outputPositions = triangluator.outputPositions;
                triangles = triangluator.triangles;
                circles = triangluator.circles;
                trianglesToEdges = triangluator.trianglesToEdges;
            }

            public void Execute()
            {
                if (status.Value != Status.OK)
                {
                    return;
                }

                RegisterSuperTriangle();
                for (int i = 0; i < inputPositions.Length; i++)
                {
                    InsertPoint(inputPositions[i]);
                }
            }

            private FixedList128Bytes<int> SearchForFirstBadTriangle(float2 p)
            {
                for (int tId = 0; tId < triangles.Length; tId++)
                {
                    var circle = circles[tId];
                    if (math.distancesq(circle.Center, p) <= circle.RadiusSq)
                    {
                        return new() { tId };
                    }
                }
                return new();
            }

            private int InsertPoint(float2 p)
            {
                return Triangulator.InsertPoint(p, initTriangles: SearchForFirstBadTriangle(p), triangles, circles, trianglesToEdges, outputPositions, edgesToTriangles);
            }

            private void RegisterSuperTriangle()
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

                AddTriangle(t: (InsertPoint(pA), InsertPoint(pB), InsertPoint(pC)), triangles, outputPositions, circles, trianglesToEdges, edgesToTriangles);
            }
        }

        [BurstCompile]
        private struct ValidateInputConstraintEdges : IJob
        {
            [ReadOnly]
            private NativeArray<int> constraints;
            [ReadOnly]
            private NativeArray<float2> positions;
            private NativeReference<Status> status;

            public ValidateInputConstraintEdges(Triangulator triangulator)
            {
                constraints = triangulator.Input.ConstraintEdges;
                positions = triangulator.Input.Positions;
                status = triangulator.status;
            }

            public void Execute()
            {
                if (constraints.Length % 2 == 1)
                {
                    Debug.LogError($"[Triangulator]: Constraint input buffer does not contain even number of elements!");
                    status.Value |= Status.ERR;
                    return;
                }

                for (int i = 0; i < constraints.Length / 2; i++)
                {
                    if (!EdgePositionsRangeValidation(i) ||
                        !EdgeValidation(i) ||
                        !EdgePointValidation(i) ||
                        !EdgeEdgeValidation(i))
                    {
                        status.Value |= Status.ERR;
                        return;
                    }
                }
            }

            private bool EdgePositionsRangeValidation(int i)
            {
                var (a0Id, a1Id) = (constraints[2 * i], constraints[2 * i + 1]);
                var count = positions.Length;
                if (a0Id >= count || a0Id < 0 || a1Id >= count || a1Id < 0)
                {
                    Debug.LogError($"[Triangulator]: ConstraintEdges[{i}] = ({a0Id}, {a1Id}) is out of range Positions.Length = {count}!");
                    return false;
                }

                return true;
            }

            private bool EdgeValidation(int i)
            {
                var (a0Id, a1Id) = (constraints[2 * i], constraints[2 * i + 1]);
                if (a0Id == a1Id)
                {
                    Debug.LogError($"[Triangulator]: ConstraintEdges[{i}] is length zero!");
                    return false;
                }
                return true;
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
                        Debug.LogError($"[Triangulator]: ConstraintEdges[{i}] and Positions[{j}] are collinear!");
                        return false;
                    }
                }

                return true;
            }

            private bool EdgeEdgeValidation(int i)
            {
                for (int j = i + 1; j < constraints.Length / 2; j++)
                {
                    if (!ValidatePair(i, j))
                    {
                        return false;
                    }
                }

                return true;
            }

            private bool ValidatePair(int i, int j)
            {
                var (a0Id, a1Id) = (constraints[2 * i], constraints[2 * i + 1]);
                var (b0Id, b1Id) = (constraints[2 * j], constraints[2 * j + 1]);

                // Repeated indicies
                if (a0Id == b0Id && a1Id == b1Id ||
                    a0Id == b1Id && a1Id == b0Id)
                {
                    Debug.LogError($"[Triangulator]: ConstraintEdges[{i}] and [{j}] are equivalent!");
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
                    Debug.LogError($"[Triangulator]: ConstraintEdges[{i}] and [{j}] intersect!");
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
            private NativeReference<Status>.ReadOnly status;

            public CopyEdgesJob(Triangulator triangulator, NativeList<Edge> edges)
            {
                edgesToTriangles = triangulator.edgesToTriangles;
                this.edges = edges;
                status = triangulator.status.AsReadOnly();
            }

            public void Execute()
            {
                if (status.Value != Status.OK)
                {
                    return;
                }

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
                internalConstraints = triangulator.constraintEdges.AsDeferredJobArray();
                this.constraints = constraints;
            }

            public void Execute() => constraints.CopyFrom(internalConstraints);
        }

        [BurstCompile]
        private struct ResizeEdgeConstraintsJob : IJob
        {
            [ReadOnly]
            private NativeArray<int> inputConstraintEdges;
            private NativeList<Edge> constraintEdges;
            private NativeReference<Status>.ReadOnly status;

            public ResizeEdgeConstraintsJob(Triangulator triangulator)
            {
                inputConstraintEdges = triangulator.Input.ConstraintEdges;
                constraintEdges = triangulator.constraintEdges;
                status = triangulator.status.AsReadOnly();
            }

            public void Execute()
            {
                if (status.Value != Status.OK)
                {
                    return;
                }

                constraintEdges.Length = inputConstraintEdges.Length / 2;
            }
        }

        [BurstCompile]
        private struct ConstructConstraintEdgesJob : IJobParallelForDefer
        {
            [ReadOnly]
            private NativeArray<int> inputConstraintEdges;
            private NativeArray<Edge> constraintEdges;

            public ConstructConstraintEdgesJob(Triangulator triangulator)
            {
                inputConstraintEdges = triangulator.Input.ConstraintEdges;
                constraintEdges = triangulator.constraintEdges.AsDeferredJobArray();
            }

            public JobHandle Schedule(Triangulator triangulator, JobHandle dependencies) =>
                this.Schedule(triangulator.constraintEdges, triangulator.Settings.BatchCount, dependencies);

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
            private NativeList<Edge> edges, constraints;

            public FilterAlreadyConstraintEdges(NativeList<Edge> edges, NativeList<Edge> constraints) =>
                (this.edges, this.constraints) = (edges, constraints);

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
            private NativeReference<Status> status;
            [ReadOnly]
            private NativeArray<float2> outputPositions;
            private NativeList<Triangle> triangles;
            private NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles;
            private NativeList<Edge3> trianglesToEdges;
            private NativeList<Circle> circles;
            private NativeList<Edge> edges;
            private NativeList<Edge> constraints;
            private readonly int maxIters;

            public ConstrainEdgesJob(Triangulator triangluator, NativeList<Edge> edges, NativeList<Edge> constraints)
            {
                status = triangluator.status;
                outputPositions = triangluator.outputPositions.AsDeferredJobArray();
                triangles = triangluator.triangles;
                edgesToTriangles = triangluator.edgesToTriangles;
                trianglesToEdges = triangluator.trianglesToEdges;
                circles = triangluator.circles;
                this.edges = edges;
                this.constraints = constraints;
                maxIters = triangluator.Settings.SloanMaxIters;
            }

            private bool EdgeEdgeIntersection(Edge e1, Edge e2)
            {
                var (a0, a1) = (outputPositions[e1.IdA], outputPositions[e1.IdB]);
                var (b0, b1) = (outputPositions[e2.IdA], outputPositions[e2.IdB]);
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

            private bool IsMaxItersExceeded(int iter, int maxIters)
            {
                if (iter >= maxIters)
                {
                    Debug.LogError(
                        $"[Triangulator]: Sloan max iterations exceeded! This may suggest that input data is hard to resolve by Sloan's algorithm. " +
                        $"It usually happens when the scale of the input positions is not uniform. " +
                        $"Please try to post-process input data or increase {nameof(Settings.SloanMaxIters)} value."
                    );
                    status.Value |= Status.ERR;
                    return true;
                }
                return false;
            }

            private void UnsafeSwapEdge(Edge edge)
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
                edgesToTriangles.Add((q0, q1), new() { t0, t1 });

                circles[t0] = CalculateCircumcenter(triangles[t0], outputPositions);
                circles[t1] = CalculateCircumcenter(triangles[t1], outputPositions);
            }

            public void Execute()
            {
                if (status.Value != Status.OK)
                {
                    return;
                }

                edges.Sort();
                constraints.Sort();
                using var intersections = new NativeQueue<Edge>(Allocator.Temp);
                for (int i = constraints.Length - 1; i >= 0; i--)
                {
                    intersections.Clear();

                    var c = constraints[i];
                    CollectIntersections(c, intersections);

                    var iter = 0;
                    while (intersections.TryDequeue(out var e))
                    {
                        if (IsMaxItersExceeded(iter++, maxIters))
                        {
                            return;
                        }

                        var tris = edgesToTriangles[e];
                        var t0 = tris[0];
                        var t1 = tris[1];

                        var q0 = triangles[t0].UnsafeOtherPoint(e);
                        var q1 = triangles[t1].UnsafeOtherPoint(e);
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
                            var (p0, p1, p2, p3) = (outputPositions[e0], outputPositions[q0], outputPositions[e1], outputPositions[q1]);
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

                        UnsafeSwapEdge(e);
                        var eId = edges.BinarySearch(e);
                        edges[eId] = swapped;
                        edges.Sort();
                    }

                    constraints.RemoveAtSwapBack(i);
                }
            }
        }

        private interface IRefineMeshJobMode<TSelf>
        {
            TSelf Create(Triangulator triangulator);
            void SplitEdge(Edge edge,
                NativeList<float2> outputPositions,
                NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles,
                NativeList<Triangle> triangles,
                NativeList<Circle> circles,
                NativeList<Edge3> trianglesToEdges
            );
            void InsertPoint(float2 p, int tId,
                NativeList<float2> outputPositions,
                NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles,
                NativeList<Triangle> triangles,
                NativeList<Circle> circles,
                NativeList<Edge3> trianglesToEdges
            );
        }

        private readonly struct ConstraintDisable : IRefineMeshJobMode<ConstraintDisable>
        {
            public ConstraintDisable Create(Triangulator _) => new();
            public void InsertPoint(float2 p, int tId,
                NativeList<float2> outputPositions,
                NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles,
                NativeList<Triangle> triangles,
                NativeList<Circle> circles,
                NativeList<Edge3> trianglesToEdges)
            {
                Triangulator.InsertPoint(p, initTriangles: new() { tId }, triangles, circles, trianglesToEdges, outputPositions, edgesToTriangles);
            }

            public void SplitEdge(Edge edge,
                NativeList<float2> outputPositions,
                NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles,
                NativeList<Triangle> triangles,
                NativeList<Circle> circles,
                NativeList<Edge3> trianglesToEdges
            )
            {
                var (e0, e1) = edge;
                var (pA, pB) = (outputPositions[e0], outputPositions[e1]);
                var p = 0.5f * (pA + pB);
                Triangulator.InsertPoint(p, initTriangles: edgesToTriangles[edge], triangles, circles, trianglesToEdges, outputPositions, edgesToTriangles);
            }
        }

        private struct ConstraintEnable : IRefineMeshJobMode<ConstraintEnable>
        {
            private NativeList<Edge> constraintEdges;
            public ConstraintEnable(Triangulator triangulator) => constraintEdges = triangulator.constraintEdges;
            public ConstraintEnable Create(Triangulator triangulator) => new(triangulator);

            public void InsertPoint(float2 p, int tId,
                NativeList<float2> outputPositions,
                NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles,
                NativeList<Triangle> triangles,
                NativeList<Circle> circles,
                NativeList<Edge3> trianglesToEdges
                )
            {
                var eId = 0;
                foreach (var e in constraintEdges)
                {
                    var (e0, e1) = (outputPositions[e.IdA], outputPositions[e.IdB]);
                    var circle = new Circle(0.5f * (e0 + e1), 0.5f * math.distance(e0, e1));
                    if (math.distancesq(circle.Center, p) < circle.RadiusSq)
                    {
                        var pId = outputPositions.Length;
                        constraintEdges.RemoveAt(eId);
                        constraintEdges.Add((pId, e.IdA));
                        constraintEdges.Add((pId, e.IdB));

                        Triangulator.InsertPoint(circle.Center, initTriangles: edgesToTriangles[e], triangles, circles, trianglesToEdges, outputPositions, constraintEdges, edgesToTriangles);
                        return;
                    }
                    eId++;
                }

                Triangulator.InsertPoint(p, initTriangles: new() { tId }, triangles, circles, trianglesToEdges, outputPositions, constraintEdges, edgesToTriangles);
            }

            public void SplitEdge(Edge edge,
                NativeList<float2> outputPositions,
                NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles,
                NativeList<Triangle> triangles,
                NativeList<Circle> circles,
                NativeList<Edge3> trianglesToEdges
            )
            {
                var (e0, e1) = edge;
                var (pA, pB) = (outputPositions[e0], outputPositions[e1]);
                var p = 0.5f * (pA + pB);

                if (constraintEdges.Contains(edge))
                {
                    var pId = outputPositions.Length;
                    var eId = constraintEdges.IndexOf(edge);
                    constraintEdges.RemoveAt(eId);
                    constraintEdges.Add((pId, edge.IdA));
                    constraintEdges.Add((pId, edge.IdB));
                }
                Triangulator.InsertPoint(p, initTriangles: edgesToTriangles[edge], triangles, circles, trianglesToEdges, outputPositions, constraintEdges, edgesToTriangles);
            }
        }

        [BurstCompile]
        private struct RefineMeshJob<T> : IJob where T : struct, IRefineMeshJobMode<T>
        {
            private readonly float minimumArea2;
            private readonly float maximumArea2;
            private readonly float minimumAngle;
            private NativeReference<float2>.ReadOnly scaleRef;
            private readonly T mode;
            private NativeReference<Status>.ReadOnly status;
            private NativeList<Edge3> trianglesToEdges;
            private NativeList<Triangle> triangles;
            private NativeList<float2> outputPositions;
            private NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles;
            private NativeList<Circle> circles;

            public RefineMeshJob(Triangulator triangulator)
            {
                minimumArea2 = 2 * triangulator.Settings.MinimumArea;
                maximumArea2 = 2 * triangulator.Settings.MaximumArea;
                minimumAngle = triangulator.Settings.MinimumAngle;
                scaleRef = triangulator.scale.AsReadOnly();
                mode = default(T).Create(triangulator);

                status = triangulator.status.AsReadOnly();
                trianglesToEdges = triangulator.trianglesToEdges;
                triangles = triangulator.triangles;
                outputPositions = triangulator.outputPositions;
                edgesToTriangles = triangulator.edgesToTriangles;
                circles = triangulator.circles;
            }

            public void Execute()
            {
                if (status.Value != Status.OK)
                {
                    return;
                }

                while (TrySplitEncroachedEdge() || TryRemoveBadTriangle()) { }
            }

            private bool TrySplitEncroachedEdge()
            {
                foreach (var edge3 in trianglesToEdges)
                {
                    foreach (var edge in edge3)
                    {
                        if (!SuperTriangle.ContainsCommonPointWith(edge) && EdgeIsEncroached(edge))
                        {
                            if (AnyEdgeTriangleAreaIsTooSmall(edge))
                            {
                                continue;
                            }

                            mode.SplitEdge(edge, outputPositions, edgesToTriangles, triangles, circles, trianglesToEdges);
                            return true;
                        }
                    }
                }

                return false;
            }

            private bool AnyEdgeTriangleAreaIsTooSmall(Edge edge)
            {
                var s = scaleRef.Value;
                foreach (var tId in edgesToTriangles[edge])
                {
                    var area2 = triangles[tId].GetArea2(outputPositions);
                    if (area2 < minimumArea2 * s.x * s.y)
                    {
                        return true;
                    }
                }
                return false;
            }

            private bool TryRemoveBadTriangle()
            {
                var s = scaleRef.Value;
                for (int tId = 0; tId < triangles.Length; tId++)
                {
                    var triangle = triangles[tId];
                    if (!SuperTriangle.ContainsCommonPointWith(triangle) && !TriangleIsEncroached(tId) &&
                        TriangleIsBad(triangle, minimumArea2 * s.x * s.y, maximumArea2 * s.x * s.y, minimumAngle))
                    {
                        var circle = circles[tId];
                        mode.InsertPoint(circle.Center, tId, outputPositions, edgesToTriangles, triangles, circles, trianglesToEdges);
                        return true;
                    }
                }
                return false;
            }

            private bool TriangleIsBad(Triangle triangle, float minimumArea2, float maximumArea2, float minimumAngle)
            {
                var area2 = triangle.GetArea2(outputPositions);
                return area2 >= minimumArea2 && (area2 > maximumArea2 || AngleIsTooSmall(triangle, minimumAngle));
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
        }

        [BurstCompile]
        private struct ResizePointsOffsetsJob : IJob
        {
            private NativeList<int> pointsOffset;
            [ReadOnly]
            private NativeArray<float2> outputPositions;

            public ResizePointsOffsetsJob(Triangulator triangulator)
            {
                pointsOffset = triangulator.pointsOffset;
                outputPositions = triangulator.outputPositions.AsDeferredJobArray();
            }
            public void Execute()
            {
                pointsOffset.Clear();
                pointsOffset.Length = outputPositions.Length;
            }
        }

        private interface IPlantingSeedJobMode<TSelf>
        {
            TSelf Create(Triangulator triangulator);
            bool PlantBoundarySeed { get; }
            bool PlantHolesSeed { get; }
            NativeArray<float2> HoleSeeds { get; }
        }

        private readonly struct PlantBoundary : IPlantingSeedJobMode<PlantBoundary>
        {
            public bool PlantBoundarySeed => true;
            public bool PlantHolesSeed => false;
            public NativeArray<float2> HoleSeeds => default;
            public PlantBoundary Create(Triangulator _) => new();
        }

        private struct PlantHoles : IPlantingSeedJobMode<PlantHoles>
        {
            public readonly bool PlantBoundarySeed => false;
            public readonly bool PlantHolesSeed => true;
            public readonly NativeArray<float2> HoleSeeds => holeSeeds;
            private NativeArray<float2> holeSeeds;
            public PlantHoles Create(Triangulator triangulator) =>
                new() { holeSeeds = triangulator.Input.HoleSeeds };
        }

        private struct PlantBoundaryAndHoles : IPlantingSeedJobMode<PlantBoundaryAndHoles>
        {
            public readonly bool PlantBoundarySeed => true;
            public readonly bool PlantHolesSeed => true;
            public readonly NativeArray<float2> HoleSeeds => holeSeeds;
            private NativeArray<float2> holeSeeds;
            public PlantBoundaryAndHoles Create(Triangulator triangulator) =>
                new() { holeSeeds = triangulator.Input.HoleSeeds };
        }

        [BurstCompile]
        private struct PlantingSeedsJob<T> : IJob where T : struct, IPlantingSeedJobMode<T>
        {
            [ReadOnly]
            private NativeArray<float2> positions;
            private readonly T mode;
            private NativeReference<Status>.ReadOnly status;
            private NativeList<Triangle> triangles;
            private NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles;
            private NativeList<float2> outputPositions;
            private NativeList<Circle> circles;
            private NativeList<Edge3> trianglesToEdges;
            private NativeList<int> pointsToRemove;
            private NativeList<int> pointsOffset;
            private NativeList<Edge> constraintEdges;

            public PlantingSeedsJob(Triangulator triangulator)
            {
                positions = triangulator.Input.Positions;
                mode = default(T).Create(triangulator);

                status = triangulator.status.AsReadOnly();
                triangles = triangulator.triangles;
                edgesToTriangles = triangulator.edgesToTriangles;
                outputPositions = triangulator.outputPositions;
                circles = triangulator.circles;
                trianglesToEdges = triangulator.trianglesToEdges;
                pointsToRemove = triangulator.pointsToRemove;
                pointsOffset = triangulator.pointsOffset;
                constraintEdges = triangulator.constraintEdges;
            }

            public void Execute()
            {
                if (status.Value != Status.OK)
                {
                    return;
                }

                var badTriangles = new NativeList<int>(triangles.Length, Allocator.Temp);
                using var visitedTriangles = new NativeArray<bool>(triangles.Length, Allocator.Temp);
                using var trianglesQueue = new NativeQueue<int>(Allocator.Temp);

                if (mode.PlantBoundarySeed)
                {
                    var seed = edgesToTriangles[(0, 1)][0];
                    PlantSeed(seed, visitedTriangles, badTriangles, trianglesQueue);
                }

                if (mode.PlantHolesSeed)
                {
                    foreach (var s in mode.HoleSeeds)
                    {
                        var tId = FindTriangle(s);
                        if (tId != -1)
                        {
                            PlantSeed(tId, visitedTriangles, badTriangles, trianglesQueue);
                        }
                    }
                }

                using var potentialPointsToRemove = new NativeHashSet<int>(initialCapacity: 3 * badTriangles.Length, Allocator.Temp);
                GeneratePotentialPointsToRemove(initialPointsCount: positions.Length, triangles);

                RemoveBadTriangles(badTriangles, triangles, circles, trianglesToEdges, edgesToTriangles);

                GeneratePointsToRemove(potentialPointsToRemove);
                GeneratePointsOffset();

                badTriangles.Dispose();

                void GeneratePotentialPointsToRemove(int initialPointsCount, NativeList<Triangle> triangles)
                {
                    foreach (var tId in badTriangles.AsReadOnly())
                    {
                        var (idA, idB, idC) = triangles[tId];
                        TryAddPotentialPointToRemove(idA, initialPointsCount);
                        TryAddPotentialPointToRemove(idB, initialPointsCount);
                        TryAddPotentialPointToRemove(idC, initialPointsCount);
                    }

                    void TryAddPotentialPointToRemove(int id, int initialPointsCount)
                    {
                        if (id >= initialPointsCount + 3 /* super triangle */)
                        {
                            potentialPointsToRemove.Add(id);
                        }
                    }
                }
            }

            public void GeneratePointsToRemove(NativeHashSet<int> potentialPointsToRemove)
            {
                var tmp = potentialPointsToRemove.ToNativeArray(Allocator.Temp);
                tmp.Sort();
                foreach (var pId in tmp)
                {
                    if (!AnyTriangleContainsPoint(pId, triangles))
                    {
                        pointsToRemove.Add(pId);
                    }
                }

                bool AnyTriangleContainsPoint(int pId, NativeList<Triangle> triangles)
                {
                    foreach (var t in triangles)
                    {
                        if (t.Contains(pId))
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }

            public void GeneratePointsOffset()
            {
                foreach (var pId in pointsToRemove)
                {
                    for (int i = pId; i < pointsOffset.Length; i++)
                    {
                        pointsOffset[i]--;
                    }
                }
            }

            private int FindTriangle(float2 p)
            {
                var tId = 0;
                foreach (var (idA, idB, idC) in triangles)
                {
                    var (a, b, c) = (outputPositions[idA], outputPositions[idB], outputPositions[idC]);
                    if (PointInsideTriangle(p, a, b, c))
                    {
                        return tId;
                    }
                    tId++;
                }

                return -1;
            }

            public void PlantSeed(int tId, NativeArray<bool> visitedTriangles, NativeList<int> badTriangles, NativeQueue<int> trianglesQueue)
            {
                if (visitedTriangles[tId])
                {
                    return;
                }

                visitedTriangles[tId] = true;
                trianglesQueue.Enqueue(tId);
                badTriangles.Add(tId);

                while (trianglesQueue.TryDequeue(out tId))
                {
                    foreach (var e in trianglesToEdges[tId])
                    {
                        if (constraintEdges.Contains(e))
                        {
                            continue;
                        }

                        foreach (var otherId in edgesToTriangles[e])
                        {
                            if (!visitedTriangles[otherId])
                            {
                                visitedTriangles[otherId] = true;
                                trianglesQueue.Enqueue(otherId);
                                badTriangles.Add(otherId);
                            }
                        }
                    }
                }
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
            [ReadOnly]
            private NativeArray<int> pointsOffset;
            private NativeReference<Status>.ReadOnly status;

            public CleanupTrianglesJob(Triangulator triangulator)
            {
                triangles = triangulator.triangles;
                outputPositions = triangulator.outputPositions;
                outputTriangles = triangulator.outputTriangles.AsParallelWriter();
                pointsOffset = triangulator.pointsOffset.AsDeferredJobArray();
                status = triangulator.status.AsReadOnly();
            }

            public JobHandle Schedule(Triangulator triangulator, JobHandle dependencies)
            {
                return this.Schedule(triangulator.triangles, triangulator.Settings.BatchCount, dependencies);
            }

            public void Execute(int i)
            {
                if (status.Value != Status.OK)
                {
                    return;
                }

                var t = triangles[i];
                if (t.ContainsCommonPointWith(SuperTriangle))
                {
                    return;
                }
                t = t.GetSignedArea2(outputPositions) < 0 ? t : t.Flip();
                var (idA, idB, idC) = t;
                t = t.WithShift(-3); // Due to supertriangle verticies.
                t = t.WithShift(pointsOffset[idA], pointsOffset[idB], pointsOffset[idC]);
                var ptr = UnsafeUtility.AddressOf(ref t);
                outputTriangles.AddRangeNoResize(ptr, 3);
            }
        }

        [BurstCompile]
        private struct CleanupPositionsJob : IJob
        {
            private NativeList<float2> positions;
            [ReadOnly]
            private NativeArray<int> pointsToRemove;
            private NativeReference<Status>.ReadOnly status;

            public CleanupPositionsJob(Triangulator triangulator)
            {
                positions = triangulator.outputPositions;
                pointsToRemove = triangulator.pointsToRemove.AsDeferredJobArray();
                status = triangulator.status.AsReadOnly();
            }

            public void Execute()
            {
                if (status.Value != Status.OK)
                {
                    return;
                }

                for (int i = pointsToRemove.Length - 1; i >= 0; i--)
                {
                    positions.RemoveAt(pointsToRemove[i]);
                }

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
        private static float3 Barycentric(float2 a, float2 b, float2 c, float2 p)
        {
            var (v0, v1, v2) = (b - a, c - a, p - a);
            var denInv = 1 / Cross(v0, v1);
            var v = denInv * Cross(v2, v1);
            var w = denInv * Cross(v0, v2);
            var u = 1.0f - v - w;
            return math.float3(u, v, w);
        }
        private static void Eigen(float2x2 matrix, out float2 eigval, out float2x2 eigvec)
        {
            var a00 = matrix[0][0];
            var a11 = matrix[1][1];
            var a01 = matrix[0][1];

            var a00a11 = a00 - a11;
            var p1 = a00 + a11;
            var p2 = (a00a11 >= 0 ? 1 : -1) * math.sqrt(a00a11 * a00a11 + 4 * a01 * a01);
            var lambda1 = p1 + p2;
            var lambda2 = p1 - p2;
            eigval = 0.5f * math.float2(lambda1, lambda2);

            var phi = 0.5f * math.atan2(2 * a01, a00a11);

            eigvec = math.float2x2
            (
                m00: math.cos(phi), m01: -math.sin(phi),
                m10: math.sin(phi), m11: math.cos(phi)
            );
        }
        private static float2x2 Kron(float2 a, float2 b) => math.float2x2(a * b[0], a * b[1]);
        private static bool PointInsideTriangle(float2 p, float2 a, float2 b, float2 c) => math.cmax(-Barycentric(a, b, c, p)) <= 0;
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
