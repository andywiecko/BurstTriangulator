using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;

namespace andywiecko.BurstTriangulator
{
    /** Supports rotation, scaling and translation in 2D */
    public struct AffineTransform2D {
        float2x2 rotScale;
        float2 translation;

        public static readonly AffineTransform2D identity = new AffineTransform2D { rotScale = float2x2.identity, translation = float2.zero };

        // R(T + x) = y
        // Solve for x:
        // T + x = R^-1(y)
        // x = R^-1(y) - T
        // x = R^-1(y - RT)
        // x = R^-1(y + R(-T))
        public AffineTransform2D inverse => new AffineTransform2D { rotScale = math.inverse(rotScale), translation = math.mul(rotScale, -translation) };

        /** How much the area of a shape is scaled by this transformation */
        public float areaScalingFactor => math.abs(math.determinant(rotScale));

        public float2 Transform (float2 point) => math.mul(rotScale, point + translation);

        public static AffineTransform2D Translate(float2 offset) => new AffineTransform2D { rotScale = float2x2.identity, translation = offset };
        public static AffineTransform2D Scale(float2 scale) => new AffineTransform2D { rotScale = new float2x2(scale.x, 0, 0, scale.y), translation = float2.zero };
        public static AffineTransform2D Rotate(float2x2 rotation) => new AffineTransform2D { rotScale = rotation, translation = float2.zero };

        // result.transform(x) = R1(T1 + R2(x + T2)) = R1((T1 + R2T2) + R2(x)) = R1*R2(R2^-1(T1 + R2T2) + x) = R1*R2(R2^-1(T1) + T2 + x)
        public static AffineTransform2D operator * (AffineTransform2D lhs, AffineTransform2D rhs) => new AffineTransform2D {
            rotScale = math.mul(lhs.rotScale, rhs.rotScale),
            translation = math.mul(math.inverse(rhs.rotScale), lhs.translation) + rhs.translation
        };

        public void Transform(NativeArray<float2> points) {
            for (int i = 0; i < points.Length; i++) points[i] = Transform(points[i]);
        }

        public void Transform([NoAlias] NativeArray<float2> points, [NoAlias] NativeArray<float2> outPoints) {
            if (points.Length != outPoints.Length) throw new ArgumentException("Input and output arrays must have the same length");
            for (int i = 0; i < points.Length; i++) outPoints[i] = Transform(points[i]);
        }

        /** Applies the inverse transform to all points, in-place.
         *
         * This is mathematically equivalent to calling `this.inverse.Transform(points, outPoints)`,
         * but it is less susceptible to floating point errors.
         */
        public void InverseTransform([NoAlias] NativeArray<float2> points, [NoAlias] NativeArray<float2> outPoints) {
            var invRotScale = math.inverse(rotScale);
            for (int i = 0; i < points.Length; i++) outPoints[i] = math.mul(invRotScale, points[i]) - translation;
        }

		public override string ToString() => $"RigidTransform(rotScale={rotScale}, translation={translation})";
	}

    public class Triangulator : IDisposable
    {
        #region Primitives
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
        public class RefinementThresholds
        {
            /// <summary>
            /// Specifies the maximum area constraint for triangles in the resulting mesh refinement.
            /// Ensures that no triangle in the mesh has an area larger than the specified value.
            /// </summary>
            [field: SerializeField]
            public float Area { get; set; } = 1f;
            /// <summary>
            /// Specifies the refinement angle constraint for triangles in the resulting mesh.
            /// Ensures that no triangle in the mesh has an angle smaller than the specified value.
            /// </summary>
            /// <remarks>
            /// Expressed in <em>radians</em>.
            /// </remarks>
            [field: SerializeField]
            public float Angle { get; set; } = math.radians(5);
        }

        [Serializable]
        public class TriangulationSettings
        {
            [field: SerializeField]
            public RefinementThresholds RefinementThresholds { get; } = new();
            /// <summary>
            /// Batch count used in parallel jobs.
            /// </summary>
            [field: SerializeField]
            public int BatchCount { get; set; } = 64;
            /// <summary>
            /// Specifies the refinement angle constraint for triangles in the resulting mesh.
            /// Ensures that no triangle in the mesh has an angle smaller than the specified value.
            /// <para>Expressed in <em>radians</em>.</para>
            /// </summary>
            /// <remarks>
            /// <b>Obsolete:</b> use <see cref="RefinementThresholds.Angle"/> instead.
            /// </remarks>
            [Obsolete]
            public float MinimumAngle { get => RefinementThresholds.Angle; set => RefinementThresholds.Angle = value; }
            /// <summary>
            /// Specifies the maximum area constraint for triangles in the resulting mesh refinement.
            /// Ensures that no triangle in the mesh has an area larger than the specified value.
            /// </summary>
            /// <remarks>
            /// <b>Obsolete:</b> use <see cref="RefinementThresholds.Area"/> instead.
            /// </remarks>
            [Obsolete]
            public float MinimumArea { get => RefinementThresholds.Area; set => RefinementThresholds.Area = value; }
            /// <summary>
            /// Specifies the maximum area constraint for triangles in the resulting mesh refinement.
            /// Ensures that no triangle in the mesh has an area larger than the specified value.
            /// </summary>
            /// <remarks>
            /// <b>Obsolete:</b> use <see cref="RefinementThresholds.Area"/> instead.
            /// </remarks>
            [Obsolete]
            public float MaximumArea { get => RefinementThresholds.Area; set => RefinementThresholds.Area = value; }
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
            [Obsolete("To enable constraint edges, pass the corresponding array into Input.ConstraintEdges. Setting this property is unnecessary.", error: true)]
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
            /// Constant used in <em>concentric shells</em> segment splitting.
            /// <b>Modify this only if you know what you are doing!</b>
            /// </summary>
            [field: SerializeField]
            public float ConcentricShellsParameter { get; set; } = 0.001f;
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

        public struct OutputData
        {
            [NativeDisableContainerSafetyRestriction]
            public NativeList<float2> Positions;
            public NativeList<int> Triangles;
            public NativeReference<Status> Status;
        }

        public TriangulationSettings Settings { get; } = new();
        public InputData Input { get; set; } = new();
        public OutputData Output => new OutputData {
            Positions = outputPositions,
            Triangles = triangles,
            Status = status,
        };

        private NativeList<float2> outputPositions;
        private NativeList<int> triangles;
        private NativeReference<Status> status;

        public Triangulator(int capacity, Allocator allocator)
        {
            outputPositions = new(capacity, allocator);
            triangles = new(6 * capacity, allocator);
            status = new(Status.OK, allocator);
        }

        public Triangulator(Allocator allocator) : this(capacity: 16 * 1024, allocator) { }

        public void Dispose()
        {
            outputPositions.Dispose();
            triangles.Dispose();
            status.Dispose();
        }

        public void Run() => Schedule().Complete();

        [BurstCompile]
        struct TriangulationJob: IJob {
            public Preprocessor preprocessor;
            public bool validateInput;
            public bool restoreBoundary;
            public bool refineMesh;
            public int sloanMaxIters;
            public float concentricShellsParameter;
            public float refinementThresholdArea;
            public float refinementThresholdAngle;
            public InputData input;
            public OutputData output;

            public struct InputData {
                public NativeArray<float2> Positions;

                [NativeDisableContainerSafetyRestriction]
                public NativeArray<int> ConstraintEdges;

                [NativeDisableContainerSafetyRestriction]
                public NativeArray<float2> HoleSeeds;
            }

            // Markers
            static readonly ProfilerMarker MarkerPreProcess = new ProfilerMarker("PreProcess");
            static readonly ProfilerMarker MarkerValidateInput = new ProfilerMarker("ValidateInput");
            static readonly ProfilerMarker MarkerDelaunayTriangulation = new ProfilerMarker("DelaunayTriangulation");
            static readonly ProfilerMarker MarkerConstrainEdges = new ProfilerMarker("ConstrainEdges");
            static readonly ProfilerMarker MarkerPlantSeeds = new ProfilerMarker("PlantSeeds");
            static readonly ProfilerMarker MarkerRefineMesh = new ProfilerMarker("RefineMesh");
            static readonly ProfilerMarker MarkerInverseTransformation = new ProfilerMarker("InverseTransformation");

            static void PreProcessInput (Preprocessor preprocessor, InputData input, OutputData output, out NativeList<float2> localPositions, out NativeArray<float2> localHoles, out NativeReference<AffineTransform2D> localTransformation) {
                localTransformation = new NativeReference<AffineTransform2D>(Allocator.Temp);
                localPositions = output.Positions.IsCreated ? output.Positions : new NativeList<float2>(input.Positions.Length, Allocator.Temp);
                localPositions.ResizeUninitialized(input.Positions.Length);
                if (preprocessor == Preprocessor.PCA || preprocessor == Preprocessor.COM) {
                    if (preprocessor == Preprocessor.PCA) {
                        localTransformation.Value = CalculatePCATransformation(input.Positions);
                    } else {
                        localTransformation.Value = CalculateLocalTransformation(input.Positions);
                    }
                    localTransformation.Value.Transform(input.Positions, localPositions.AsArray());
                    if (input.HoleSeeds.IsCreated) {
                        localHoles = new NativeArray<float2>(input.HoleSeeds.Length, Allocator.Temp);
                        localTransformation.Value.Transform(input.HoleSeeds, localHoles);
                    } else {
                        localHoles = default;
                    }
                } else if (preprocessor == Preprocessor.None) {
                    localPositions.CopyFrom(input.Positions);
                    localHoles = input.HoleSeeds;
                    localTransformation.Value = AffineTransform2D.identity;
                } else {
                    throw new System.ArgumentException();
                }
            }

            public void Execute() {
                output.Status.Value = Status.OK;
                output.Triangles.Clear();
                output.Positions.Clear();

                MarkerPreProcess.Begin();
                PreProcessInput(preprocessor, input, output, out var localPositions, out var localHoles, out var localTransformation);
                MarkerPreProcess.End();

                if (validateInput) {
                    MarkerValidateInput.Begin();
                    ValidateInput(localPositions.AsArray(), input.ConstraintEdges);
                    MarkerValidateInput.End();
                }

                MarkerDelaunayTriangulation.Begin();
                var halfEdges = new NativeList<int>(localPositions.Length, Allocator.Temp);
                var triangles = output.Triangles;
                var circles = new NativeList<Circle>(localPositions.Length, Allocator.Temp);
                new DelaunayTriangulationJob {
                    status = output.Status,
                    positions = localPositions.AsArray(),
                    triangles = triangles,
                    halfedges = halfEdges,
                    hullStart = int.MaxValue,
                    c = float.MaxValue,
                }.Execute();
                MarkerDelaunayTriangulation.End();

                var internalConstraints = new NativeList<Edge>(localPositions.Length, Allocator.Temp);
                if (input.ConstraintEdges.IsCreated) {
                    MarkerConstrainEdges.Begin();
                    new ConstrainEdgesJob {
                        status = output.Status,
                        positions = localPositions.AsArray(),
                        triangles = triangles.AsArray(),
                        inputConstraintEdges = input.ConstraintEdges,
                        internalConstraints = internalConstraints,
                        halfedges = halfEdges.AsArray(),
                        maxIters = sloanMaxIters,
                    }.Execute();
                    MarkerConstrainEdges.End();

                    MarkerPlantSeeds.Begin();
                    if (restoreBoundary)
                    {
                        if (localHoles.IsCreated) {
                            // TODO: Merge with ELSE, as this optimization is not necessary.
                            new PlantingSeedsJob<PlantBoundaryAndHoles> {
                                positions = localPositions,
                                mode = new PlantBoundaryAndHoles(localHoles),
                                status = output.Status,
                                triangles = triangles,
                                circles = circles,
                                constraintEdges = internalConstraints,
                                halfedges = halfEdges,
                            }.Execute();
                        } else {
                            new PlantingSeedsJob<PlantBoundary> {
                                positions = localPositions,
                                mode = new PlantBoundary(),
                                status = output.Status,
                                triangles = triangles,
                                circles = circles,
                                constraintEdges = internalConstraints,
                                halfedges = halfEdges,
                            }.Execute();
                        }
                    }
                    else if (localHoles.IsCreated)
                    {
                        new PlantingSeedsJob<PlantHoles> {
                            positions = localPositions,
                            mode = new PlantHoles(localHoles),
                            status = output.Status,
                            triangles = triangles,
                            circles = circles,
                            constraintEdges = internalConstraints,
                            halfedges = halfEdges,
                        }.Execute();
                    }
                    MarkerPlantSeeds.End();
                }

                if (refineMesh) {
                    MarkerRefineMesh.Begin();
                    new RefineMeshJob() {
                        restoreBoundary = restoreBoundary,
                        maximumArea2 = 2 * refinementThresholdArea * localTransformation.Value.areaScalingFactor,
                        minimumAngle = refinementThresholdAngle,
                        D = concentricShellsParameter,
                        status = output.Status,
                        triangles = triangles,
                        outputPositions = localPositions,
                        circles = circles,
                        halfedges = halfEdges,
                        constraints = input.ConstraintEdges.IsCreated ? internalConstraints : default,
                    }.Execute();
                    MarkerRefineMesh.End();
                }

                MarkerInverseTransformation.Begin();
                // If an output position list was provided, we need to transform the local positions back to world space.
                // If none was provided, we can skip this step, as the user doesn't need it.
                if (output.Positions.IsCreated && preprocessor != Preprocessor.None) {
                    localTransformation.Value.InverseTransform(localPositions.AsArray(), output.Positions.AsArray());
                }
                MarkerInverseTransformation.End();
            }

            void ValidateInput (NativeArray<float2> localPositions, NativeArray<int> constraintEdges) {
                new ValidateInputPositionsJob {
                    positions = localPositions,
                    status = output.Status,
                }.Execute();
                if (input.ConstraintEdges.IsCreated) new ValidateInputConstraintEdges {
                    positions = localPositions,
                    constraints = constraintEdges,
                    status = output.Status,
                }.Execute();
            }
        }

        public JobHandle Schedule(JobHandle dependencies = default)
        {
            return new TriangulationJob {
                preprocessor = Settings.Preprocessor,
                validateInput = Settings.ValidateInput,
                restoreBoundary = Settings.RestoreBoundary,
                refineMesh = Settings.RefineMesh,
                sloanMaxIters = Settings.SloanMaxIters,
                concentricShellsParameter = Settings.ConcentricShellsParameter,
                refinementThresholdArea = Settings.RefinementThresholds.Area,
                refinementThresholdAngle = Settings.RefinementThresholds.Angle,
                input = new TriangulationJob.InputData {
                    Positions = Input.Positions,
                    ConstraintEdges = Input.ConstraintEdges,
                    HoleSeeds = Input.HoleSeeds,
                },
                output = Output,
            }.Schedule(dependencies);
        }

        #region Jobs
        [BurstCompile]
        private struct ValidateInputPositionsJob : IJob
        {
            [ReadOnly]
            public NativeArray<float2> positions;
            public NativeReference<Status> status;

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
        static AffineTransform2D CalculatePCATransformation(NativeArray<float2> positions) {
            var com = (float2)0;
            foreach (var p in positions)
            {
                com += p;
            }
            com /= positions.Length;

            var cov = float2x2.zero;
            for (int i = 0; i < positions.Length; i++)
            {
                var q = positions[i] - com;
                cov += Kron(q, q);
            }
            cov /= positions.Length;

            Eigen(cov, out _, out var rotationMatrix);

            // Note: Taking the transpose of a rotation matrix is equivalent to taking the inverse.
            var partialTransform = AffineTransform2D.Rotate(math.transpose(rotationMatrix)) * AffineTransform2D.Translate(-com);
            float2 min = float.MaxValue;
            float2 max = float.MinValue;
            for (int i = 0; i < positions.Length; i++)
            {
                var p = partialTransform.Transform(positions[i]);
                min = math.min(p, min);
                max = math.max(p, max);
            }

            var c = 0.5f * (min + max);
            var s = 2f / (max - min);

            return AffineTransform2D.Scale(s) * AffineTransform2D.Translate(-c) * partialTransform;
        }

        [BurstCompile]
        static AffineTransform2D CalculateLocalTransformation(NativeArray<float2> positions) {
            float2 min = float.PositiveInfinity, max = float.NegativeInfinity, com = 0;
            foreach (var p in positions)
            {
                min = math.min(p, min);
                max = math.max(p, max);
                com += p;
            }

            com /= positions.Length;
            var scale = 1 / math.cmax(math.max(math.abs(max - com), math.abs(min - com)));
            return AffineTransform2D.Scale(scale) * AffineTransform2D.Translate(-com);
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


            public NativeReference<Status> status;
            [ReadOnly]
            public NativeArray<float2> positions;

            public NativeList<int> triangles;

            public NativeList<int> halfedges;

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
            private NativeArray<int> EDGE_STACK;

            public int hullStart;
            private int trianglesLen;
            private int hashSize;
            public float2 c;

            private readonly int HashKey(float2 p)
            {
                return (int)math.floor(pseudoAngle(p.x - c.x, p.y - c.y) * hashSize) % hashSize;

                static float pseudoAngle(float dx, float dy)
                {
                    var p = dx / (math.abs(dx) + math.abs(dy));
                    return (dy > 0 ? 3 - p : 1 + p) / 4; // [0..1]
                }
            }

            public void Execute()
            {
                if (status.Value == Status.ERR)
                {
                    return;
                }

                var n = positions.Length;
                var maxTriangles = math.max(2 * n - 5, 0);
                triangles.Length = 3 * maxTriangles;
                halfedges.Length = 3 * maxTriangles;

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

                // Centermost vertex
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

                // Second closest to the center
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

                // Vertex closest to p1 and p2, as measured by the circumscribed circle radius of p1, p2, p3
                // Thus (p1,p2,p3) form a triangle close to the center of the point set, and it's guaranteed that there
                // are no other vertices inside this triangle.
                var p2 = positions[i2];

                if (minRadius == float.MaxValue)
                {
                    Debug.LogError("[Triangulator]: Provided input is not supported!");
                    status.Value |= Status.ERR;
                    return;
                }

                // Swap the order of the vertices if the triangle is not oriented in the right direction
                if (Orient2dFast(p0, p1, p2) < 0)
                {
                    (i1, i2) = (i2, i1);
                    (p1, p2) = (p2, p1);
                }

                // Sort all other vertices by their distance to the circumcenter of the initial triangle
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

                // Add the initial triangle
                AddTriangle(i0, i1, i2, -1, -1, -1);

                for (var k = 0; k < ids.Length; k++)
                {
                    var i = ids[k];
                    if (i == i0 || i == i1 || i == i2) continue;

                    var p = positions[i];

                    // Find a visible edge on the convex hull using edge hash
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

                     // Add the first triangle from the point
                    var t = AddTriangle(e, i, hullNext[e], -1, -1, hullTri[e]);

                    // Recursively flip triangles from the point until they satisfy the Delaunay condition
                    hullTri[i] = Legalize(t + 2);
                    // Keep track of boundary triangles on the hull
                    hullTri[e] = t;

                    var next = hullNext[e];
                    q = hullNext[next];

                    // Walk forward through the hull, adding more triangles and flipping recursively
                    while (Orient2dFast(p, positions[next], positions[q]) < 0)
                    {
                        t = AddTriangle(next, i, q, hullTri[i], -1, hullTri[next]);
                        hullTri[i] = Legalize(t + 2);
                        hullNext[next] = next;
                        next = q;

                        q = hullNext[next];
                    }

                    // Walk backward from the other side, adding more triangles and flipping
                    if (e == start)
                    {
                        q = hullPrev[e];

                        while (Orient2dFast(p, positions[q], positions[e]) < 0)
                        {
                            t = AddTriangle(q, i, e, -1, hullTri[e], hullTri[q]);
                            Legalize(t + 2);
                            hullTri[q] = t;
                            hullNext[e] = e; // mark as removed
                            e = q;
                            q = hullPrev[e];
                        }
                    }

                    // Update the hull indices
                    hullStart = hullPrev[i] = e;
                    hullNext[e] = hullPrev[next] = i;
                    hullNext[i] = next;

                    // Save the two new edges in the hash table
                    hullHash[HashKey(p)] = i;
                    hullHash[HashKey(positions[e])] = e;
                }

                // Trim lists to their actual size
                triangles.Length = trianglesLen;
                halfedges.Length = trianglesLen;
            }

            private int Legalize(int a)
            {
                var i = 0;
                int ar;

                // recursion eliminated with a fixed-size stack
                while (true)
                {
                    var b = halfedges[a];
                    /* if the pair of triangles doesn't satisfy the Delaunay condition
                     * (p1 is inside the circumcircle of [p0, pl, pr]), flip them,
                     * then do the same check/flip recursively for the new pair of triangles
                     *
                     *           pl                    pl
                     *          /||\                  /  \
                     *       al/ || \bl            al/    \a
                     *        /  ||  \              /      \
                     *       /  a||b  \    flip    /___ar___\
                     *     p0\   ||   /p1   =>   p0\---bl---/p1
                     *        \  ||  /              \      /
                     *       ar\ || /br             b\    /br
                     *          \||/                  \  /
                     *           pr                    pr
                     */

                    int a0 = a - a % 3;
                    ar = a0 + (a + 2) % 3;

                    // Check if we are on a convex hull edge
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

                        // Edge swapped on the other side of the hull (rare); fix the halfedge reference
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

                        // Don't worry about hitting the cap: it can only happen on extremely degenerate input
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
        private struct ValidateInputConstraintEdges : IJob
        {
            [ReadOnly]
            public NativeArray<int> constraints;
            [ReadOnly]
            public NativeArray<float2> positions;
            public NativeReference<Status> status;

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
            public NativeReference<Status> status;
            [ReadOnly]
            public NativeArray<float2> positions;
            public NativeArray<int> triangles;
            [ReadOnly]
            public NativeArray<int> inputConstraintEdges;
            public NativeList<Edge> internalConstraints;
            public NativeArray<int> halfedges;
            public int maxIters;

            [NativeDisableContainerSafetyRestriction]
            private NativeList<int> intersections;
            [NativeDisableContainerSafetyRestriction]
            private NativeList<int> unresolvedIntersections;
            [NativeDisableContainerSafetyRestriction]
            private NativeArray<int> pointToHalfedge;

            public void Execute()
            {
                if (status.Value != Status.OK)
                {
                    return;
                }

                using var _intersections = intersections = new NativeList<int>(Allocator.Temp);
                using var _unresolvedIntersections = unresolvedIntersections = new NativeList<int>(Allocator.Temp);
                using var _pointToHalfedge = pointToHalfedge = new NativeArray<int>(positions.Length, Allocator.Temp);

                // build point to halfedge
                for (int i = 0; i < triangles.Length; i++)
                {
                    pointToHalfedge[triangles[i]] = i;
                }

                BuildInternalConstraints();

                foreach (var c in internalConstraints)
                {
                    TryApplyConstraint(c);
                }
            }

            private void BuildInternalConstraints()
            {
                internalConstraints.Length = inputConstraintEdges.Length / 2;
                for (int index = 0; index < internalConstraints.Length; index++)
                {
                    internalConstraints[index] = new(
                        idA: inputConstraintEdges[2 * index + 0],
                        idB: inputConstraintEdges[2 * index + 1]
                    );
                }
            }

            private void TryApplyConstraint(Edge c)
            {
                intersections.Clear();
                unresolvedIntersections.Clear();

                CollectIntersections(c);

                var iter = 0;
                do
                {
                    if ((status.Value & Status.ERR) == Status.ERR)
                    {
                        return;
                    }

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

                    var (p0, p1, p2, p3) = (positions[_i], positions[_q], positions[_j], positions[_p]);
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
                    pointToHalfedge[_i] = h4;
                    pointToHalfedge[_j] = h1;

                    var h5p = halfedges[h5];
                    halfedges[h0] = h5p;
                    if (h5p != -1)
                    {
                        halfedges[h5p] = h0;
                    }

                    var h2p = halfedges[h2];
                    halfedges[h3] = h2p;
                    if (h2p != -1)
                    {
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
                var (a0, a1) = (positions[e1.IdA], positions[e1.IdB]);
                var (b0, b1) = (positions[e2.IdA], positions[e2.IdB]);
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

                    // Boundary reached check other side
                    if (h0 == -1)
                    {
                        // possible that triangles[h2] == cj, not need to check
                        break;
                    }
                } while (h0 != h0init);

                h0 = halfedges[h0init];
                if (tunnelInit == -1 && h0 != -1)
                {
                    h0 = NextHalfedge(h0);
                    // Same but reversed
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

                        h0 = halfedges[h0];
                        // Boundary reached
                        if (h0 == -1)
                        {
                            break;
                        }
                        h0 = NextHalfedge(h0);
                    } while (h0 != h0init);
                }

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

        [BurstCompile]
        private struct RefineMeshJob : IJob
        {
            public bool restoreBoundary;
            public float maximumArea2;
            public float minimumAngle;
            public float D;
            public NativeReference<Status>.ReadOnly status;
            public NativeList<int> triangles;
            public NativeList<float2> outputPositions;
            public NativeList<Circle> circles;
            public NativeList<int> halfedges;
            public NativeList<Edge> constraints;

            [NativeDisableContainerSafetyRestriction]
            private NativeQueue<int> trianglesQueue;
            [NativeDisableContainerSafetyRestriction]
            private NativeList<int> badTriangles;
            [NativeDisableContainerSafetyRestriction]
            private NativeList<int> pathPoints;
            [NativeDisableContainerSafetyRestriction]
            private NativeList<int> pathHalfedges;
            [NativeDisableContainerSafetyRestriction]
            private NativeList<bool> visitedTriangles;
            [NativeDisableContainerSafetyRestriction]
            private int initialPointsCount;
            private NativeList<bool> constrainedHalfedges;

            public void Execute()
            {
                if (status.Value != Status.OK)
                {
                    return;
                }

                initialPointsCount = outputPositions.Length;
                circles.Length = triangles.Length / 3;
                for (int tId = 0; tId < triangles.Length / 3; tId++)
                {
                    var (i, j, k) = (triangles[3 * tId + 0], triangles[3 * tId + 1], triangles[3 * tId + 2]);
                    circles[tId] = CalculateCircumCircle(i, j, k, outputPositions.AsArray());
                }

                using var _trianglesQueue = trianglesQueue = new NativeQueue<int>(Allocator.Temp);
                using var _badTriangles = badTriangles = new NativeList<int>(triangles.Length / 3, Allocator.Temp);
                using var _pathPoints = pathPoints = new NativeList<int>(Allocator.Temp);
                using var _pathHalfedges = pathHalfedges = new NativeList<int>(Allocator.Temp);
                using var _visitedTriangles = visitedTriangles = new NativeList<bool>(triangles.Length / 3, Allocator.Temp);
                using var _constrainedHalfedges = constrainedHalfedges = new NativeList<bool>(triangles.Length, Allocator.Temp) { Length = triangles.Length };

                using var heQueue = new NativeList<int>(triangles.Length, Allocator.Temp);
                using var tQueue = new NativeList<int>(triangles.Length, Allocator.Temp);

                var tempConstraints = default(NativeList<Edge>);
                if (!constraints.IsCreated)
                {
                    tempConstraints = constraints = new NativeList<Edge>(Allocator.Temp);
                    for (int he = 0; he < halfedges.Length; he++)
                    {
                        if (halfedges[he] == -1)
                        {
                            constraints.Add(new(triangles[he], triangles[NextHalfedge(he)]));
                        }
                    }
                }
                if (!restoreBoundary)
                {
                    for (int he = 0; he < halfedges.Length; he++)
                    {
                        var edge = new Edge(triangles[he], triangles[NextHalfedge(he)]);
                        if (halfedges[he] == -1 && !constraints.Contains(edge))
                        {
                            constraints.Add(edge);
                        }
                    }
                }

                for (int he = 0; he < constrainedHalfedges.Length; he++)
                {
                    for (int id = 0; id < constraints.Length; id++)
                    {
                        var (ci, cj) = constraints[id];
                        var (i, j) = (triangles[he], triangles[NextHalfedge(he)]);
                        (i, j) = i < j ? (i, j) : (j, i);
                        if (ci == i && cj == j)
                        {
                            constrainedHalfedges[he] = true;
                            break;
                        }
                    }
                }

                // Collect encroached half-edges.
                for (int he = 0; he < constrainedHalfedges.Length; he++)
                {
                    if (constrainedHalfedges[he] && IsEncroached(he))
                    {
                        heQueue.Add(he);
                    }
                }

                SplitEncroachedEdges(heQueue, tQueue: default); // ignore bad triangles in this run

                // Collect encroached triangles
                for (int tId = 0; tId < triangles.Length / 3; tId++)
                {
                    if (IsBadTriangle(tId))
                    {
                        tQueue.Add(tId);
                    }
                }

                // Split triangles
                for (int i = 0; i < tQueue.Length; i++)
                {
                    var tId = tQueue[i];
                    if (tId != -1)
                    {
                        SplitTriangle(tId, heQueue, tQueue);
                    }
                }

                if (tempConstraints.IsCreated)
                {
                    tempConstraints.Dispose();
                }
            }

            private void SplitEncroachedEdges(NativeList<int> heQueue, NativeList<int> tQueue)
            {
                for (int i = 0; i < heQueue.Length; i++)
                {
                    var he = heQueue[i];
                    if (he != -1)
                    {
                        SplitEdge(he, heQueue, tQueue);
                    }
                }
                heQueue.Clear();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool IsEncroached(int he0)
            {
                var he1 = NextHalfedge(he0);
                var he2 = NextHalfedge(he1);

                var p0 = outputPositions[triangles[he0]];
                var p1 = outputPositions[triangles[he1]];
                var p2 = outputPositions[triangles[he2]];

                return math.dot(p0 - p2, p1 - p2) <= 0;
            }

            private void SplitEdge(int he, NativeList<int> heQueue, NativeList<int> tQueue)
            {
                var (i, j) = (triangles[he], triangles[NextHalfedge(he)]);
                var (e0, e1) = (outputPositions[i], outputPositions[j]);

                float2 p;
                // Use midpoint method for:
                // - the first segment split,
                // - subsegment not made of input vertices.
                // Otherwise, use "concentric circular shells".
                if (i < initialPointsCount && j < initialPointsCount ||
                    i >= initialPointsCount && j >= initialPointsCount)
                {
                    p = 0.5f * (e0 + e1);
                }
                else
                {
                    var d = math.distance(e0, e1);
                    var k = (int)math.round(math.log2(0.5f * d / D));
                    var alpha = D / d * (1 << k);
                    alpha = i < initialPointsCount ? alpha : 1 - alpha;
                    p = (1 - alpha) * e0 + alpha * e1;
                }

                constrainedHalfedges[he] = false;
                var ohe = halfedges[he];
                if (ohe != -1)
                {
                    constrainedHalfedges[ohe] = false;
                }

                if (halfedges[he] != -1)
                {
                    UnsafeInsertPointBulk(p, initTriangle: he / 3, heQueue, tQueue);

                    var h0 = triangles.Length - 3;
                    var hi = -1;
                    var hj = -1;
                    while (hi == -1 || hj == -1)
                    {
                        var h1 = NextHalfedge(h0);
                        if (triangles[h1] == i)
                        {
                            hi = h0;
                        }
                        if (triangles[h1] == j)
                        {
                            hj = h0;
                        }

                        var h2 = NextHalfedge(h1);
                        h0 = halfedges[h2];
                    }

                    if (IsEncroached(hi))
                    {
                        heQueue.Add(hi);
                    }
                    var ohi = halfedges[hi];
                    if (IsEncroached(ohi))
                    {
                        heQueue.Add(ohi);
                    }
                    if (IsEncroached(hj))
                    {
                        heQueue.Add(hj);
                    }
                    var ohj = halfedges[hj];
                    if (IsEncroached(ohj))
                    {
                        heQueue.Add(ohj);
                    }

                    constrainedHalfedges[hi] = true;
                    constrainedHalfedges[ohi] = true;
                    constrainedHalfedges[hj] = true;
                    constrainedHalfedges[ohj] = true;
                }
                else
                {
                    UnsafeInsertPointBoundary(p, initHe: he, heQueue, tQueue);

                    //var h0 = triangles.Length - 3;
                    var id = 3 * (pathPoints.Length - 1);
                    var hi = halfedges.Length - 1;
                    var hj = halfedges.Length - id;

                    if (IsEncroached(hi))
                    {
                        heQueue.Add(hi);
                    }

                    if (IsEncroached(hj))
                    {
                        heQueue.Add(hj);
                    }

                    constrainedHalfedges[hi] = true;
                    constrainedHalfedges[hj] = true;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool IsBadTriangle(int tId)
            {
                var (i, j, k) = (triangles[3 * tId + 0], triangles[3 * tId + 1], triangles[3 * tId + 2]);
                var area2 = Area2(i, j, k, outputPositions.AsArray());
                return area2 > maximumArea2 || AngleIsTooSmall(tId, minimumAngle);
            }

            private void SplitTriangle(int tId, NativeList<int> heQueue, NativeList<int> tQueue)
            {
                var c = circles[tId];
                var edges = new NativeList<int>(Allocator.Temp);

                for (int he = 0; he < constrainedHalfedges.Length; he++)
                {
                    if (!constrainedHalfedges[he])
                    {
                        continue;
                    }

                    var (i, j) = (triangles[he], triangles[NextHalfedge(he)]);
                    if (halfedges[he] == -1 || i < j)
                    {
                        var (p0, p1) = (outputPositions[i], outputPositions[j]);
                        if (math.dot(p0 - c.Center, p1 - c.Center) <= 0)
                        {
                            edges.Add(he);
                        }
                    }
                }

                if (edges.IsEmpty)
                {
                    UnsafeInsertPointBulk(c.Center, initTriangle: tId, heQueue, tQueue);
                }
                else
                {
                    var (i, j, k) = (triangles[3 * tId + 0], triangles[3 * tId + 1], triangles[3 * tId + 2]);
                    var area2 = Area2(i, j, k, outputPositions.AsArray());
                    if (area2 > maximumArea2) // TODO split permited
                    {
                        foreach (var he in edges.AsReadOnly())
                        {
                            heQueue.Add(he);
                        }
                    }
                    if (!heQueue.IsEmpty)
                    {
                        tQueue.Add(tId);
                        SplitEncroachedEdges(heQueue, tQueue);
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool AngleIsTooSmall(int tId, float minimumAngle)
            {
                var (i, j, k) = (triangles[3 * tId + 0], triangles[3 * tId + 1], triangles[3 * tId + 2]);
                var (pA, pB, pC) = (outputPositions[i], outputPositions[j], outputPositions[k]);

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

            private int UnsafeInsertPointCommon(float2 p, int initTriangle)
            {
                var pId = outputPositions.Length;
                outputPositions.Add(p);

                badTriangles.Clear();
                trianglesQueue.Clear();
                pathPoints.Clear();
                pathHalfedges.Clear();

                visitedTriangles.Clear();
                visitedTriangles.Length = triangles.Length / 3;

                trianglesQueue.Enqueue(initTriangle);
                badTriangles.Add(initTriangle);
                visitedTriangles[initTriangle] = true;
                RecalculateBadTriangles(p);

                return pId;
            }

            private void UnsafeInsertPointBulk(float2 p, int initTriangle, NativeList<int> heQueue = default, NativeList<int> tQueue = default)
            {
                var pId = UnsafeInsertPointCommon(p, initTriangle);
                BuildStarPolygon();
                ProcessBadTriangles(heQueue, tQueue);
                BuildNewTrianglesForStar(pId, heQueue, tQueue);
            }

            private void UnsafeInsertPointBoundary(float2 p, int initHe, NativeList<int> heQueue = default, NativeList<int> tQueue = default)
            {
                var pId = UnsafeInsertPointCommon(p, initHe / 3);
                BuildAmphitheaterPolygon(initHe);
                ProcessBadTriangles(heQueue, tQueue);
                BuildNewTrianglesForAmphitheater(pId, heQueue, tQueue);
            }

            private void RecalculateBadTriangles(float2 p)
            {
                while (trianglesQueue.TryDequeue(out var tId))
                {
                    VisitEdge(p, 3 * tId + 0);
                    VisitEdge(p, 3 * tId + 1);
                    VisitEdge(p, 3 * tId + 2);
                }
            }

            private void VisitEdge(float2 p, int t0)
            {
                var he = halfedges[t0];
                if (he == -1 || constrainedHalfedges[he])
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
                    visitedTriangles[otherId] = true;
                }
            }

            private void BuildAmphitheaterPolygon(int initHe)
            {
                var id = initHe;
                var initPoint = triangles[id];
                while (true)
                {
                    id = NextHalfedge(id);
                    if (triangles[id] == initPoint)
                    {
                        break;
                    }

                    var he = halfedges[id];
                    if (he == -1 || !badTriangles.Contains(he / 3))
                    {
                        pathPoints.Add(triangles[id]);
                        pathHalfedges.Add(he);
                        continue;
                    }
                    id = he;
                }
                pathPoints.Add(triangles[initHe]);
                pathHalfedges.Add(-1);
            }

            private void BuildStarPolygon()
            {
                // Find the "first" halfedge of the polygon.
                var initHe = -1;
                for (int i = 0; i < badTriangles.Length; i++)
                {
                    var tId = badTriangles[i];
                    for (int t = 0; t < 3; t++)
                    {
                        var he = 3 * tId + t;
                        var ohe = halfedges[he];
                        if (ohe == -1 || !badTriangles.Contains(ohe / 3))
                        {
                            pathPoints.Add(triangles[he]);
                            pathHalfedges.Add(ohe);
                            initHe = he;
                            break;
                        }
                    }
                    if (initHe != -1)
                    {
                        break;
                    }
                }

                // Build polygon path from halfedges and points.
                var id = initHe;
                var initPoint = pathPoints[0];
                while (true)
                {
                    id = NextHalfedge(id);
                    if (triangles[id] == initPoint)
                    {
                        break;
                    }

                    var he = halfedges[id];
                    if (he == -1 || !badTriangles.Contains(he / 3))
                    {
                        pathPoints.Add(triangles[id]);
                        pathHalfedges.Add(he);
                        continue;
                    }
                    id = he;
                }
            }

            private void ProcessBadTriangles(NativeList<int> heQueue, NativeList<int> tQueue)
            {
                // Remove bad triangles and recalculate polygon path halfedges.
                badTriangles.Sort();
                for (int t = badTriangles.Length - 1; t >= 0; t--)
                {
                    var tId = badTriangles[t];
                    triangles.RemoveAt(3 * tId + 2);
                    triangles.RemoveAt(3 * tId + 1);
                    triangles.RemoveAt(3 * tId + 0);
                    circles.RemoveAt(tId);
                    RemoveHalfedge(3 * tId + 2, 0);
                    RemoveHalfedge(3 * tId + 1, 1);
                    RemoveHalfedge(3 * tId + 0, 2);
                    constrainedHalfedges.RemoveAt(3 * tId + 2);
                    constrainedHalfedges.RemoveAt(3 * tId + 1);
                    constrainedHalfedges.RemoveAt(3 * tId + 0);

                    for (int i = 3 * tId; i < halfedges.Length; i++)
                    {
                        var he = halfedges[i];
                        if (he == -1)
                        {
                            continue;
                        }
                        halfedges[he < 3 * tId ? he : i] -= 3;
                    }

                    for (int i = 0; i < pathHalfedges.Length; i++)
                    {
                        if (pathHalfedges[i] > 3 * tId + 2)
                        {
                            pathHalfedges[i] -= 3;
                        }
                    }

                    // Adapt he queue
                    if (heQueue.IsCreated)
                    {
                        for (int i = 0; i < heQueue.Length; i++)
                        {
                            var he = heQueue[i];
                            if (he == 3 * tId + 0 || he == 3 * tId + 1 || he == 3 * tId + 2)
                            {
                                heQueue[i] = -1;
                                continue;
                            }

                            if (he > 3 * tId + 2)
                            {
                                heQueue[i] -= 3;
                            }
                        }
                    }

                    // Adapt t queue
                    if (tQueue.IsCreated)
                    {
                        for (int i = 0; i < tQueue.Length; i++)
                        {
                            var q = tQueue[i];
                            if (q == tId)
                            {
                                tQueue[i] = -1;
                                continue;
                            }

                            if (q > tId)
                            {
                                tQueue[i]--;
                            }
                        }
                    }
                }
            }

            private void RemoveHalfedge(int he, int offset)
            {
                var ohe = halfedges[he];
                var o = ohe > he ? ohe - offset : ohe;
                if (o > -1)
                {
                    halfedges[o] = -1;
                }
                halfedges.RemoveAt(he);
            }

            private void BuildNewTrianglesForStar(int pId, NativeList<int> heQueue, NativeList<int> tQueue)
            {
                // Build triangles/circles for inserted point pId.
                var initTriangles = triangles.Length;
                triangles.Length += 3 * pathPoints.Length;
                circles.Length += pathPoints.Length;
                for (int i = 0; i < pathPoints.Length - 1; i++)
                {
                    triangles[initTriangles + 3 * i + 0] = pId;
                    triangles[initTriangles + 3 * i + 1] = pathPoints[i];
                    triangles[initTriangles + 3 * i + 2] = pathPoints[i + 1];
                    circles[initTriangles / 3 + i] = CalculateCircumCircle(pId, pathPoints[i], pathPoints[i + 1], outputPositions.AsArray());
                }
                triangles[^3] = pId;
                triangles[^2] = pathPoints[^1];
                triangles[^1] = pathPoints[0];
                circles[^1] = CalculateCircumCircle(pId, pathPoints[^1], pathPoints[0], outputPositions.AsArray());

                // Build half-edges for inserted point pId.
                var heOffset = halfedges.Length;
                halfedges.Length += 3 * pathPoints.Length;
                constrainedHalfedges.Length += 3 * pathPoints.Length;
                for (int i = 0; i < pathPoints.Length - 1; i++)
                {
                    var he = pathHalfedges[i];
                    halfedges[3 * i + 1 + heOffset] = he;
                    if (he != -1)
                    {
                        halfedges[he] = 3 * i + 1 + heOffset;
                        constrainedHalfedges[3 * i + 1 + heOffset] = constrainedHalfedges[he];
                    }
                    else
                    {
                        constrainedHalfedges[3 * i + 1 + heOffset] = true;
                    }
                    halfedges[3 * i + 2 + heOffset] = 3 * i + 3 + heOffset;
                    halfedges[3 * i + 3 + heOffset] = 3 * i + 2 + heOffset;
                }
                var phe = pathHalfedges[^1];
                halfedges[heOffset + 3 * (pathPoints.Length - 1) + 1] = phe;
                if (phe != -1)
                {
                    halfedges[phe] = heOffset + 3 * (pathPoints.Length - 1) + 1;
                    constrainedHalfedges[heOffset + 3 * (pathPoints.Length - 1) + 1] = constrainedHalfedges[phe];
                }
                else
                {
                    constrainedHalfedges[heOffset + 3 * (pathPoints.Length - 1) + 1] = true;
                }
                halfedges[heOffset] = heOffset + 3 * (pathPoints.Length - 1) + 2;
                halfedges[heOffset + 3 * (pathPoints.Length - 1) + 2] = heOffset;

                if (heQueue.IsCreated)
                {
                    for (int i = 0; i < pathPoints.Length - 1; i++)
                    {
                        var he = heOffset + 3 * i + 1;
                        if (constrainedHalfedges[he] && IsEncroached(he))
                        {
                            heQueue.Add(he);
                        }
                        else if (tQueue.IsCreated && IsBadTriangle(he / 3))
                        {
                            tQueue.Add(he / 3);
                        }
                    }
                }
            }

            private void BuildNewTrianglesForAmphitheater(int pId, NativeList<int> heQueue, NativeList<int> tQueue)
            {
                // Build triangles/circles for inserted point pId.
                var initTriangles = triangles.Length;
                triangles.Length += 3 * (pathPoints.Length - 1);
                circles.Length += pathPoints.Length - 1;
                for (int i = 0; i < pathPoints.Length - 1; i++)
                {
                    triangles[initTriangles + 3 * i + 0] = pId;
                    triangles[initTriangles + 3 * i + 1] = pathPoints[i];
                    triangles[initTriangles + 3 * i + 2] = pathPoints[i + 1];
                    circles[initTriangles / 3 + i] = CalculateCircumCircle(pId, pathPoints[i], pathPoints[i + 1], outputPositions.AsArray());
                }

                // Build half-edges for inserted point pId.
                var heOffset = halfedges.Length;
                halfedges.Length += 3 * (pathPoints.Length - 1);
                constrainedHalfedges.Length += 3 * (pathPoints.Length - 1);
                for (int i = 0; i < pathPoints.Length - 2; i++)
                {
                    var he = pathHalfedges[i];
                    halfedges[3 * i + 1 + heOffset] = he;
                    if (he != -1)
                    {
                        halfedges[he] = 3 * i + 1 + heOffset;
                        constrainedHalfedges[3 * i + 1 + heOffset] = constrainedHalfedges[he];
                    }
                    else
                    {
                        constrainedHalfedges[3 * i + 1 + heOffset] = true;
                    }
                    halfedges[3 * i + 2 + heOffset] = 3 * i + 3 + heOffset;
                    halfedges[3 * i + 3 + heOffset] = 3 * i + 2 + heOffset;
                }

                var phe = pathHalfedges[^2];
                halfedges[heOffset + 3 * (pathPoints.Length - 2) + 1] = phe;
                if (phe != -1)
                {
                    halfedges[phe] = heOffset + 3 * (pathPoints.Length - 2) + 1;
                    constrainedHalfedges[heOffset + 3 * (pathPoints.Length - 2) + 1] = constrainedHalfedges[phe];
                }
                else
                {
                    constrainedHalfedges[heOffset + 3 * (pathPoints.Length - 2) + 1] = true;
                }
                halfedges[heOffset] = -1;
                halfedges[heOffset + 3 * (pathPoints.Length - 2) + 2] = -1;

                if (heQueue.IsCreated)
                {
                    for (int i = 0; i < pathPoints.Length - 1; i++)
                    {
                        var he = heOffset + 3 * i + 1;
                        if (constrainedHalfedges[he] && IsEncroached(he))
                        {
                            heQueue.Add(he);
                        }
                        else if (tQueue.IsCreated && IsBadTriangle(he / 3))
                        {
                            tQueue.Add(he / 3);
                        }
                    }
                }
            }
        }

        [BurstCompile]
        private struct PlantingSeedsJob : IJob
        {
            public T mode;
            public NativeReference<Status>.ReadOnly status;
            public NativeList<int> triangles;
            [ReadOnly]
            public NativeList<float2> positions;
            public NativeList<Circle> circles;
            public NativeList<Edge> constraintEdges;
            public NativeList<int> halfedges;

            [NativeDisableContainerSafetyRestriction]
            private NativeList<bool> constrainedHalfedges;

            public void Execute()
            {
                // unsafe {
                //     Unity.Burst.CompilerServices.Aliasing.ExpectNotAliased(triangles.GetUnsafePtr(), halfedges.GetUnsafePtr());
                // }
                if (status.Value != Status.OK)
                {
                    return;
                }

                if (circles.Length != triangles.Length / 3)
                {
                    circles.Length = triangles.Length / 3;
                    for (int tId = 0; tId < triangles.Length / 3; tId++)
                    {
                        var (i, j, k) = (triangles[3 * tId + 0], triangles[3 * tId + 1], triangles[3 * tId + 2]);
                        circles[tId] = CalculateCircumCircle(i, j, k, positions.AsArray());
                    }
                }

                using var visitedTriangles = new NativeArray<bool>(triangles.Length / 3, Allocator.Temp);
                using var badTriangles = new NativeList<int>(triangles.Length / 3, Allocator.Temp);
                using var trianglesQueue = new NativeQueue<int>(Allocator.Temp);
                using var _constrainedHalfedges = constrainedHalfedges = new NativeList<bool>(triangles.Length, Allocator.Temp) { Length = triangles.Length };

                for (int he = 0; he < constrainedHalfedges.Length; he++)
                {
                    for (int id = 0; id < constraintEdges.Length; id++)
                    {
                        var (ci, cj) = constraintEdges[id];
                        var (i, j) = (triangles[he], triangles[NextHalfedge(he)]);
                        (i, j) = i < j ? (i, j) : (j, i);
                        if (ci == i && cj == j)
                        {
                            constrainedHalfedges[he] = true;
                            break;
                        }
                    }
                }

                PlantSeeds(visitedTriangles, badTriangles, trianglesQueue);

                using var potentialPointsToRemove = new NativeHashSet<int>(initialCapacity: 3 * badTriangles.Length, Allocator.Temp);

                GeneratePotentialPointsToRemove(initialPointsCount: positions.Length, potentialPointsToRemove, badTriangles);
                RemoveBadTriangles(badTriangles);
                RemovePoints(potentialPointsToRemove);
            }

            private void PlantSeeds(NativeArray<bool> visitedTriangles, NativeList<int> badTriangles, NativeQueue<int> trianglesQueue)
            {
                if (restoreBoundary)
                {
                    for (int he = 0; he < halfedges.Length; he++)
                    {
                        if (halfedges[he] == -1 && !visitedTriangles[he / 3] && !constrainedHalfedges[he])
                        {
                            PlantSeed(he / 3, visitedTriangles, badTriangles, trianglesQueue);
                        }
                    }
                }

                if (holeSeeds.IsCreated)
                {
                    foreach (var s in holeSeeds.AsReadOnly())
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
                    for (int i = 0; i < 3; i++)
                    {
                        var he = 3 * tId + i;
                        var ohe = halfedges[he];
                        if (constrainedHalfedges[he] || ohe == -1)
                        {
                            continue;
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
                for (int tId = 0; tId < triangles.Length / 3; tId++)
                {
                    var (i, j, k) = (triangles[3 * tId + 0], triangles[3 * tId + 1], triangles[3 * tId + 2]);
                    var (a, b, c) = (positions[i], positions[j], positions[k]);
                    if (PointInsideTriangle(p, a, b, c))
                    {
                        return tId;
                    }
                }

                return -1;
            }

            private void GeneratePotentialPointsToRemove(int initialPointsCount, NativeHashSet<int> potentialPointsToRemove, NativeList<int> badTriangles)
            {
                foreach (var tId in badTriangles.AsReadOnly())
                {
                    for (int t = 0; t < 3; t++)
                    {
                        var id = triangles[3 * tId + t];
                        if (id >= initialPointsCount)
                        {
                            potentialPointsToRemove.Add(id);
                        }
                    }
                }
            }

            private void RemoveBadTriangles(NativeList<int> badTriangles)
            {
                badTriangles.Sort();
                for (int t = badTriangles.Length - 1; t >= 0; t--)
                {
                    var tId = badTriangles[t];
                    triangles.RemoveAt(3 * tId + 2);
                    triangles.RemoveAt(3 * tId + 1);
                    triangles.RemoveAt(3 * tId + 0);
                    circles.RemoveAt(tId);
                    RemoveHalfedge(3 * tId + 2, 0);
                    RemoveHalfedge(3 * tId + 1, 1);
                    RemoveHalfedge(3 * tId + 0, 2);

                    for (int i = 3 * tId; i < halfedges.Length; i++)
                    {
                        var he = halfedges[i];
                        if (he == -1)
                        {
                            continue;
                        }
                        halfedges[he < 3 * tId ? he : i] -= 3;
                    }
                }
            }

            private void RemoveHalfedge(int he, int offset)
            {
                var ohe = halfedges[he];
                var o = ohe > he ? ohe - offset : ohe;
                if (o > -1)
                {
                    halfedges[o] = -1;
                }
                halfedges.RemoveAt(he);
            }

            private void RemovePoints(NativeHashSet<int> potentialPointsToRemove)
            {
                var pointToHalfedge = new NativeArray<int>(positions.Length, Allocator.Temp);
                var pointsOffset = new NativeArray<int>(positions.Length, Allocator.Temp);

                for (int i = 0; i < pointToHalfedge.Length; i++)
                {
                    pointToHalfedge[i] = -1;
                }

                for (int i = 0; i < triangles.Length; i++)
                {
                    pointToHalfedge[triangles[i]] = i;
                }

                // TODO: Optimize
                using var tmp = potentialPointsToRemove.ToNativeArray(Allocator.Temp);
                tmp.Sort();

                for (int p = tmp.Length - 1; p >= 0; p--)
                {
                    var pId = tmp[p];
                    if (pointToHalfedge[pId] != -1)
                    {
                        continue;
                    }

                    positions.RemoveAt(pId);
                    for (int i = pId; i < pointsOffset.Length; i++)
                    {
                        pointsOffset[i]--;
                    }
                }

                for (int i = 0; i < triangles.Length; i++)
                {
                    triangles[i] += pointsOffset[triangles[i]];
                }

                pointToHalfedge.Dispose();
                pointsOffset.Dispose();
            }
        }
        #endregion

        #region Utils
        private static int NextHalfedge(int he) => he % 3 == 2 ? he - 2 : he + 1;
        private static float Angle(float2 a, float2 b) => math.atan2(Cross(a, b), math.dot(a, b));
        private static float Area2(int i, int j, int k, ReadOnlySpan<float2> positions)
        {
            var (pA, pB, pC) = (positions[i], positions[j], positions[k]);
            var pAB = pB - pA;
            var pAC = pC - pA;
            return math.abs(Cross(pAB, pAC));
        }
        private static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;
        private static Circle CalculateCircumCircle(int i, int j, int k, NativeArray<float2> positions)
        {
            var (pA, pB, pC) = (positions[i], positions[j], positions[k]);
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
