using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace andywiecko.BurstTriangulator
{
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
        /// <summary>
        /// If set to <see langword="true"/>, holes and boundaries will be created automatically 
        /// depending on the provided <see cref="InputData{T2}.ConstraintEdges"/>.
        /// </summary>
        /// <remarks>
        /// The current implementation detects only <em>1-level islands</em>.
        /// It will not detect holes in <em>solid</em> meshes inside other holes.
        /// </remarks>
        [field: SerializeField]
        public bool AutoHolesAndBoundary { get; set; } = false;
        [field: SerializeField]
        public RefinementThresholds RefinementThresholds { get; } = new();
        /// <summary>
        /// If <see langword="true"/> refines mesh using
        /// <see href="https://en.wikipedia.org/wiki/Delaunay_refinement#Ruppert's_algorithm">Ruppert's algorithm</see>.
        /// </summary>
        [field: SerializeField]
        public bool RefineMesh { get; set; } = false;
        /// <summary>
        /// If set to <see langword="true"/>, the provided data will be validated before running the triangulation procedure.
        /// Input positions, as well as input constraints, have a few restrictions.
        /// See <seealso href="https://github.com/andywiecko/BurstTriangulator/blob/main/README.md">README.md</seealso> for more details.
        /// If one of the conditions fails, the triangulation will not be calculated.
        /// This can be detected as an error by inspecting <see cref="OutputData{T2}.Status"/> value (native, can be used in jobs).
        /// Additionally, if <see cref="Verbose"/> is set to <see langword="true"/>, the corresponding error will be logged in the Console.
        /// </summary>
        [field: SerializeField]
        public bool ValidateInput { get; set; } = true;
        /// <summary>
        /// If set to <see langword="true"/>, caught errors with <see cref="Triangulator"/> will be logged in the Console.
        /// </summary>
        /// <remarks>
        /// See also the <see cref="ValidateInput"/> settings.
        /// </remarks>
        [field: SerializeField]
        public bool Verbose { get; set; } = true;
        /// <summary>
        /// If <see langword="true"/> the mesh boundary is restored using <see cref="InputData{T}.ConstraintEdges"/>.
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

    public class InputData<T2> where T2 : unmanaged
    {
        public NativeArray<T2> Positions { get; set; }
        public NativeArray<int> ConstraintEdges { get; set; }
        public NativeArray<T2> HoleSeeds { get; set; }
    }

    public class OutputData<T2> where T2: unmanaged
    {
        public NativeList<T2> Positions => owner.outputPositions;
        public NativeList<int> Triangles => owner.triangles;
        public NativeReference<Status> Status => owner.status;
        public NativeList<int> Halfedges => owner.halfedges;
        private readonly Triangulator<T2> owner;
        public OutputData(Triangulator<T2> owner) => this.owner = owner;
    }

    public class Triangulator : IDisposable
    {
        public TriangulationSettings Settings => impl.Settings;
        public InputData<double2> Input { get => impl.Input; set => impl.Input = value; }
        public OutputData<double2> Output => impl.Output;
        private readonly Triangulator<double2> impl;
        public Triangulator(int capacity, Allocator allocator) => impl = new(capacity, allocator);
        public Triangulator(Allocator allocator) => impl = new(allocator);

        public void Dispose() => impl.Dispose();

        /// <summary>
        /// Perform the job's Execute method immediately on the same thread.
        /// </summary>
        public void Run() => impl.Run();

        /// <summary>
        /// Schedule the job for execution on a worker thread.
        /// </summary>
        /// <param name="dependencies">
        /// Dependencies are used to ensure that a job executes on worker threads after the dependency has completed execution.
        /// Making sure that two jobs reading or writing to same data do not run in parallel.
        /// </param>
        /// <returns>
        /// The handle identifying the scheduled job. Can be used as a dependency for a later job or ensure completion on the main thread.
        /// </returns>
        public JobHandle Schedule(JobHandle dependencies = default) => impl.Schedule(dependencies);
    }

    public class Triangulator<T2> : IDisposable where T2 : unmanaged
    {
        public TriangulationSettings Settings { get; } = new();
        public InputData<T2> Input { get; set; } = new();
        public OutputData<T2> Output { get; }

        internal NativeList<T2> outputPositions;
        internal NativeList<int> triangles;
        internal NativeList<int> halfedges;
        internal NativeReference<Status> status;

        public Triangulator(int capacity, Allocator allocator)
        {
            outputPositions = new(capacity, allocator);
            triangles = new(6 * capacity, allocator);
            status = new(Status.OK, allocator);
            halfedges = new NativeList<int>(6 * capacity, allocator);
            Output = new(this);
        }

        public Triangulator(Allocator allocator) : this(capacity: 16 * 1024, allocator) { }

        public void Dispose()
        {
            outputPositions.Dispose();
            triangles.Dispose();
            status.Dispose();
            halfedges.Dispose();
        }
    }

    [BurstCompile]
    internal struct TriangulationJob<T, T2, TTransform, TUtils> : IJob
        where T : unmanaged, IComparable<T>
        where T2 : unmanaged
        where TTransform : unmanaged, ITransform<TTransform, T, T2>
        where TUtils : unmanaged, IUtils<T, T2>
    {
        private static readonly TUtils utils = default;
        private readonly Args args;
        private InputData input;
        private OutputData output;
        private TTransform localTransformation;

        public readonly struct Args
        {
            public readonly Preprocessor Preprocessor;
            public readonly int SloanMaxIters;
            public readonly bool AutoHolesAndBoundary, RefineMesh, RestoreBoundary, ValidateInput, Verbose;
            public readonly T ConcentricShellsParameter, RefinementThresholdAngle, RefinementThresholdArea;

            public Args(Triangulator<T2> @this)
            {
                var settings = @this.Settings;
                AutoHolesAndBoundary = settings.AutoHolesAndBoundary;
                ConcentricShellsParameter = utils.Const(settings.ConcentricShellsParameter);
                Preprocessor = settings.Preprocessor;
                RefineMesh = settings.RefineMesh;
                RestoreBoundary = settings.RestoreBoundary;
                SloanMaxIters = settings.SloanMaxIters;
                ValidateInput = settings.ValidateInput;
                Verbose = settings.Verbose;
                RefinementThresholdAngle = utils.Const(settings.RefinementThresholds.Angle);
                RefinementThresholdArea = utils.Const(settings.RefinementThresholds.Area);
            }
        }

        public struct InputData
        {
            public NativeArray<T2> Positions;
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<int> ConstraintEdges;
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<T2> HoleSeeds;

            public InputData(Triangulator<T2> @this)
            {
                Positions = @this.Input.Positions;
                ConstraintEdges = @this.Input.ConstraintEdges;
                HoleSeeds = @this.Input.HoleSeeds;
            }
        }

        public struct OutputData
        {
            public NativeList<T2> Positions;
            public NativeList<int> Triangles;
            public NativeReference<Status> Status;
            public NativeList<int> Halfedges;

            public OutputData(Triangulator<T2> @this)
            {
                Positions = @this.Output.Positions;
                Triangles = @this.Output.Triangles;
                Status = @this.Output.Status;
                Halfedges = @this.Output.Halfedges;
            }
        }

        public TriangulationJob(Triangulator<T2> @this)
        {
            args = new(@this);
            input = new(@this);
            output = new(@this);
            localTransformation = default(TTransform).Identity;
        }

        public void Execute()
        {
            output.Status.Value = Status.OK;
            output.Triangles.Clear();
            output.Positions.Clear();
            output.Halfedges.Clear();

            PreProcessInputStep(out var localPositions, out var localHoles);
            new ValidateInputStep(this, localPositions.AsArray()).Execute();
            new DelaunayTriangulationStep(this, localPositions.AsArray()).Execute();
            // TODO: make this public as output, can be useful for consumers.
            using var constrainedHalfedges = new NativeList<bool>(Allocator.Temp) { Length = output.Halfedges.Length };
            new ConstrainEdgesStep(this, localPositions.AsArray(), constrainedHalfedges).Execute();
            new PlantingSeedStep(this, localPositions, constrainedHalfedges, localHoles).Execute();
            new RefineMeshStep(this, localPositions, constrainedHalfedges).Execute();
            PostProcessInputStep(localPositions.AsArray());
        }

        private void PreProcessInputStep(out NativeList<T2> localPositions, out NativeArray<T2> localHoles)
        {
            using var _ = new ProfilerMarker($"{nameof(PreProcessInputStep)}").Auto();

            localPositions = output.Positions;
            localPositions.ResizeUninitialized(input.Positions.Length);
            if (args.Preprocessor == Preprocessor.PCA || args.Preprocessor == Preprocessor.COM)
            {
                localTransformation = args.Preprocessor == Preprocessor.PCA ? default(TTransform).CalculatePCATransformation(input.Positions) : default(TTransform).CalculateLocalTransformation(input.Positions);
                for (int i = 0; i < input.Positions.Length; i++)
                {
                    localPositions[i] = localTransformation.Transform(input.Positions[i]);
                }
                if (input.HoleSeeds.IsCreated)
                {
                    localHoles = new NativeArray<T2>(input.HoleSeeds.Length, Allocator.Temp);
                    for (int i = 0; i < input.HoleSeeds.Length; i++)
                    {
                        localHoles[i] = localTransformation.Transform(input.HoleSeeds[i]);
                    }
                }
                else
                {
                    localHoles = default;
                }
            }
            else if (args.Preprocessor == Preprocessor.None)
            {
                localPositions.CopyFrom(input.Positions);
                localHoles = input.HoleSeeds;
                localTransformation = default(TTransform).Identity;
            }
            else
            {
                throw new ArgumentException();
            }
        }

        private void PostProcessInputStep(NativeArray<T2> localPositions)
        {
            using var _ = new ProfilerMarker($"{nameof(PostProcessInputStep)}").Auto();
            if (args.Preprocessor != Preprocessor.None)
            {
                var inverse = localTransformation.Inverse();
                for (int i = 0; i < localPositions.Length; i++)
                {
                    output.Positions[i] = inverse.Transform(localPositions[i]);
                }
            }
        }

        private struct ValidateInputStep
        {
            private NativeArray<T2>.ReadOnly positions;
            private NativeReference<Status> status;
            private readonly Args args;
            private NativeArray<int>.ReadOnly constraints;

            public ValidateInputStep(TriangulationJob<T, T2, TTransform, TUtils> @this, NativeArray<T2> localPositions)
            {
                positions = localPositions.AsReadOnly();
                status = @this.output.Status;
                args = @this.args;
                var input = @this.input;
                constraints = input.ConstraintEdges.AsReadOnly();
            }

            public void Execute()
            {
                using var _ = new ProfilerMarker($"{nameof(ValidateInputStep)}").Auto();

                if (!args.ValidateInput)
                {
                    return;
                }

                if (positions.Length < 3)
                {
                    Log($"[Triangulator]: Positions.Length is less then 3!");
                    status.Value |= Status.ERR;
                }

                for (int i = 0; i < positions.Length; i++)
                {
                    if (!PointValidation(i))
                    {
                        Log($"[Triangulator]: Positions[{i}] does not contain finite value: {positions[i]}!");
                        status.Value |= Status.ERR;
                    }
                    if (!PointPointValidation(i))
                    {
                        status.Value |= Status.ERR;
                    }
                }

                if (!constraints.IsCreated)
                {
                    return;
                }

                if (constraints.Length % 2 == 1)
                {
                    Log($"[Triangulator]: Constraint input buffer does not contain even number of elements!");
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

            private bool PointValidation(int i) => math.all(utils.isfinite(positions[i]));

            private bool PointPointValidation(int i)
            {
                var pi = positions[i];
                for (int j = i + 1; j < positions.Length; j++)
                {
                    var pj = positions[j];
                    if (math.all(utils.eq(pi, pj)))
                    {
                        Log($"[Triangulator]: Positions[{i}] and [{j}] are duplicated with value: {pi}!");
                        return false;
                    }
                }
                return true;
            }

            private bool EdgePositionsRangeValidation(int i)
            {
                var (a0Id, a1Id) = (constraints[2 * i], constraints[2 * i + 1]);
                var count = positions.Length;
                if (a0Id >= count || a0Id < 0 || a1Id >= count || a1Id < 0)
                {
                    Log($"[Triangulator]: ConstraintEdges[{i}] = ({a0Id}, {a1Id}) is out of range Positions.Length = {count}!");
                    return false;
                }

                return true;
            }

            private bool EdgeValidation(int i)
            {
                var (a0Id, a1Id) = (constraints[2 * i], constraints[2 * i + 1]);
                if (a0Id == a1Id)
                {
                    Log($"[Triangulator]: ConstraintEdges[{i}] is length zero!");
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
                        Log($"[Triangulator]: ConstraintEdges[{i}] and Positions[{j}] are collinear!");
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
                    Log($"[Triangulator]: ConstraintEdges[{i}] and [{j}] are equivalent!");
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
                    Log($"[Triangulator]: ConstraintEdges[{i}] and [{j}] intersect!");
                    return false;
                }

                return true;
            }

            private readonly void Log(string message)
            {
                if (args.Verbose)
                {
                    Debug.LogError(message);
                }
            }
        }

        private struct DelaunayTriangulationStep
        {
            private struct DistComparer : IComparer<int>
            {
                private NativeArray<T> dist;
                public DistComparer(NativeArray<T> dist) => this.dist = dist;
                public int Compare(int x, int y) => dist[x].CompareTo(dist[y]);
            }

            private NativeReference<Status> status;
            private NativeArray<T2>.ReadOnly positions;
            private NativeList<int> triangles;
            private NativeList<int> halfedges;

            private NativeArray<int> hullNext, hullPrev, hullTri, hullHash;
            private NativeArray<int> EDGE_STACK;

            private readonly int hashSize;
            private readonly bool verbose;
            private int hullStart;
            private int trianglesLen;
            private T2 c;

            public DelaunayTriangulationStep(TriangulationJob<T, T2, TTransform, TUtils> @this, NativeArray<T2> localPositions)
            {
                status = @this.output.Status;
                positions = localPositions.AsReadOnly();
                triangles = @this.output.Triangles;
                halfedges = @this.output.Halfedges;
                hullStart = int.MaxValue;
                c = utils.MaxValue2();
                verbose = @this.args.Verbose;
                hashSize = (int)math.ceil(math.sqrt(positions.Length));
                trianglesLen = default;

                hullNext = default;
                hullPrev = default;
                hullTri = default;
                hullHash = default;
                EDGE_STACK = default;
            }

            public void Execute()
            {
                using var _ = new ProfilerMarker($"{nameof(DelaunayTriangulationStep)}").Auto();

                if (status.Value == Status.ERR)
                {
                    return;
                }

                var n = positions.Length;
                var maxTriangles = math.max(2 * n - 5, 0);
                triangles.Length = 3 * maxTriangles;
                halfedges.Length = 3 * maxTriangles;

                using var _hullPrev = hullPrev = new(n, Allocator.Temp);
                using var _hullNext = hullNext = new(n, Allocator.Temp);
                using var _hullTri = hullTri = new(n, Allocator.Temp);
                using var _hullHash = hullHash = new(hashSize, Allocator.Temp);
                using var _EDGE_STACK = EDGE_STACK = new(512, Allocator.Temp);

                var ids = new NativeArray<int>(n, Allocator.Temp);
                var dists = new NativeArray<T>(n, Allocator.Temp);

                var min = utils.MaxValue2();
                var max = utils.MinValue2();
                for (int i = 0; i < positions.Length; i++)
                {
                    var p = positions[i];
                    min = utils.min(min, p);
                    max = utils.max(max, p);
                    ids[i] = i;
                }

                var center = utils.avg(min, max);

                int i0 = int.MaxValue, i1 = int.MaxValue, i2 = int.MaxValue;
                var minDistSq = utils.MaxValue();
                for (int i = 0; i < positions.Length; i++)
                {
                    var distSq = utils.distancesq(center, positions[i]);
                    if (utils.less(distSq, minDistSq))
                    {
                        i0 = i;
                        minDistSq = distSq;
                    }
                }

                // Centermost vertex
                var p0 = positions[i0];

                minDistSq = utils.MaxValue();
                for (int i = 0; i < positions.Length; i++)
                {
                    if (i == i0) continue;
                    var distSq = utils.distancesq(p0, positions[i]);
                    if (utils.less(distSq, minDistSq))
                    {
                        i1 = i;
                        minDistSq = distSq;
                    }
                }

                // Second closest to the center
                var p1 = positions[i1];

                var minRadius = utils.MaxValue();
                for (int i = 0; i < positions.Length; i++)
                {
                    if (i == i0 || i == i1) continue;
                    var p = positions[i];
                    var r = CircumRadiusSq(p0, p1, p);
                    if (utils.less(r, minRadius))
                    {
                        i2 = i;
                        minRadius = r;
                    }
                }

                // Vertex closest to p1 and p2, as measured by the circumscribed circle radius of p1, p2, p3
                // Thus (p1,p2,p3) form a triangle close to the center of the point set, and it's guaranteed that there
                // are no other vertices inside this triangle.
                var p2 = positions[i2];

                if (utils.eq(minRadius, utils.MaxValue()))
                {
                    if (verbose)
                    {
                        Debug.LogError("[Triangulator]: Provided input is not supported!");
                    }
                    status.Value |= Status.ERR;
                    return;
                }

                // Swap the order of the vertices if the triangle is not oriented in the right direction
                if (utils.less(Orient2dFast(p0, p1, p2), utils.Zero()))
                {
                    (i1, i2) = (i2, i1);
                    (p1, p2) = (p2, p1);
                }

                // Sort all other vertices by their distance to the circumcenter of the initial triangle
                c = CircumCenter(p0, p1, p2);
                for (int i = 0; i < positions.Length; i++)
                {
                    dists[i] = utils.distancesq(c, positions[i]);
                }

                ids.Sort(new DistComparer(dists));

                hullStart = i0;

                hullNext[i0] = hullPrev[i2] = i1;
                hullNext[i1] = hullPrev[i0] = i2;
                hullNext[i2] = hullPrev[i1] = i0;

                hullTri[i0] = 0;
                hullTri[i1] = 1;
                hullTri[i2] = 2;

                hullHash[utils.hashkey(p0, c, hashSize)] = i0;
                hullHash[utils.hashkey(p1, c, hashSize)] = i1;
                hullHash[utils.hashkey(p2, c, hashSize)] = i2;

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
                        var key = utils.hashkey(p, c, hashSize);
                        start = hullHash[(key + j) % hashSize];
                        if (start != -1 && start != hullNext[start]) break;
                    }

                    start = hullPrev[start];
                    var e = start;
                    var q = hullNext[e];

                    while (utils.ge(Orient2dFast(p, positions[e], positions[q]), utils.Zero()))
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
                    while (utils.less(Orient2dFast(p, positions[next], positions[q]), utils.Zero()))
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

                        while (utils.less(Orient2dFast(p, positions[q], positions[e]), utils.Zero()))
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
                    hullHash[utils.hashkey(p, c, hashSize)] = i;
                    hullHash[utils.hashkey(positions[e], c, hashSize)] = e;
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

        private struct ConstrainEdgesStep
        {
            private NativeReference<Status> status;
            private NativeArray<T2>.ReadOnly positions;
            private NativeArray<int> triangles;
            private NativeArray<int>.ReadOnly inputConstraintEdges;
            private NativeArray<int> halfedges;
            private NativeList<bool> constrainedHalfedges;
            private readonly Args args;

            private NativeList<int> intersections;
            private NativeList<int> unresolvedIntersections;
            private NativeArray<int> pointToHalfedge;

            public ConstrainEdgesStep(TriangulationJob<T, T2, TTransform, TUtils> @this, NativeArray<T2> localPositions, NativeList<bool> constrainedHalfedges)
            {
                status = @this.output.Status;
                positions = localPositions.AsReadOnly();
                var output = @this.output;
                triangles = output.Triangles.AsArray();
                var input = @this.input;
                inputConstraintEdges = input.ConstraintEdges.AsReadOnly();
                halfedges = output.Halfedges.AsArray();
                args = @this.args;
                this.constrainedHalfedges = constrainedHalfedges;

                intersections = default;
                unresolvedIntersections = default;
                pointToHalfedge = default;
            }

            public void Execute()
            {
                using var _ = new ProfilerMarker($"{nameof(ConstrainEdgesStep)}").Auto();

                if (!inputConstraintEdges.IsCreated || status.Value != Status.OK)
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

                for (int index = 0; index < inputConstraintEdges.Length / 2; index++)
                {
                    var c = math.int2(
                        inputConstraintEdges[2 * index + 0],
                        inputConstraintEdges[2 * index + 1]
                    );
                    c = c.x < c.y ? c.xy : c.yx; // Backward compatibility. To remove in the future.
                    TryApplyConstraint(c);
                }
            }

            private void TryApplyConstraint(int2 c)
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

            private void TryResolveIntersections(int2 c, ref int iter)
            {
                for (int i = 0; i < intersections.Length; i++)
                {
                    if (IsMaxItersExceeded(iter++, args.SloanMaxIters))
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

                    constrainedHalfedges[h3] = constrainedHalfedges[h2];
                    var h3p = halfedges[h3];
                    if (h3p != -1)
                    {
                        constrainedHalfedges[h3p] = constrainedHalfedges[h2];
                    }

                    constrainedHalfedges[h0] = constrainedHalfedges[h5];
                    var h0p = halfedges[h0];
                    if (h0p != -1)
                    {
                        constrainedHalfedges[h0p] = constrainedHalfedges[h5];
                    }
                    constrainedHalfedges[h2] = false;
                    constrainedHalfedges[h5] = false;

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

                    var swapped = math.int2(_p, _q);
                    if (math.all(c.xy == swapped.xy) || math.all(c.xy == swapped.yx))
                    {
                        constrainedHalfedges[h2] = true;
                        constrainedHalfedges[h5] = true;
                    }
                    if (EdgeEdgeIntersection(c, swapped))
                    {
                        unresolvedIntersections.Add(h2);
                    }
                }

                intersections.Clear();
            }

            private bool EdgeEdgeIntersection(int2 e1, int2 e2)
            {
                var (a0, a1) = (positions[e1.x], positions[e1.y]);
                var (b0, b1) = (positions[e2.x], positions[e2.y]);
                return !(math.any(e1.xy == e2.xy | e1.xy == e2.yx)) && TriangulationJob<T, T2, TTransform, TUtils>.EdgeEdgeIntersection(a0, a1, b0, b1);
            }

            private void CollectIntersections(int2 edge)
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
                var (ci, cj) = (edge.x, edge.y);
                var h0init = pointToHalfedge[ci];
                var h0 = h0init;
                do
                {
                    var h1 = NextHalfedge(h0);
                    if (triangles[h1] == cj)
                    {
                        constrainedHalfedges[h0] = true;
                        var oh0 = halfedges[h0];
                        if (oh0 != -1)
                        {
                            constrainedHalfedges[oh0] = true;
                        }
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
                        if (triangles[h2] == cj)
                        {
                            constrainedHalfedges[h2] = true;
                        }

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
                            constrainedHalfedges[h0] = true;
                            var oh0 = halfedges[h0];
                            if (oh0 != -1)
                            {
                                constrainedHalfedges[oh0] = true;
                            }
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
                    if (args.Verbose)
                    {
                        Debug.LogError(
                            $"[Triangulator]: Sloan max iterations exceeded! This may suggest that input data is hard to resolve by Sloan's algorithm. " +
                            $"It usually happens when the scale of the input positions is not uniform. " +
                            $"Please try to post-process input data or increase {nameof(TriangulationSettings.SloanMaxIters)} value."
                        );
                    }
                    status.Value |= Status.ERR;
                    return true;
                }
                return false;
            }
        }

        private struct PlantingSeedStep
        {
            private NativeReference<Status>.ReadOnly status;
            private NativeList<int> triangles;
            [ReadOnly]
            private NativeList<T2> positions;
            private NativeList<bool> constrainedHalfedges;
            private NativeList<int> halfedges;

            private NativeArray<bool> visitedTriangles;
            private NativeList<int> badTriangles;
            private NativeQueue<int> trianglesQueue;
            private NativeArray<T2> holes;

            private readonly bool constraintsIsCreated;
            private readonly Args args;

            public PlantingSeedStep(TriangulationJob<T, T2, TTransform, TUtils> @this, NativeList<T2> localPositions, NativeList<bool> constrainedHalfedges, NativeArray<T2> localHoles)
            {
                status = @this.output.Status;
                triangles = @this.output.Triangles;
                positions = localPositions;
                this.constrainedHalfedges = constrainedHalfedges;
                halfedges = @this.output.Halfedges;
                holes = localHoles;
                args = @this.args;
                var input = @this.input;
                constraintsIsCreated = input.ConstraintEdges.IsCreated;

                visitedTriangles = new NativeArray<bool>(triangles.Length / 3, Allocator.Temp);
                badTriangles = new NativeList<int>(triangles.Length / 3, Allocator.Temp);
                trianglesQueue = new NativeQueue<int>(Allocator.Temp);
            }

            public void Execute()
            {
                using var _ = new ProfilerMarker($"{nameof(PlantingSeedStep)}").Auto();

                if (!constraintsIsCreated)
                {
                    return;
                }

                if (args.AutoHolesAndBoundary) PlantAuto();
                if (holes.IsCreated) PlantHoleSeeds(holes);
                if (args.RestoreBoundary) PlantBoundarySeeds();

                Finish();
            }

            private void PlantBoundarySeeds()
            {
                for (int he = 0; he < halfedges.Length; he++)
                {
                    if (halfedges[he] == -1 &&
                        !visitedTriangles[he / 3] &&
                        !constrainedHalfedges[he])
                    {
                        PlantSeed(he / 3);
                    }
                }
            }

            private void PlantHoleSeeds(NativeArray<T2> holeSeeds)
            {
                foreach (var s in holeSeeds)
                {
                    var tId = FindTriangle(s);
                    if (tId != -1)
                    {
                        PlantSeed(tId);
                    }
                }
            }

            private void Finish()
            {
                if (status.Value != Status.OK)
                {
                    return;
                }

                badTriangles.Sort();
                for (int t = badTriangles.Length - 1; t >= 0; t--)
                {
                    var tId = badTriangles[t];
                    triangles.RemoveAt(3 * tId + 2);
                    triangles.RemoveAt(3 * tId + 1);
                    triangles.RemoveAt(3 * tId + 0);
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
                }
            }

            private void PlantSeed(int tId)
            {
                var visitedTriangles = this.visitedTriangles;
                var badTriangles = this.badTriangles;
                var trianglesQueue = this.trianglesQueue;

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

            private int FindTriangle(T2 p)
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

            private void PlantAuto()
            {
                using var heQueue = new NativeQueue<int>(Allocator.Temp);
                using var loop = new NativeList<int>(Allocator.Temp);
                var heVisited = new NativeArray<bool>(halfedges.Length, Allocator.Temp);

                // Build boundary loop: 1st sweep
                for (int he = 0; he < halfedges.Length; he++)
                {
                    if (halfedges[he] != -1 || heVisited[he])
                    {
                        continue;
                    }

                    heVisited[he] = true;
                    if (constrainedHalfedges[he])
                    {
                        loop.Add(he);
                    }
                    else
                    {
                        if (!visitedTriangles[he / 3])
                        {
                            PlantSeed(he / 3);
                        }

                        var h2 = NextHalfedge(he);
                        if (halfedges[h2] != -1 && !heVisited[h2])
                        {
                            heQueue.Enqueue(h2);
                            heVisited[h2] = true;
                        }

                        var h3 = NextHalfedge(h2);
                        if (halfedges[h3] != -1 && !heVisited[h3])
                        {
                            heQueue.Enqueue(h3);
                            heVisited[h3] = true;
                        }
                    }
                }

                // Build boundary loop: 2nd sweep
                while (heQueue.TryDequeue(out var he))
                {
                    var ohe = halfedges[he]; // valid `ohe` should always exist, -1 are eliminated in the 1st sweep!
                    if (constrainedHalfedges[ohe])
                    {
                        heVisited[ohe] = true;
                        loop.Add(ohe);
                    }
                    else
                    {
                        ohe = NextHalfedge(ohe);
                        if (!heVisited[ohe])
                        {
                            heQueue.Enqueue(ohe);
                        }

                        ohe = NextHalfedge(ohe);
                        if (!heVisited[ohe])
                        {
                            heQueue.Enqueue(ohe);
                        }
                    }
                }

                // Plant seeds for non visited constraint edges
                foreach (var h1 in loop)
                {
                    var h2 = NextHalfedge(h1);
                    if (!heVisited[h2])
                    {
                        heQueue.Enqueue(h2);
                        heVisited[h2] = true;
                    }

                    var h3 = NextHalfedge(h2);
                    if (!heVisited[h3])
                    {
                        heQueue.Enqueue(h3);
                        heVisited[h3] = true;
                    }
                }
                while (heQueue.TryDequeue(out var he))
                {
                    var ohe = halfedges[he];
                    if (constrainedHalfedges[ohe])
                    {
                        heVisited[ohe] = true;
                        PlantSeed(ohe / 3);
                    }
                    else
                    {
                        ohe = NextHalfedge(ohe);
                        if (!heVisited[ohe])
                        {
                            heQueue.Enqueue(ohe);
                            heVisited[ohe] = true;
                        }

                        ohe = NextHalfedge(ohe);
                        if (!heVisited[ohe])
                        {
                            heQueue.Enqueue(ohe);
                            heVisited[ohe] = true;
                        }
                    }
                }
            }
        }

        private struct RefineMeshStep
        {
            private readonly struct Circle
            {
                public readonly T2 Center;
                public readonly T RadiusSq;
                private readonly T offset;
                public Circle(T2 center, T radiusSq) => (Center, RadiusSq, offset) = (center, radiusSq, default);
                public Circle((T2 center, T radiusSq) circle) => (Center, RadiusSq, offset) = (circle.center, circle.radiusSq, default);
            }

            private NativeReference<Status>.ReadOnly status;
            private NativeList<int> triangles;
            private NativeList<T2> outputPositions;
            private NativeList<int> halfedges;
            private NativeList<bool> constrainedHalfedges;

            private NativeList<Circle> circles;
            private NativeQueue<int> trianglesQueue;
            private NativeList<int> badTriangles;
            private NativeList<int> pathPoints;
            private NativeList<int> pathHalfedges;
            private NativeList<bool> visitedTriangles;

            private readonly Args args;
            private readonly bool constrainBoundary;
            private readonly T maximumArea2;
            private readonly int initialPointsCount;

            public RefineMeshStep(TriangulationJob<T, T2, TTransform, TUtils> @this, NativeList<T2> localPositions, NativeList<bool> constrainedHalfedges)
            {
                args = @this.args;
                var s = @this.output.Status; // Note: Cannot be one-liner. Burst throws bit cast exception.
                status = s.AsReadOnly();
                var constraints = @this.input.ConstraintEdges;
                constrainBoundary = !constraints.IsCreated || !@this.args.RestoreBoundary;
                initialPointsCount = localPositions.Length;
                var areaThreshold = @this.args.RefinementThresholdArea;
                var lt = @this.localTransformation;
                maximumArea2 = utils.mul(utils.mul(utils.Const(2), areaThreshold), lt.AreaScalingFactor);
                triangles = @this.output.Triangles;
                outputPositions = localPositions;
                halfedges = @this.output.Halfedges;
                this.constrainedHalfedges = constrainedHalfedges;

                circles = default;
                trianglesQueue = default;
                badTriangles = default;
                pathPoints = default;
                pathHalfedges = default;
                visitedTriangles = default;
            }

            public void Execute()
            {
                using var _ = new ProfilerMarker($"{nameof(RefineMeshStep)}").Auto();

                if (!args.RefineMesh || status.Value != Status.OK)
                {
                    return;
                }

                if (constrainBoundary)
                {
                    for (int he = 0; he < constrainedHalfedges.Length; he++)
                    {
                        constrainedHalfedges[he] = halfedges[he] == -1;
                    }
                }

                using var _circles = circles = new(Allocator.Temp) { Length = triangles.Length / 3 };
                using var _trianglesQueue = trianglesQueue = new NativeQueue<int>(Allocator.Temp);
                using var _badTriangles = badTriangles = new NativeList<int>(triangles.Length / 3, Allocator.Temp);
                using var _pathPoints = pathPoints = new NativeList<int>(Allocator.Temp);
                using var _pathHalfedges = pathHalfedges = new NativeList<int>(Allocator.Temp);
                using var _visitedTriangles = visitedTriangles = new NativeList<bool>(triangles.Length / 3, Allocator.Temp);

                using var heQueue = new NativeList<int>(triangles.Length, Allocator.Temp);
                using var tQueue = new NativeList<int>(triangles.Length, Allocator.Temp);

                for (int tId = 0; tId < triangles.Length / 3; tId++)
                {
                    var (i, j, k) = (triangles[3 * tId + 0], triangles[3 * tId + 1], triangles[3 * tId + 2]);
                    circles[tId] = new(CalculateCircumCircle(i, j, k, outputPositions.AsArray()));
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

                return utils.le(utils.dot(utils.diff(p0, p2), utils.diff(p1, p2)), utils.Zero());
            }

            private void SplitEdge(int he, NativeList<int> heQueue, NativeList<int> tQueue)
            {
                var (i, j) = (triangles[he], triangles[NextHalfedge(he)]);
                var (e0, e1) = (outputPositions[i], outputPositions[j]);

                T2 p;
                // Use midpoint method for:
                // - the first segment split,
                // - subsegment not made of input vertices.
                // Otherwise, use "concentric circular shells".
                if (i < initialPointsCount && j < initialPointsCount ||
                    i >= initialPointsCount && j >= initialPointsCount)
                {
                    p = utils.avg(e0, e1);
                }
                else
                {
                    var alpha = utils.alpha(D: args.ConcentricShellsParameter, d: utils.distance(e0, e1), i < initialPointsCount);
                    p = utils.lerp(e0, e1, alpha);
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
                return utils.greater(area2, maximumArea2) || AngleIsTooSmall(tId, args.RefinementThresholdAngle);
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
                        if (utils.le(utils.dot(utils.diff(p0, c.Center), utils.diff(p1, c.Center)), utils.Zero()))
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
                    if (utils.greater(area2, maximumArea2)) // TODO split permited
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
            private bool AngleIsTooSmall(int tId, T minimumAngle)
            {
                var (i, j, k) = (triangles[3 * tId + 0], triangles[3 * tId + 1], triangles[3 * tId + 2]);
                var (pA, pB, pC) = (outputPositions[i], outputPositions[j], outputPositions[k]);

                var pAB = utils.diff(pB, pA);
                var pBC = utils.diff(pC, pB);
                var pCA = utils.diff(pA, pC);

                return utils.anyabslessthen(
                    a: Angle(pAB, utils.neg(pCA)),
                    b: Angle(pBC, utils.neg(pAB)),
                    c: Angle(pCA, utils.neg(pBC)),
                    v: minimumAngle
                );
            }

            private int UnsafeInsertPointCommon(T2 p, int initTriangle)
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

            private void UnsafeInsertPointBulk(T2 p, int initTriangle, NativeList<int> heQueue = default, NativeList<int> tQueue = default)
            {
                var pId = UnsafeInsertPointCommon(p, initTriangle);
                BuildStarPolygon();
                ProcessBadTriangles(heQueue, tQueue);
                BuildNewTrianglesForStar(pId, heQueue, tQueue);
            }

            private void UnsafeInsertPointBoundary(T2 p, int initHe, NativeList<int> heQueue = default, NativeList<int> tQueue = default)
            {
                var pId = UnsafeInsertPointCommon(p, initHe / 3);
                BuildAmphitheaterPolygon(initHe);
                ProcessBadTriangles(heQueue, tQueue);
                BuildNewTrianglesForAmphitheater(pId, heQueue, tQueue);
            }

            private void RecalculateBadTriangles(T2 p)
            {
                while (trianglesQueue.TryDequeue(out var tId))
                {
                    VisitEdge(p, 3 * tId + 0);
                    VisitEdge(p, 3 * tId + 1);
                    VisitEdge(p, 3 * tId + 2);
                }
            }

            private void VisitEdge(T2 p, int t0)
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
                if (utils.le(utils.distancesq(circle.Center, p), circle.RadiusSq))
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
                    circles[initTriangles / 3 + i] = new(CalculateCircumCircle(pId, pathPoints[i], pathPoints[i + 1], outputPositions.AsArray()));
                }
                triangles[^3] = pId;
                triangles[^2] = pathPoints[^1];
                triangles[^1] = pathPoints[0];
                circles[^1] = new(CalculateCircumCircle(pId, pathPoints[^1], pathPoints[0], outputPositions.AsArray()));

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
                    circles[initTriangles / 3 + i] = new(CalculateCircumCircle(pId, pathPoints[i], pathPoints[i + 1], outputPositions.AsArray()));
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

        private static T Angle(T2 a, T2 b) => utils.atan2(Cross(a, b), utils.dot(a, b));
        private static T Area2(int i, int j, int k, ReadOnlySpan<T2> positions)
        {
            var (pA, pB, pC) = (positions[i], positions[j], positions[k]);
            var pAB = utils.diff(pB, pA);
            var pAC = utils.diff(pC, pA);
            return utils.abs(Cross(pAB, pAC));
        }
        private static T Cross(T2 a, T2 b) => utils.diff(utils.mul(utils.X(a), utils.Y(b)), utils.mul(utils.Y(a), utils.X(b)));
        private static T CCW(T2 a, T2 b, T2 c) => utils.sign(Cross(utils.diff(b, a), utils.diff(b, c)));
        private static T2 CircumCenter(T2 a, T2 b, T2 c)
        {
            var dx = utils.diff(utils.X(b), utils.X(a));
            var dy = utils.diff(utils.Y(b), utils.Y(a));
            var ex = utils.diff(utils.X(c), utils.X(a));
            var ey = utils.diff(utils.Y(c), utils.Y(a));

            var bl = utils.add(utils.mul(dx, dx), utils.mul(dy, dy));
            var cl = utils.add(utils.mul(ex, ex), utils.mul(ey, ey));

            var d = utils.div(utils.Const(0.5f), utils.diff(utils.mul(dx, ey), utils.mul(dy, ex)));

            var x = utils.add(utils.X(a), utils.mul(utils.diff(utils.mul(ey, bl), utils.mul(dy, cl)), d));
            var y = utils.add(utils.Y(a), utils.mul(utils.diff(utils.mul(dx, cl), utils.mul(ex, bl)), d));

            return utils.NewT2(x, y);
        }
        private static T CircumRadiusSq(T2 a, T2 b, T2 c) => utils.distancesq(CircumCenter(a, b, c), a);
        private static (T2, T) CalculateCircumCircle(int i, int j, int k, NativeArray<T2> positions)
        {
            var (pA, pB, pC) = (positions[i], positions[j], positions[k]);
            return (CircumCenter(pA, pB, pC), CircumRadiusSq(pA, pB, pC));
        }
        private static bool EdgeEdgeIntersection(T2 a0, T2 a1, T2 b0, T2 b1) => utils.neq(CCW(a0, a1, b0), CCW(a0, a1, b1)) && utils.neq(CCW(b0, b1, a0), CCW(b0, b1, a1));
        private static int NextHalfedge(int he) => he % 3 == 2 ? he - 2 : he + 1;
        private static bool InCircle(T2 a, T2 b, T2 c, T2 p)
        {
            var dx = utils.diff(utils.X(a), utils.X(p));
            var dy = utils.diff(utils.Y(a), utils.Y(p));
            var ex = utils.diff(utils.X(b), utils.X(p));
            var ey = utils.diff(utils.Y(b), utils.Y(p));
            var fx = utils.diff(utils.X(c), utils.X(p));
            var fy = utils.diff(utils.Y(c), utils.Y(p));

            var ap = utils.add(utils.mul(dx, dx), utils.mul(dy, dy));
            var bp = utils.add(utils.mul(ex, ex), utils.mul(ey, ey));
            var cp = utils.add(utils.mul(fx, fx), utils.mul(fy, fy));

            // dx * (ey * cp - bp * fy) - dy * (ex * cp - bp * fx) + ap * (ex * fy - ey * fx) < 0
            return utils.less(
                utils.add(
                    utils.diff(
                        utils.mul(dx, utils.diff(utils.mul(ey, cp), utils.mul(bp, fy))),
                        utils.mul(dy, utils.diff(utils.mul(ex, cp), utils.mul(bp, fx)))
                    ),
                    utils.mul(ap, utils.diff(utils.mul(ex, fy), utils.mul(ey, fx)))
                ),
                utils.Zero()
            );
        }

        private static bool IsConvexQuadrilateral(T2 a, T2 b, T2 c, T2 d) => true
            && utils.neq(CCW(a, c, b), utils.Zero())
            && utils.neq(CCW(a, c, d), utils.Zero())
            && utils.neq(CCW(b, d, a), utils.Zero())
            && utils.neq(CCW(b, d, c), utils.Zero())
            && utils.neq(CCW(a, c, b), CCW(a, c, d))
            && utils.neq(CCW(b, d, a), CCW(b, d, c));
        // (a.y - c.y) * (b.x - c.x) - (a.x - c.x) * (b.y - c.y)
        private static T Orient2dFast(T2 a, T2 b, T2 c) => utils.diff(
            utils.mul(utils.diff(utils.Y(a), utils.Y(c)), utils.diff(utils.X(b), utils.X(c))),
            utils.mul(utils.diff(utils.X(a), utils.X(c)), utils.diff(utils.Y(b), utils.Y(c)))
        );
        private static bool PointLineSegmentIntersection(T2 a, T2 b0, T2 b1) => true
            && utils.eq(CCW(b0, b1, a), utils.Zero())
            && math.all(utils.ge(a, utils.min(b0, b1)) & utils.le(a, utils.max(b0, b1)));

        private static (T b0, T b1, T b2) Barycentric(T2 a, T2 b, T2 c, T2 p)
        {
            var (v0, v1, v2) = (utils.diff(b, a), utils.diff(c, a), utils.diff(p, a));
            var denInv = utils.div(utils.Const(1), Cross(v0, v1));
            var v = utils.mul(denInv, Cross(v2, v1));
            var w = utils.mul(denInv, Cross(v0, v2));
            var u = utils.diff(utils.diff(utils.Const(1), v), w);
            return (u, v, w);
        }

        private static bool PointInsideTriangle(T2 p, T2 a, T2 b, T2 c)
        {
            var (u, v, w) = Barycentric(a, b, c, p);
            // math.cmax(-Barycentric(a, b, c, p)) <= 0
            return utils.le(utils.max(utils.max(utils.neg(u), utils.neg(v)), utils.neg(w)), utils.Zero());
        }
    }

    internal interface ITransform<TSelf, T, T2> where T : unmanaged where T2 : unmanaged
    {
        T AreaScalingFactor { get; }
        TSelf Identity { get; }
        TSelf Inverse();
        T2 Transform(T2 point);
        TSelf CalculatePCATransformation(NativeArray<T2> positions);
        TSelf CalculateLocalTransformation(NativeArray<T2> positions);
    }

    internal readonly struct AffineTransform32 : ITransform<AffineTransform32, float, float2>
    {
        public readonly AffineTransform32 Identity => new(float2x2.identity, float2.zero);
        public readonly float AreaScalingFactor => math.abs(math.determinant(rotScale));

        private readonly float2x2 rotScale;
        private readonly float2 translation;

        public AffineTransform32(float2x2 rotScale, float2 translation) => (this.rotScale, this.translation) = (rotScale, translation);
        private static AffineTransform32 Translate(float2 offset) => new(float2x2.identity, offset);
        private static AffineTransform32 Scale(float2 scale) => new(new float2x2(scale.x, 0, 0, scale.y), float2.zero);
        private static AffineTransform32 Rotate(float2x2 rotation) => new(rotation, float2.zero);
        public static AffineTransform32 operator *(AffineTransform32 lhs, AffineTransform32 rhs) => new(
            math.mul(lhs.rotScale, rhs.rotScale),
            math.mul(math.inverse(rhs.rotScale), lhs.translation) + rhs.translation
        );

        public AffineTransform32 Inverse() => new(math.inverse(rotScale), math.mul(rotScale, -translation));
        public float2 Transform(float2 point) => math.mul(rotScale, point + translation);

        public readonly AffineTransform32 CalculatePCATransformation(NativeArray<float2> positions)
        {
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

            var partialTransform = Rotate(math.transpose(rotationMatrix)) * Translate(-com);
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

            return Scale(s) * Translate(-c) * partialTransform;
        }

        public readonly AffineTransform32 CalculateLocalTransformation(NativeArray<float2> positions)
        {
            float2 min = float.MaxValue, max = float.MinValue, com = 0;
            foreach (var p in positions)
            {
                min = math.min(p, min);
                max = math.max(p, max);
                com += p;
            }

            com /= positions.Length;
            var scale = 1 / math.cmax(math.max(math.abs(max - com), math.abs(min - com)));
            return Scale(scale) * Translate(-com);
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
    }

    internal readonly struct AffineTransform64 : ITransform<AffineTransform64, double, double2>
    {
        public readonly AffineTransform64 Identity => new(double2x2.identity, double2.zero);
        public readonly double AreaScalingFactor => math.abs(math.determinant(rotScale));

        private readonly double2x2 rotScale;
        private readonly double2 translation;

        public AffineTransform64(double2x2 rotScale, double2 translation) => (this.rotScale, this.translation) = (rotScale, translation);
        private static AffineTransform64 Translate(double2 offset) => new(double2x2.identity, offset);
        private static AffineTransform64 Scale(double2 scale) => new(new double2x2(scale.x, 0, 0, scale.y), double2.zero);
        private static AffineTransform64 Rotate(double2x2 rotation) => new(rotation, double2.zero);
        public static AffineTransform64 operator *(AffineTransform64 lhs, AffineTransform64 rhs) => new(
            math.mul(lhs.rotScale, rhs.rotScale),
            math.mul(math.inverse(rhs.rotScale), lhs.translation) + rhs.translation
        );

        public AffineTransform64 Inverse() => new(math.inverse(rotScale), math.mul(rotScale, -translation));
        public double2 Transform(double2 point) => math.mul(rotScale, point + translation);

        public readonly AffineTransform64 CalculatePCATransformation(NativeArray<double2> positions)
        {
            var com = (double2)0;
            foreach (var p in positions)
            {
                com += p;
            }
            com /= positions.Length;

            var cov = double2x2.zero;
            for (int i = 0; i < positions.Length; i++)
            {
                var q = positions[i] - com;
                cov += Kron(q, q);
            }
            cov /= positions.Length;

            Eigen(cov, out _, out var rotationMatrix);

            var partialTransform = Rotate(math.transpose(rotationMatrix)) * Translate(-com);
            double2 min = double.MaxValue;
            double2 max = double.MinValue;
            for (int i = 0; i < positions.Length; i++)
            {
                var p = partialTransform.Transform(positions[i]);
                min = math.min(p, min);
                max = math.max(p, max);
            }

            var c = 0.5f * (min + max);
            var s = 2f / (max - min);

            return Scale(s) * Translate(-c) * partialTransform;
        }

        public readonly AffineTransform64 CalculateLocalTransformation(NativeArray<double2> positions)
        {
            double2 min = double.MaxValue, max = double.MinValue, com = 0;
            foreach (var p in positions)
            {
                min = math.min(p, min);
                max = math.max(p, max);
                com += p;
            }

            com /= positions.Length;
            var scale = 1 / math.cmax(math.max(math.abs(max - com), math.abs(min - com)));
            return Scale(scale) * Translate(-com);
        }

        private static void Eigen(double2x2 matrix, out double2 eigval, out double2x2 eigvec)
        {
            var a00 = matrix[0][0];
            var a11 = matrix[1][1];
            var a01 = matrix[0][1];

            var a00a11 = a00 - a11;
            var p1 = a00 + a11;
            var p2 = (a00a11 >= 0 ? 1 : -1) * math.sqrt(a00a11 * a00a11 + 4 * a01 * a01);
            var lambda1 = p1 + p2;
            var lambda2 = p1 - p2;
            eigval = 0.5f * math.double2(lambda1, lambda2);

            var phi = 0.5f * math.atan2(2 * a01, a00a11);

            eigvec = math.double2x2
            (
                m00: math.cos(phi), m01: -math.sin(phi),
                m10: math.sin(phi), m11: math.cos(phi)
            );
        }
        private static double2x2 Kron(double2 a, double2 b) => math.double2x2(a * b[0], a * b[1]);
    }

    internal interface IUtils<T, T2> where T : unmanaged where T2 : unmanaged
    {
        T Const(float v);
        T MaxValue();
        T2 MaxValue2();
        T2 MinValue2();
        T2 NewT2(T x, T y);
        T X(T2 v);
        T Y(T2 v);
        T Zero();
#pragma warning disable IDE1006
        T abs(T v);
        T add(T a, T b);
        T alpha(T D, T d, bool initial);
        bool anyabslessthen(T a, T b, T c, T v);
        T atan2(T v, T w);
        T2 avg(T2 a, T2 b);
        T diff(T a, T b);
        T2 diff(T2 a, T2 b);
        T distance(T2 a, T2 b);
        T distancesq(T2 a, T2 b);
        T div(T a, T b);
        T dot(T2 a, T2 b);
        bool eq(T v, T w);
        bool2 eq(T2 v, T2 w);
        bool ge(T a, T b);
        bool2 ge(T2 a, T2 b);
        bool greater(T a, T b);
        int hashkey(T2 p, T2 c, int hashSize);
        bool2 isfinite(T2 v);
        bool le(T a, T b);
        bool2 le(T2 a, T2 b);
        T2 lerp(T2 a, T2 b, T v);
        bool less(T a, T b);
        T max(T v, T w);
        T2 max(T2 v, T2 w);
        T2 min(T2 v, T2 w);
        T mul(T a, T b);
        T neg(T v);
        T2 neg(T2 v);
        bool neq(T v, T w);
        T sign(T a);
#pragma warning restore IDE1006
    }

    internal readonly struct FloatUtils : IUtils<float, float2>
    {
        public readonly float Const(float v) => v;
        public readonly float MaxValue() => float.MaxValue;
        public readonly float2 MaxValue2() => float.MaxValue;
        public readonly float2 MinValue2() => float.MinValue;
        public readonly float2 NewT2(float x, float y) => math.float2(x, y);
        public readonly float X(float2 a) => a.x;
        public readonly float Y(float2 a) => a.y;
        public readonly float Zero() => 0;
        public readonly float abs(float v) => math.abs(v);
        public readonly float add(float a, float b) => a + b;
        public readonly float alpha(float D, float d, bool initial)
        {
            var k = (int)math.round(math.log2(0.5f * d / D));
            var alpha = D / d * (1 << k);
            return initial ? alpha : 1 - alpha;
        }
        public readonly bool anyabslessthen(float a, float b, float c, float v) => math.any(math.abs(math.float3(a, b, c)) < v);
        public readonly float atan2(float a, float b) => math.atan2(a, b);
        public readonly float2 avg(float2 a, float2 b) => 0.5f * (a + b);
        public readonly float diff(float a, float b) => a - b;
        public readonly float2 diff(float2 a, float2 b) => a - b;
        public readonly float distance(float2 a, float2 b) => math.distance(a, b);
        public readonly float distancesq(float2 a, float2 b) => math.distancesq(a, b);
        public readonly float div(float a, float b) => a / b;
        public readonly float dot(float2 a, float2 b) => math.dot(a, b);
        public readonly bool eq(float v, float w) => v == w;
        public readonly bool2 eq(float2 v, float2 w) => v == w;
        public readonly bool ge(float a, float b) => a >= b;
        public readonly bool2 ge(float2 a, float2 b) => a >= b;
        public readonly bool greater(float a, float b) => a > b;
        public readonly int hashkey(float2 p, float2 c, int hashSize)
        {
            return (int)math.floor(pseudoAngle(p.x - c.x, p.y - c.y) * hashSize) % hashSize;

            static float pseudoAngle(float dx, float dy)
            {
                var p = dx / (math.abs(dx) + math.abs(dy));
                return (dy > 0 ? 3 - p : 1 + p) / 4; // [0..1]
            }
        }
        public readonly bool2 isfinite(float2 v) => math.isfinite(v);
        public readonly bool le(float a, float b) => a <= b;
        public readonly bool2 le(float2 a, float2 b) => a <= b;
        public readonly float2 lerp(float2 a, float2 b, float v) => math.lerp(a, b, v);
        public readonly bool less(float a, float b) => a < b;
        public readonly float max(float v, float w) => math.max(v, w);
        public readonly float2 max(float2 v, float2 w) => math.max(v, w);
        public readonly float2 min(float2 v, float2 w) => math.min(v, w);
        public readonly float mul(float a, float b) => a * b;
        public readonly float neg(float v) => -v;
        public readonly float2 neg(float2 v) => -v;
        public readonly bool neq(float v, float w) => v != w;
        public readonly float sign(float a) => math.sign(a);
    }

    internal readonly struct DoubleUtils : IUtils<double, double2>
    {
        public readonly double Const(float v) => v;
        public readonly double MaxValue() => double.MaxValue;
        public readonly double2 MaxValue2() => double.MaxValue;
        public readonly double2 MinValue2() => double.MinValue;
        public readonly double2 NewT2(double x, double y) => math.double2(x, y);
        public readonly double X(double2 a) => a.x;
        public readonly double Y(double2 a) => a.y;
        public readonly double Zero() => 0;
        public readonly double abs(double v) => math.abs(v);
        public readonly double add(double a, double b) => a + b;
        public readonly double alpha(double D, double d, bool initial)
        {
            var k = (int)math.round(math.log2(0.5f * d / D));
            var alpha = D / d * (1 << k);
            return initial ? alpha : 1 - alpha;
        }
        public readonly bool anyabslessthen(double a, double b, double c, double v) => math.any(math.abs(math.double3(a, b, c)) < v);
        public readonly double atan2(double a, double b) => math.atan2(a, b);
        public readonly double2 avg(double2 a, double2 b) => 0.5f * (a + b);
        public readonly double diff(double a, double b) => a - b;
        public readonly double2 diff(double2 a, double2 b) => a - b;
        public readonly double distance(double2 a, double2 b) => math.distance(a, b);
        public readonly double distancesq(double2 a, double2 b) => math.distancesq(a, b);
        public readonly double div(double a, double b) => a / b;
        public readonly double dot(double2 a, double2 b) => math.dot(a, b);
        public readonly bool eq(double v, double w) => v == w;
        public readonly bool2 eq(double2 v, double2 w) => v == w;
        public readonly bool ge(double a, double b) => a >= b;
        public readonly bool2 ge(double2 a, double2 b) => a >= b;
        public readonly bool greater(double a, double b) => a > b;
        public readonly int hashkey(double2 p, double2 c, int hashSize)
        {
            return (int)math.floor(pseudoAngle(p.x - c.x, p.y - c.y) * hashSize) % hashSize;

            static double pseudoAngle(double dx, double dy)
            {
                var p = dx / (math.abs(dx) + math.abs(dy));
                return (dy > 0 ? 3 - p : 1 + p) / 4; // [0..1]
            }
        }
        public readonly bool2 isfinite(double2 v) => math.isfinite(v);
        public readonly bool le(double a, double b) => a <= b;
        public readonly bool2 le(double2 a, double2 b) => a <= b;
        public readonly double2 lerp(double2 a, double2 b, double v) => math.lerp(a, b, v);
        public readonly bool less(double a, double b) => a < b;
        public readonly double max(double v, double w) => math.max(v, w);
        public readonly double2 max(double2 v, double2 w) => math.max(v, w);
        public readonly double2 min(double2 v, double2 w) => math.min(v, w);
        public readonly double mul(double a, double b) => a * b;
        public readonly double neg(double v) => -v;
        public readonly double2 neg(double2 v) => -v;
        public readonly bool neq(double v, double w) => v != w;
        public readonly double sign(double a) => math.sign(a);
    }

    public static class Extensions
    {
        public static void Run(this Triangulator<float2> @this) =>
            new TriangulationJob<float, float2, AffineTransform32, FloatUtils>(@this).Run();
        public static JobHandle Schedule(this Triangulator<float2> @this, JobHandle dependencies = default) =>
            new TriangulationJob<float, float2, AffineTransform32, FloatUtils>(@this).Schedule(dependencies);

        public static void Run(this Triangulator<double2> @this) =>
            new TriangulationJob<double, double2, AffineTransform64, DoubleUtils>(@this).Run();
        public static JobHandle Schedule(this Triangulator<double2> @this, JobHandle dependencies = default) =>
            new TriangulationJob<double, double2, AffineTransform64, DoubleUtils>(@this).Schedule(dependencies);
    }
}
