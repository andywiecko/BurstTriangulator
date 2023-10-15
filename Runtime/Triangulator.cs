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
            public bool RefineMesh { get; set; } = false;
            /// <summary>
            /// If <see langword="true"/> constrains edges defined in <see cref="Input"/> using
            /// <see href="https://www.sciencedirect.com/science/article/abs/pii/004579499390239A">Sloan's algorithm</see>.
            /// </summary>
            [field: SerializeField]
            public bool ConstrainEdges { get; set; } = false;
            /// <summary>
            /// If is set to <see langword="true"/>, the provided data will be validated before running the triangulation procedure.
            /// Input positions, as well as input constraints, have a few restrictions, 
            /// see <seealso href="https://github.com/andywiecko/BurstTriangulator/blob/main/README.md">README.md</seealso> for more details.
            /// If one of the conditions fails, then triangulation will not be calculated. 
            /// One could catch this as an error by using <see cref="OutputData.Status"/> (native, can be used in jobs).
            /// </summary>
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
        private NativeList<int> halfedges;
        private NativeList<Circle> circles;
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
            outputTriangles = new(6 * capacity, allocator);
            triangles = new(capacity, allocator);
            halfedges = new(6 * capacity, allocator);
            circles = new(capacity, allocator);
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
            halfedges.Dispose();
            circles.Dispose();
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
            dependencies = Settings.ConstrainEdges ? new ConstrainEdgesJob(this).Schedule(dependencies) : dependencies;
            dependencies = Settings.RefineMesh || Settings.ConstrainEdges ? new RecalculateTriangleMappingsJob(this).Schedule(dependencies) : dependencies;

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
            private NativeList<int> halfedges;
            private NativeList<Circle> circles;
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
                halfedges = triangulator.halfedges;
                circles = triangulator.circles;
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
                halfedges.Clear();
                circles.Clear();
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
            private struct DistComparer : IComparer<int>
            {
                private NativeArray<float> dist;
                public DistComparer(NativeArray<float> dist) => this.dist = dist;
                public int Compare(int x, int y) => dist[x].CompareTo(dist[y]);
            }

            private NativeReference<Status> status;
            [ReadOnly]
            private NativeArray<float2> inputPositions;
            private NativeList<float2> outputPositions;
            private NativeList<Triangle> trianglesRaw;
            private NativeList<int> halfedgesRaw;

            [NativeDisableContainerSafetyRestriction]
            private NativeArray<float2> positions;
            [NativeDisableContainerSafetyRestriction]
            private NativeArray<int> ids;
            [NativeDisableContainerSafetyRestriction]
            private NativeArray<float> dists;
            [NativeDisableContainerSafetyRestriction]
            private NativeArray<int> hullNext;
            [NativeDisableContainerSafetyRestriction]
            private NativeArray<int> hullPrev;
            [NativeDisableContainerSafetyRestriction]
            private NativeArray<int> hullTri;
            [NativeDisableContainerSafetyRestriction]
            private NativeArray<int> hullHash;
            [NativeDisableContainerSafetyRestriction]
            private NativeArray<int> triangles;
            [NativeDisableContainerSafetyRestriction]
            private NativeArray<int> halfedges;
            [NativeDisableContainerSafetyRestriction]
            private NativeArray<int> EDGE_STACK;

            private int hullStart;
            private int trianglesLen;
            private int hashSize;
            private float2 c;

            public DelaunayTriangulationJob(Triangulator triangulator)
            {
                status = triangulator.status;
                inputPositions = triangulator.Input.Positions;
                outputPositions = triangulator.outputPositions;
                trianglesRaw = triangulator.triangles;
                halfedgesRaw = triangulator.halfedges;

                positions = default;
                ids = default;
                dists = default;
                hullNext = default;
                hullPrev = default;
                hullTri = default;
                hullHash = default;
                triangles = default;
                halfedges = default;
                EDGE_STACK = default;

                hullStart = int.MaxValue;
                trianglesLen = 0;
                hashSize = 0;
                c = float.MaxValue;
            }

            private readonly int HashKey(float2 p)
            {
                return (int)math.floor(pseudoAngle(p.x - c.x, p.y - c.y) * hashSize) % hashSize;

                static float pseudoAngle(float dx, float dy)
                {
                    var p = dx / (math.abs(dx) + math.abs(dy));
                    return (dy > 0 ? 3 - p : 1 + p) / 4; // [0..1]
                }
            }

            private void RegisterPointsWithSupertriangle()
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

                outputPositions.Add(pA);
                outputPositions.Add(pB);
                outputPositions.Add(pC);
                outputPositions.AddRange(inputPositions);
            }

            public void Execute()
            {
                if (status.Value == Status.ERR)
                {
                    return;
                }

                RegisterPointsWithSupertriangle();
                positions = outputPositions.AsArray();

                var n = positions.Length;
                var maxTriangles = math.max(2 * n - 5, 0);
                trianglesRaw.Length = maxTriangles;
                triangles = trianglesRaw.AsArray().Reinterpret<int>(3 * sizeof(int));
                halfedgesRaw.Length = 3 * maxTriangles;
                halfedges = halfedgesRaw.AsArray();

                hashSize = (int)math.ceil(math.sqrt(n));
                using var _hullPrev = hullPrev = new(n, Allocator.Temp);
                using var _hullNext = hullNext = new(n, Allocator.Temp);
                using var _hullTri = hullTri = new(n, Allocator.Temp);
                using var _hullHash = hullHash = new(hashSize, Allocator.Temp);

                using var _ids = ids = new(n, Allocator.Temp);
                using var _dists = dists = new(n, Allocator.Temp);

                using var _EDGE_STACK = EDGE_STACK = new(512, Allocator.Temp);

                var min = (float2)float.MaxValue;
                var max = (float2)float.MinValue;

                for (int i = 0; i < positions.Length; i++)
                {
                    var p = positions[i];
                    min = math.min(min, p);
                    max = math.max(max, p);
                    ids[i] = i;
                }

                var center = 0.5f * (min + max);

                int i0 = int.MaxValue, i1 = int.MaxValue, i2 = int.MaxValue;
                var minDistSq = float.MaxValue;
                for (int i = 0; i < positions.Length; i++)
                {
                    var distSq = math.distancesq(center, positions[i]);
                    if (distSq < minDistSq)
                    {
                        i0 = i;
                        minDistSq = distSq;
                    }
                }
                var p0 = positions[i0];

                minDistSq = float.MaxValue;
                for (int i = 0; i < positions.Length; i++)
                {
                    if (i == i0) continue;
                    var distSq = math.distancesq(p0, positions[i]);
                    if (distSq < minDistSq)
                    {
                        i1 = i;
                        minDistSq = distSq;
                    }
                }
                var p1 = positions[i1];

                var minRadius = float.MaxValue;
                for (int i = 0; i < positions.Length; i++)
                {
                    if (i == i0 || i == i1) continue;
                    var p = positions[i];
                    var r = CircumRadiusSq(p0, p1, p);
                    if (r < minRadius)
                    {
                        i2 = i;
                        minRadius = r;
                    }
                }
                var p2 = positions[i2];

                if (minRadius == float.MaxValue)
                {
                    Debug.LogError("[Triangulator]: Provided input is not supported!");
                    status.Value |= Status.ERR;
                    return;
                }

                if (Orient2dFast(p0, p1, p2) < 0)
                {
                    (i1, i2) = (i2, i1);
                    (p1, p2) = (p2, p1);
                }

                c = CircumCenter(p0, p1, p2);
                for (int i = 0; i < positions.Length; i++)
                {
                    dists[i] = math.distancesq(c, positions[i]);
                }

                ids.Sort(new DistComparer(dists));

                hullStart = i0;

                hullNext[i0] = hullPrev[i2] = i1;
                hullNext[i1] = hullPrev[i0] = i2;
                hullNext[i2] = hullPrev[i1] = i0;

                hullTri[i0] = 0;
                hullTri[i1] = 1;
                hullTri[i2] = 2;

                hullHash[HashKey(p0)] = i0;
                hullHash[HashKey(p1)] = i1;
                hullHash[HashKey(p2)] = i2;

                AddTriangle(i0, i1, i2, -1, -1, -1);

                for (var k = 0; k < ids.Length; k++)
                {
                    var i = ids[k];
                    if (i == i0 || i == i1 || i == i2) continue;

                    var p = positions[i];

                    var start = 0;
                    for (var j = 0; j < hashSize; j++)
                    {
                        var key = HashKey(p);
                        start = hullHash[(key + j) % hashSize];
                        if (start != -1 && start != hullNext[start]) break;
                    }

                    start = hullPrev[start];
                    var e = start;
                    var q = hullNext[e];

                    while (Orient2dFast(p, positions[e], positions[q]) >= 0)
                    {
                        e = q;
                        if (e == start)
                        {
                            e = int.MaxValue;
                            break;
                        }

                        q = hullNext[e];
                    }

                    if (e == int.MaxValue) continue;

                    var t = AddTriangle(e, i, hullNext[e], -1, -1, hullTri[e]);
                    hullTri[i] = Legalize(t + 2);
                    hullTri[e] = t;

                    var next = hullNext[e];
                    q = hullNext[next];

                    while (Orient2dFast(p, positions[next], positions[q]) < 0)
                    {
                        t = AddTriangle(next, i, q, hullTri[i], -1, hullTri[next]);
                        hullTri[i] = Legalize(t + 2);
                        hullNext[next] = next;
                        next = q;

                        q = hullNext[next];
                    }

                    if (e == start)
                    {
                        q = hullPrev[e];

                        while (Orient2dFast(p, positions[q], positions[e]) < 0)
                        {
                            t = AddTriangle(q, i, e, -1, hullTri[e], hullTri[q]);
                            Legalize(t + 2);
                            hullTri[q] = t;
                            hullNext[e] = e;
                            e = q;
                            q = hullPrev[e];
                        }
                    }

                    hullStart = hullPrev[i] = e;
                    hullNext[e] = hullPrev[next] = i;
                    hullNext[i] = next;

                    hullHash[HashKey(p)] = i;
                    hullHash[HashKey(positions[e])] = e;
                }

                trianglesRaw.Length = trianglesLen / 3;
                halfedgesRaw.Length = trianglesLen;
            }

            private int Legalize(int a)
            {
                var i = 0;
                int ar;

                while (true)
                {
                    var b = halfedges[a];
                    int a0 = a - a % 3;
                    ar = a0 + (a + 2) % 3;

                    if (b == -1)
                    {
                        if (i == 0) break;
                        a = EDGE_STACK[--i];
                        continue;
                    }

                    var b0 = b - b % 3;
                    var al = a0 + (a + 1) % 3;
                    var bl = b0 + (b + 2) % 3;

                    var p0 = triangles[ar];
                    var pr = triangles[a];
                    var pl = triangles[al];
                    var p1 = triangles[bl];

                    var illegal = InCircle(positions[p0], positions[pr], positions[pl], positions[p1]);

                    if (illegal)
                    {
                        triangles[a] = p1;
                        triangles[b] = p0;

                        var hbl = halfedges[bl];

                        if (hbl == -1)
                        {
                            var e = hullStart;
                            do
                            {
                                if (hullTri[e] == bl)
                                {
                                    hullTri[e] = a;
                                    break;
                                }
                                e = hullPrev[e];
                            } while (e != hullStart);
                        }
                        Link(a, hbl);
                        Link(b, halfedges[ar]);
                        Link(ar, bl);

                        var br = b0 + (b + 1) % 3;

                        if (i < EDGE_STACK.Length)
                        {
                            EDGE_STACK[i++] = br;
                        }
                    }
                    else
                    {
                        if (i == 0) break;
                        a = EDGE_STACK[--i];
                    }
                }

                return ar;
            }

            private int AddTriangle(int i0, int i1, int i2, int a, int b, int c)
            {
                var t = trianglesLen;

                triangles[t + 0] = i0;
                triangles[t + 1] = i1;
                triangles[t + 2] = i2;

                Link(t + 0, a);
                Link(t + 1, b);
                Link(t + 2, c);

                trianglesLen += 3;

                return t;
            }

            private void Link(int a, int b)
            {
                halfedges[a] = b;
                if (b != -1) halfedges[b] = a;
            }
        }

        [BurstCompile]
        private struct RecalculateTriangleMappingsJob : IJob
        {
            [ReadOnly]
            private NativeArray<Triangle> triangles;
            private NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles;
            private NativeList<Circle> circles;
            private NativeList<float2> positions;

            public RecalculateTriangleMappingsJob(Triangulator triangulator)
            {
                triangles = triangulator.triangles.AsDeferredJobArray();
                edgesToTriangles = triangulator.edgesToTriangles;
                circles = triangulator.circles;
                positions = triangulator.outputPositions;
            }

            public void Execute()
            {
                circles.Length = triangles.Length;
                edgesToTriangles.Clear();
                for (int i = 0; i < triangles.Length; i++)
                {
                    Execute(i);
                }
            }

            private void Execute(int t)
            {
                var (i, j, k) = triangles[t];

                circles[t] = CalculateCircumCircle((i, j, k), positions.AsArray());

                RegisterEdgeData((i, j), t);
                RegisterEdgeData((j, k), t);
                RegisterEdgeData((k, i), t);
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
                    edgesToTriangles.Add(edge, new() { triangleId });
                }
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
        private struct ConstrainEdgesJob : IJob
        {
            private NativeReference<Status> status;
            [ReadOnly]
            private NativeArray<float2> outputPositions;
            private NativeArray<Triangle> trianglesRaw;
            [ReadOnly]
            private NativeArray<int> inputConstraintEdges;
            private NativeList<Edge> internalConstraints;
            private NativeArray<int> halfedges;
            private readonly int maxIters;

            [NativeDisableContainerSafetyRestriction]
            private NativeList<int> intersections;
            [NativeDisableContainerSafetyRestriction]
            private NativeList<int> unresolvedIntersections;
            [NativeDisableContainerSafetyRestriction]
            private NativeArray<int> triangles;
            [NativeDisableContainerSafetyRestriction]
            private NativeArray<int> pointToHalfedge;

            public ConstrainEdgesJob(Triangulator triangluator)
            {
                status = triangluator.status;
                outputPositions = triangluator.outputPositions.AsDeferredJobArray();
                trianglesRaw = triangluator.triangles.AsDeferredJobArray();
                maxIters = triangluator.Settings.SloanMaxIters;
                inputConstraintEdges = triangluator.Input.ConstraintEdges;
                internalConstraints = triangluator.constraintEdges;
                halfedges = triangluator.halfedges.AsDeferredJobArray();

                intersections = default;
                unresolvedIntersections = default;
                triangles = default;
                pointToHalfedge = default;
            }

            public void Execute()
            {
                if (status.Value != Status.OK)
                {
                    return;
                }

                using var _intersections = intersections = new NativeList<int>(Allocator.Temp);
                using var _unresolvedIntersections = unresolvedIntersections = new NativeList<int>(Allocator.Temp);
                using var _pointToHalfedge = pointToHalfedge = new NativeArray<int>(outputPositions.Length, Allocator.Temp);
                triangles = trianglesRaw.Reinterpret<int>(3 * sizeof(int));

                // build point to halfedge
                for (int i = 0; i < triangles.Length; i++)
                {
                    var pId = triangles[i];
                    pointToHalfedge[pId] = i;
                }

                BuildInternalConstraints();

                for (int i = internalConstraints.Length - 1; i >= 0; i--) // Reverse order for backward compatibility
                {
                    TryApplyConstraint(i);
                }
            }

            private void BuildInternalConstraints()
            {
                internalConstraints.Length = inputConstraintEdges.Length / 2;
                for (int index = 0; index < internalConstraints.Length; index++)
                {
                    var i = inputConstraintEdges[2 * index + 0];
                    var j = inputConstraintEdges[2 * index + 1];
                    // Note: +3 due to supertriangle points
                    internalConstraints[index] = new Edge(i + 3, j + 3);
                }
                internalConstraints.Sort();
            }

            static int NextHalfedge(int i) => i % 3 == 2 ? i - 2 : i + 1;
            static int PrevHalfedge(int i) => i % 3 == 0 ? i + 2 : i - 1;

            private void TryApplyConstraint(int i)
            {
                intersections.Clear();
                unresolvedIntersections.Clear();

                var c = internalConstraints[i];
                CollectIntersections(c);

                var iter = 0;
                do
                {
                    (intersections, unresolvedIntersections) = (unresolvedIntersections, intersections);
                    TryResolveIntersections(c, ref iter);
                } while (!unresolvedIntersections.IsEmpty);
            }

            private void TryResolveIntersections(Edge c, ref int iter)
            {
                for (int i = 0; i < intersections.Length; i++)
                {
                    if (IsMaxItersExceeded(iter++, maxIters))
                    {
                        return;
                    }

                    //  p                             i
                    //      h2 -----------> h0   h4
                    //      ^             .'   .^:
                    //      :           .'   .'  :
                    //      :         .'   .'    :
                    //      :       .'   .'      :
                    //      :     .'   .'        :
                    //      :   .'   .'          :
                    //      : v'   .'            v
                    //      h1   h3 <----------- h5
                    // j                              q
                    //
                    //  p                             i
                    //      h2   h3 -----------> h4
                    //      ^ '.   ^.            :
                    //      :   '.   '.          :
                    //      :     '.   '.        :
                    //      :       '.   '.      :
                    //      :         '.   '.    :
                    //      :           '.   '.  :
                    //      :             'v   '.v
                    //      h1 <----------- h0   h5
                    // j                              q
                    //
                    // Changes:
                    // ---------------------------------------------
                    //              h0   h1   h2   |   h3   h4   h5
                    // ---------------------------------------------
                    // triangles     i    j    p   |    j    i    q
                    // triangles'   *q*   j    p   |   *p*   i    q
                    // ---------------------------------------------
                    // halfedges    h3   g1   g2   |   h0   f1   f2
                    // halfedges'  *h5'* g1  *h5*  |  *h2'* f1  *h2*, where hi' = halfedge[hi]
                    // ---------------------------------------------
                    // intersec.'    X    X   h3   |    X    X   h0
                    // ---------------------------------------------

                    var h0 = intersections[i];
                    var h1 = NextHalfedge(h0);
                    var h2 = NextHalfedge(h1);

                    var h3 = halfedges[h0];
                    var h4 = NextHalfedge(h3);
                    var h5 = NextHalfedge(h4);

                    var _i = triangles[h0];
                    var _j = triangles[h1];
                    var _p = triangles[h2];
                    var _q = triangles[h5];

                    var (p0, p1, p2, p3) = (outputPositions[_i], outputPositions[_q], outputPositions[_j], outputPositions[_p]);
                    if (!IsConvexQuadrilateral(p0, p1, p2, p3))
                    {
                        unresolvedIntersections.Add(h0);
                        continue;
                    }

                    // Swap edge
                    triangles[h0] = _q;
                    triangles[h3] = _p;
                    pointToHalfedge[_q] = h0;
                    pointToHalfedge[_p] = h3;

                    var h5p = halfedges[h5];
                    if (h5p != -1)
                    {
                        halfedges[h0] = h5p;
                        halfedges[h5p] = h0;
                    }

                    var h2p = halfedges[h2];
                    if (h2p != -1)
                    {
                        halfedges[h3] = h2p;
                        halfedges[h2p] = h3;
                    }

                    halfedges[h2] = h5;
                    halfedges[h5] = h2;

                    // Fix intersections
                    for (int j = i + 1; j < intersections.Length; j++)
                    {
                        var tmp = intersections[j];
                        intersections[j] = tmp == h2 ? h3 : tmp == h5 ? h0 : tmp;
                    }
                    for (int j = 0; j < unresolvedIntersections.Length; j++)
                    {
                        var tmp = unresolvedIntersections[j];
                        unresolvedIntersections[j] = tmp == h2 ? h3 : tmp == h5 ? h0 : tmp;
                    }

                    var swapped = new Edge(_p, _q);
                    if (EdgeEdgeIntersection(c, swapped))
                    {
                        unresolvedIntersections.Add(h2);
                    }
                }

                intersections.Clear();
            }

            private bool EdgeEdgeIntersection(Edge e1, Edge e2)
            {
                var (a0, a1) = (outputPositions[e1.IdA], outputPositions[e1.IdB]);
                var (b0, b1) = (outputPositions[e2.IdA], outputPositions[e2.IdB]);
                return !e1.ContainsCommonPointWith(e2) && Triangulator.EdgeEdgeIntersection(a0, a1, b0, b1);
            }

            private void CollectIntersections(Edge edge)
            {
                // 1. Check if h1 is cj
                // 2. Check if h1-h2 intersects with ci-cj
                // 3. After each iteration: h0 <- h0'
                //
                //          h1
                //       .^ |
                //     .'   |
                //   .'     v
                // h0 <---- h2
                // h0'----> h1'
                //   ^.     |
                //     '.   |
                //       '. v
                //          h2'
                var tunnelInit = -1;
                var (ci, cj) = edge;
                var h0init = pointToHalfedge[ci];
                var h0 = h0init;
                do
                {
                    var h1 = NextHalfedge(h0);
                    if (triangles[h1] == cj)
                    {
                        break;
                    }
                    var h2 = NextHalfedge(h1);
                    if (EdgeEdgeIntersection(edge, new(triangles[h1], triangles[h2])))
                    {
                        unresolvedIntersections.Add(h1);
                        tunnelInit = halfedges[h1];
                        break;
                    }

                    h0 = halfedges[h2];

                    // TODO: go up and down, to resolve boundaries.
                    // This should be done before super-triangle removal.
                    if (h0 == -1)
                    {
                        break;
                    }
                } while (h0 != h0init);

                // Tunnel algorithm
                //
                // h2'
                //  ^'.
                //  |  '.
                //  |    'v
                // h1'<-- h0'
                // h1 --> h2  h1''
                //  ^   .'   ^ |
                //  | .'   .'  |
                //  |v   .'    v
                // h0   h0''<--h2''
                //
                // 1. if h2 == cj break
                // 2. if h1-h2 intersects ci-cj, repeat with h0 <- halfedges[h1] = h0'
                // 3. if h2-h0 intersects ci-cj, repeat with h0 <- halfedges[h2] = h0''
                while (tunnelInit != -1)
                {
                    var h0p = tunnelInit;
                    tunnelInit = -1;
                    var h1p = NextHalfedge(h0p);
                    var h2p = NextHalfedge(h1p);

                    if (triangles[h2p] == cj)
                    {
                        break;
                    }
                    else if (EdgeEdgeIntersection(edge, new(triangles[h1p], triangles[h2p])))
                    {
                        unresolvedIntersections.Add(h1p);
                        tunnelInit = halfedges[h1p];
                    }
                    else if (EdgeEdgeIntersection(edge, new(triangles[h2p], triangles[h0p])))
                    {
                        unresolvedIntersections.Add(h2p);
                        tunnelInit = halfedges[h2p];
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
        }

        private interface IRefineMeshJobMode<TSelf>
        {
            TSelf Create(Triangulator triangulator);
            void SplitEdge(Edge edge);
            void InsertPoint(float2 p, int tId);
        }

        private struct ConstraintDisable : IRefineMeshJobMode<ConstraintDisable>
        {
            [NativeDisableContainerSafetyRestriction]
            private NativeList<float2> outputPositions;
            [NativeDisableContainerSafetyRestriction]
            private NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles;
            [NativeDisableContainerSafetyRestriction]
            private NativeList<Triangle> triangles;
            [NativeDisableContainerSafetyRestriction]
            private NativeList<Circle> circles;
            [NativeDisableContainerSafetyRestriction]
            private NativeList<int> halfedges;

            public ConstraintDisable Create(Triangulator triangulator) => new()
            {
                outputPositions = triangulator.outputPositions,
                edgesToTriangles = triangulator.edgesToTriangles,
                triangles = triangulator.triangles,
                circles = triangulator.circles,
                halfedges = triangulator.halfedges
            };
            public void InsertPoint(float2 p, int tId)
            {
                Triangulator.InsertPoint(p, initTriangles: new() { tId }, triangles, circles, outputPositions, edgesToTriangles, halfedges);
            }

            public void SplitEdge(Edge edge)
            {
                var (e0, e1) = edge;
                var (pA, pB) = (outputPositions[e0], outputPositions[e1]);
                var p = 0.5f * (pA + pB);
                Triangulator.InsertPoint(p, initTriangles: edgesToTriangles[edge], triangles, circles, outputPositions, edgesToTriangles, halfedges);
            }
        }

        private struct ConstraintEnable : IRefineMeshJobMode<ConstraintEnable>
        {
            [NativeDisableContainerSafetyRestriction]
            private NativeList<float2> outputPositions;
            [NativeDisableContainerSafetyRestriction]
            private NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles;
            [NativeDisableContainerSafetyRestriction]
            private NativeList<Triangle> triangles;
            [NativeDisableContainerSafetyRestriction]
            private NativeList<Circle> circles;
            [NativeDisableContainerSafetyRestriction]
            private NativeList<int> halfedges;

            private NativeList<Edge> constraintEdges;

            public ConstraintEnable Create(Triangulator triangulator) => new()
            {
                outputPositions = triangulator.outputPositions,
                edgesToTriangles = triangulator.edgesToTriangles,
                triangles = triangulator.triangles,
                circles = triangulator.circles,
                constraintEdges = triangulator.constraintEdges,
                halfedges = triangulator.halfedges
            };

            public void InsertPoint(float2 p, int tId)
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

                        Triangulator.InsertPoint(circle.Center, initTriangles: edgesToTriangles[e], triangles, circles, outputPositions, constraintEdges, edgesToTriangles, halfedges);
                        return;
                    }
                    eId++;
                }

                Triangulator.InsertPoint(p, initTriangles: new() { tId }, triangles, circles, outputPositions, constraintEdges, edgesToTriangles, halfedges);
            }

            public void SplitEdge(Edge edge)
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
                Triangulator.InsertPoint(p, initTriangles: edgesToTriangles[edge], triangles, circles, outputPositions, constraintEdges, edgesToTriangles, halfedges);
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
            private NativeList<Triangle> triangles;
            private NativeList<float2> outputPositions;
            private NativeList<Circle> circles;
            private NativeList<int> halfedges;

            public RefineMeshJob(Triangulator triangulator)
            {
                minimumArea2 = 2 * triangulator.Settings.MinimumArea;
                maximumArea2 = 2 * triangulator.Settings.MaximumArea;
                minimumAngle = triangulator.Settings.MinimumAngle;
                scaleRef = triangulator.scale.AsReadOnly();
                mode = default(T).Create(triangulator);

                status = triangulator.status.AsReadOnly();
                triangles = triangulator.triangles;
                outputPositions = triangulator.outputPositions;
                circles = triangulator.circles;
                halfedges = triangulator.halfedges;
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
                for (int i = 0; i < triangles.Length; i++)
                {
                    var (t0, t1, t2) = triangles[i];
                    if (TrySplit(new(t0, t1), 3 * i + 0) || TrySplit(new(t1, t2), 3 * i + 1) || TrySplit(new(t2, t0), 3 * i + 2))
                    {
                        return true;
                    }
                }
                return false;
            }

            private bool TrySplit(Edge edge, int he)
            {
                if (!SuperTriangle.ContainsCommonPointWith(edge) && EdgeIsEncroached(edge, he))
                {
                    if (AnyEdgeTriangleAreaIsTooSmall(he))
                    {
                        return false;
                    }

                    mode.SplitEdge(edge);
                    return true;
                }
                return false;
            }

            private bool AnyEdgeTriangleAreaIsTooSmall(int he)
            {
                var ohe = halfedges[he];
                return AreaTooSmall(tId: he / 3) || (ohe != -1 && AreaTooSmall(tId: ohe / 3));
            }

            private bool AreaTooSmall(int tId)
            {
                var s = scaleRef.Value;
                var area2 = triangles[tId].GetArea2(outputPositions);
                return area2 < minimumArea2 * s.x * s.y;
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
                        mode.InsertPoint(circle.Center, tId);
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

            private bool EdgeIsEncroached(Edge edge, int he)
            {
                var (e1, e2) = edge;
                var (pA, pB) = (outputPositions[e1], outputPositions[e2]);
                var circle = new Circle(0.5f * (pA + pB), 0.5f * math.distance(pA, pB));

                var ohe = halfedges[he];
                return IsEncroached(circle, edge, tId: he / 3) || (ohe != -1 && IsEncroached(circle, edge, tId: ohe / 3));
            }

            private bool IsEncroached(Circle circle, Edge edge, int tId)
            {
                var triangle = triangles[tId];
                var pointId = triangle.UnsafeOtherPoint(edge);
                var pC = outputPositions[pointId];
                return math.distancesq(circle.Center, pC) < circle.RadiusSq;
            }

            private bool TriangleIsEncroached(int tId)
            {
                var t = triangles[tId];
                return EdgeIsEncroached(new(t.IdA, t.IdB), 3 * tId + 0)
                    || EdgeIsEncroached(new(t.IdB, t.IdC), 3 * tId + 1)
                    || EdgeIsEncroached(new(t.IdC, t.IdA), 3 * tId + 2);
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
            public PlantHoles Create(Triangulator triangulator) => new() { holeSeeds = triangulator.Input.HoleSeeds };
        }

        private struct PlantBoundaryAndHoles : IPlantingSeedJobMode<PlantBoundaryAndHoles>
        {
            public readonly bool PlantBoundarySeed => true;
            public readonly bool PlantHolesSeed => true;
            public readonly NativeArray<float2> HoleSeeds => holeSeeds;
            private NativeArray<float2> holeSeeds;
            public PlantBoundaryAndHoles Create(Triangulator triangulator) => new() { holeSeeds = triangulator.Input.HoleSeeds };
        }

        [BurstCompile]
        private struct PlantingSeedsJob<T> : IJob where T : struct, IPlantingSeedJobMode<T>
        {
            [ReadOnly]
            private NativeArray<float2> positions;
            private readonly T mode;
            private NativeReference<Status>.ReadOnly status;
            private NativeList<Triangle> triangles;
            private NativeList<float2> outputPositions;
            private NativeList<Circle> circles;
            private NativeList<int> pointsToRemove;
            private NativeList<int> pointsOffset;
            private NativeList<Edge> constraintEdges;
            private NativeList<int> halfedges;

            public PlantingSeedsJob(Triangulator triangulator)
            {
                positions = triangulator.Input.Positions;
                mode = default(T).Create(triangulator);

                status = triangulator.status.AsReadOnly();
                triangles = triangulator.triangles;
                outputPositions = triangulator.outputPositions;
                circles = triangulator.circles;
                pointsToRemove = triangulator.pointsToRemove;
                pointsOffset = triangulator.pointsOffset;
                constraintEdges = triangulator.constraintEdges;
                halfedges = triangulator.halfedges;
            }

            public void Execute()
            {
                if (status.Value != Status.OK)
                {
                    return;
                }

                using var visitedTriangles = new NativeArray<bool>(triangles.Length, Allocator.Temp);
                using var badTriangles = new NativeList<int>(triangles.Length, Allocator.Temp);
                using var trianglesQueue = new NativeQueue<int>(Allocator.Temp);

                PlantSeeds(visitedTriangles, badTriangles, trianglesQueue);

                using var potentialPointsToRemove = new NativeHashSet<int>(initialCapacity: 3 * badTriangles.Length, Allocator.Temp);

                GeneratePotentialPointsToRemove(initialPointsCount: positions.Length, potentialPointsToRemove, badTriangles);
                RemoveBadTriangles(badTriangles, triangles, circles, halfedges);
                GeneratePointsToRemove(potentialPointsToRemove);
                GeneratePointsOffset();
            }

            private void RemoveBadTriangles(NativeList<int> badTriangles, NativeList<Triangle> triangles, NativeList<Circle> circles, NativeList<int> halfedges)
            {
                badTriangles.Sort();
                for (int t = badTriangles.Length - 1; t >= 0; t--)
                {
                    var tId = badTriangles[t];
                    triangles.RemoveAt(tId);
                    circles.RemoveAt(tId);
                    rm_he(3 * tId + 2, 0);
                    rm_he(3 * tId + 1, 1);
                    rm_he(3 * tId + 0, 2);

                    for (int i = 3 * tId; i < halfedges.Length; i++)
                    {
                        var he = halfedges[i];
                        if (he == -1)
                        {
                            continue;
                        }
                        else if (he < 3 * tId)
                        {
                            halfedges[he] -= 3;
                        }
                        else
                        {
                            halfedges[i] -= 3;
                        }
                    }

                    void rm_he(int i, int offset)
                    {
                        var he = halfedges[i];
                        var o = he > 3 * tId ? he - offset : he;
                        if (o > -1)
                        {
                            halfedges[o] = -1;
                        }
                        halfedges.RemoveAt(i);
                    }
                }
            }

            private void PlantSeeds(NativeArray<bool> visitedTriangles, NativeList<int> badTriangles, NativeQueue<int> trianglesQueue)
            {
                if (mode.PlantBoundarySeed)
                {
                    var seed = -1;
                    var itriangles = triangles.AsArray().Reinterpret<int>(3 * sizeof(int));
                    for (int i = 0; i < itriangles.Length; i++)
                    {
                        if (itriangles[i] == 0)
                        {
                            seed = i / 3;
                            break;
                        }
                    }
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
            }

            private void PlantSeed(int tId, NativeArray<bool> visitedTriangles, NativeList<int> badTriangles, NativeQueue<int> trianglesQueue)
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
                    var (t0, t1, t2) = triangles[tId];

                    TryEnqueue(new(t0, t1), 3 * tId + 0, constraintEdges, halfedges);
                    TryEnqueue(new(t1, t2), 3 * tId + 1, constraintEdges, halfedges);
                    TryEnqueue(new(t2, t0), 3 * tId + 2, constraintEdges, halfedges);

                    void TryEnqueue(Edge e, int he, NativeList<Edge> constraintEdges, NativeList<int> halfedges)
                    {
                        var ohe = halfedges[he];
                        if (constraintEdges.Contains(e) || ohe == -1)
                        {
                            return;
                        }

                        var otherId = ohe / 3;
                        if (!visitedTriangles[otherId])
                        {
                            visitedTriangles[otherId] = true;
                            trianglesQueue.Enqueue(otherId);
                            badTriangles.Add(otherId);
                        }
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

            private void GeneratePotentialPointsToRemove(int initialPointsCount, NativeHashSet<int> potentialPointsToRemove, NativeList<int> badTriangles)
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

            private void GeneratePointsToRemove(NativeHashSet<int> potentialPointsToRemove)
            {
                using var tmp = potentialPointsToRemove.ToNativeArray(Allocator.Temp);
                tmp.Sort();
                foreach (var pId in tmp)
                {
                    if (!AnyTriangleContainsPoint(pId))
                    {
                        pointsToRemove.Add(pId);
                    }
                }
            }

            private bool AnyTriangleContainsPoint(int pId)
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

            private void GeneratePointsOffset()
            {
                foreach (var pId in pointsToRemove)
                {
                    for (int i = pId; i < pointsOffset.Length; i++)
                    {
                        pointsOffset[i]--;
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
        private static int InsertPoint(float2 p,
            FixedList128Bytes<int> initTriangles,
            NativeList<Triangle> triangles,
            NativeList<Circle> circles,
            NativeList<float2> outputPositions,
            NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles,
            NativeList<int> halfedges
        ) => UnsafeInsertPoint(p, initTriangles, triangles, circles, outputPositions, constraintEdges: default, edgesToTriangles, halfedges, constraint: false);

        private static int InsertPoint(float2 p,
            FixedList128Bytes<int> initTriangles,
            NativeList<Triangle> triangles,
            NativeList<Circle> circles,
            NativeList<float2> outputPositions,
            NativeList<Edge> constraintEdges,
            NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles,
            NativeList<int> halfedges
        ) => UnsafeInsertPoint(p, initTriangles, triangles, circles, outputPositions, constraintEdges, edgesToTriangles, halfedges, constraint: true);

        private static int UnsafeInsertPoint(float2 p,
            FixedList128Bytes<int> initTriangles,
            NativeList<Triangle> triangles,
            NativeList<Circle> circles,
            NativeList<float2> outputPositions,
            NativeList<Edge> constraintEdges,
            NativeHashMap<Edge, FixedList32Bytes<int>> edgesToTriangles,
            NativeList<int> halfedges,
            bool constraint = false
        )
        {
            var pId = outputPositions.Length;
            outputPositions.Add(p);

            var visitedTriangles = new NativeArray<bool>(triangles.Length, Allocator.Temp);
            using var badTriangles = new NativeList<int>(triangles.Length, Allocator.Temp);
            using var trianglesQueue = new NativeQueue<int>(Allocator.Temp);
            using var pathPoints = new NativeList<int>(Allocator.Temp);
            var pathHalfedges = new NativeList<int>(Allocator.Temp);

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
            pathHalfedges.Dispose();

            return pId;

            void RecalculateBadTriangles(float2 p)
            {
                var itriangles = triangles.AsArray().Reinterpret<int>(3 * sizeof(int));
                while (trianglesQueue.TryDequeue(out var tId))
                {
                    visitedTriangles[tId] = true;

                    VisitEdge(3 * tId + 0, 3 * tId + 1);
                    VisitEdge(3 * tId + 1, 3 * tId + 2);
                    VisitEdge(3 * tId + 2, 3 * tId + 0);


                    void VisitEdge(int t0, int t1)
                    {
                        var e = new Edge(itriangles[t0], itriangles[t1]);
                        if (constraint && constraintEdges.Contains(e))
                        {
                            return;
                        }

                        var he = halfedges[t0];
                        if (he == -1)
                        {
                            return;
                        }

                        var otherId = he / 3;
                        if (visitedTriangles[otherId])
                        {
                            return;
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
            static int NextHalfedge(int i) => i % 3 == 2 ? i - 2 : i + 1;

            void ProcessBadTriangles(int pId)
            {
                // 1. find start
                var id = 3 * badTriangles[0];
                var initHe = -1;
                var itriangles = triangles.AsArray().Reinterpret<int>(3 * sizeof(int));
                while (initHe == -1)
                {
                    id = NextHalfedge(id);
                    var he = halfedges[id];
                    if (he == -1 || !badTriangles.Contains(he / 3))
                    {
                        initHe = id;
                        pathPoints.Add(itriangles[id]);
                        pathHalfedges.Add(he);
                        break;
                    }
                    id = he;
                }

                id = initHe;
                var initPoint = pathPoints[0];

                var maxIter = 1000000;
                var it = 0;
                while (it++ < maxIter)
                {
                    id = NextHalfedge(id);
                    if (itriangles[id] == initPoint)
                    {
                        break;
                    }

                    var he = halfedges[id];
                    if (he == -1 || !badTriangles.Contains(he / 3))
                    {
                        pathPoints.Add(itriangles[id]);
                        pathHalfedges.Add(he);
                        continue;
                    }
                    id = he;
                }

                // 2. remove bad triangles
                badTriangles.Sort();
                for (int t = badTriangles.Length - 1; t >= 0; t--)
                {
                    var tId = badTriangles[t];
                    //triangles.RemoveAt(3 * tId + 2);
                    //triangles.RemoveAt(3 * tId + 1);
                    //triangles.RemoveAt(3 * tId + 0);
                    triangles.RemoveAt(tId);
                    circles.RemoveAt(tId);
                    rm_he(3 * tId + 2, 0);
                    rm_he(3 * tId + 1, 1);
                    rm_he(3 * tId + 0, 2);

                    for (int i = 3 * tId; i < halfedges.Length; i++)
                    {
                        var he = halfedges[i];
                        if (he == -1)
                        {
                            continue;
                        }
                        else if (he < 3 * tId)
                        {
                            halfedges[he] -= 3;
                        }
                        else
                        {
                            halfedges[i] -= 3;
                        }
                    }

                    for (int i = 0; i < pathHalfedges.Length; i++)
                    {
                        if (pathHalfedges[i] > 3 * tId + 2)
                        {
                            pathHalfedges[i] -= 3;
                        }
                    }

                    void rm_he(int i, int offset)
                    {
                        var he = halfedges[i];
                        var o = he > 3 * tId ? he - offset : he;
                        if (o > -1)
                        {
                            halfedges[o] = -1;
                        }
                        halfedges.RemoveAt(i);
                    }
                }

                // 3. Insert point create triangles
                for (int i = 0; i < pathPoints.Length - 1; i++)
                {
                    //triangles.Add(pId);
                    //triangles.Add(pathPoints[i]);
                    //triangles.Add(pathPoints[i + 1]);
                    triangles.Add(new(pId, pathPoints[i], pathPoints[i + 1]));
                    circles.Add(CalculateCircumCircle((pId, pathPoints[i], pathPoints[i + 1]), outputPositions.AsArray()));
                }
                //triangles.Add(pId);
                //triangles.Add(pathPoints[^1]);
                //triangles.Add(pathPoints[0]);
                triangles.Add(new(pId, pathPoints[^1], pathPoints[0]));
                circles.Add(CalculateCircumCircle((pId, pathPoints[^1], pathPoints[0]), outputPositions.AsArray()));

                var heOffset = halfedges.Length;
                halfedges.Length += 3 * pathPoints.Length;
                for (int i = 0; i < pathPoints.Length - 1; i++)
                {
                    var he = pathHalfedges[i];
                    halfedges[3 * i + 1 + heOffset] = pathHalfedges[i];
                    if (he != -1)
                    {
                        halfedges[pathHalfedges[i]] = 3 * i + 1 + heOffset;
                    }
                    halfedges[3 * i + 2 + heOffset] = 3 * i + 3 + heOffset;
                    halfedges[3 * i + 3 + heOffset] = 3 * i + 2 + heOffset;
                }

                var phe = pathHalfedges[^1];
                halfedges[heOffset + 3 * (pathPoints.Length - 1) + 1] = phe;
                if (phe != -1)
                {
                    halfedges[phe] = heOffset + 3 * (pathPoints.Length - 1) + 1;
                }
                halfedges[heOffset] = heOffset + 3 * (pathPoints.Length - 1) + 2;
                halfedges[heOffset + 3 * (pathPoints.Length - 1) + 2] = heOffset;

                // 4. (TMP) recalculate mapping
                edgesToTriangles.Clear();
                for (int tId = 0; tId < triangles.Length; tId++)
                {
                    var t = triangles[tId];
                    RegisterEdgeData(new(t.IdA, t.IdB), tId);
                    RegisterEdgeData(new(t.IdB, t.IdC), tId);
                    RegisterEdgeData(new(t.IdC, t.IdA), tId);
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
        }

        private static float Angle(float2 a, float2 b) => math.atan2(Cross(a, b), math.dot(a, b));
        private static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;
        private static Circle CalculateCircumCircle(Triangle triangle, NativeArray<float2> outputPositions)
        {
            var (idA, idB, idC) = triangle;
            var (pA, pB, pC) = (outputPositions[idA], outputPositions[idB], outputPositions[idC]);
            return new(CircumCenter(pA, pB, pC), CircumRadius(pA, pB, pC));
        }
        private static float CircumRadius(float2 a, float2 b, float2 c) => math.distance(CircumCenter(a, b, c), a);
        private static float CircumRadiusSq(float2 a, float2 b, float2 c) => math.distancesq(CircumCenter(a, b, c), a);
        private static float2 CircumCenter(float2 a, float2 b, float2 c)
        {
            var dx = b.x - a.x;
            var dy = b.y - a.y;
            var ex = c.x - a.x;
            var ey = c.y - a.y;

            var bl = dx * dx + dy * dy;
            var cl = ex * ex + ey * ey;

            var d = 0.5f / (dx * ey - dy * ex);

            var x = a.x + (ey * bl - dy * cl) * d;
            var y = a.y + (dx * cl - ex * bl) * d;

            return new(x, y);
        }
        private static float Orient2dFast(float2 a, float2 b, float2 c) => (a.y - c.y) * (b.x - c.x) - (a.x - c.x) * (b.y - c.y);
        private static bool InCircle(float2 a, float2 b, float2 c, float2 p)
        {
            var dx = a.x - p.x;
            var dy = a.y - p.y;
            var ex = b.x - p.x;
            var ey = b.y - p.y;
            var fx = c.x - p.x;
            var fy = c.y - p.y;

            var ap = dx * dx + dy * dy;
            var bp = ex * ex + ey * ey;
            var cp = fx * fx + fy * fy;

            return dx * (ey * cp - bp * fy) -
                   dy * (ex * cp - bp * fx) +
                   ap * (ex * fy - ey * fx) < 0;
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
