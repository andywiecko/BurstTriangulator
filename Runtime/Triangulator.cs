/*
MIT License

Copyright (c) 2021 Andrzej Więckowski, Ph.D., https://github.com/andywiecko/BurstTriangulator

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

*/
using andywiecko.BurstTriangulator.LowLevel.Unsafe;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_MATHEMATICS_FIXEDPOINT
using Unity.Mathematics.FixedPoint;
#endif
using static andywiecko.BurstTriangulator.Utilities;

[assembly: InternalsVisibleTo("andywiecko.BurstTriangulator.Tests")]

namespace andywiecko.BurstTriangulator
{
    /// <summary>
    /// An <see cref="Enum"/> representing the status of triangulation.
    /// To check if the triangulation was successful, compare the status with <see cref="Status.OK"/>.
    /// </summary>
    [Flags]
    public enum Status
    {
#pragma warning disable IDE0055
        /// <summary>
        /// Triangulation completed successfully.
        /// </summary>
        OK                                   = 0x0000_0000,
        /// <summary>
        /// An error occurred during triangulation. See the console for more details.
        /// </summary>
        ERR                                  = 0x0000_0001,
        /// <summary>
        /// Invalid triangulation settings detected.
        /// </summary>
        ERR_ARGS_INVALID                     = 0x0000_0002 | ERR,
        /// <summary>
        /// Error when the length of <see cref="InputData{T2}.Positions"/> is less than 3.
        /// </summary>
        ERR_INPUT_POSITIONS_LENGTH           = 0x0000_0004 | ERR,
        /// <summary>
        /// Error when <see cref="InputData{T2}.Positions"/> contains an undefined value, such as
        /// <see cref="float.NaN"/>, <see cref="float.PositiveInfinity"/>, or <see cref="float.NegativeInfinity"/>.
        /// </summary>
        ERR_INPUT_POSITIONS_UNDEFINED_VALUE  = 0x0000_0008 | ERR,
        /// <summary>
        /// Error when <see cref="InputData{T2}.Positions"/> contains duplicate values.
        /// </summary>
        ERR_INPUT_POSITIONS_DUPLICATES       = 0x0000_0010 | ERR,
        /// <summary>
        /// Error when the length of <see cref="InputData{T2}.ConstraintEdges"/> is not a multiple of 2.
        /// </summary>
        ERR_INPUT_CONSTRAINTS_LENGTH         = 0x0000_0020 | ERR,
        /// <summary>
        /// Error when <see cref="InputData{T2}.ConstraintEdges"/> contains a constraint outside the range of
        /// <see cref="InputData{T2}.Positions"/>.
        /// </summary>
        ERR_INPUT_CONSTRAINTS_OUT_OF_RANGE   = 0x0000_0040 | ERR,
        /// <summary>
        /// Error when <see cref="InputData{T2}.ConstraintEdges"/> contains a self-loop constraint, such as (1, 1).
        /// </summary>
        ERR_INPUT_CONSTRAINTS_SELF_LOOP      = 0x0000_0080 | ERR,
        /// <summary>
        /// Error when <see cref="InputData{T2}.ConstraintEdges"/> contains a collinear point not part of the constraint.
        /// </summary>
        ERR_INPUT_CONSTRAINTS_COLLINEAR      = 0x0000_0100 | ERR,
        /// <summary>
        /// Error when <see cref="InputData{T2}.ConstraintEdges"/> contains duplicate constraints,
        /// such as (0, 1) and (1, 0).
        /// </summary>
        ERR_INPUT_CONSTRAINTS_DUPLICATES     = 0x0000_0200 | ERR,
        /// <summary>
        /// Error when <see cref="InputData{T2}.ConstraintEdges"/> contains intersecting constraints.
        /// </summary>
        ERR_INPUT_CONSTRAINTS_INTERSECTING   = 0x0000_0400 | ERR,
        /// <summary>
        /// Error when <see cref="InputData{T2}.HoleSeeds"/> contains an undefined value, such as
        /// <see cref="float.NaN"/>, <see cref="float.PositiveInfinity"/>, or <see cref="float.NegativeInfinity"/>.
        /// </summary>
        ERR_INPUT_HOLES_UNDEFINED_VALUE      = 0x0000_0800 | ERR,
        /// <summary>
        /// Error when the length of <see cref="InputData{T2}.IgnoreConstraintForPlantingSeeds"/>
        /// is not half the length of <see cref="InputData{T2}.ConstraintEdges"/>.
        /// </summary>
        ERR_INPUT_IGNORED_CONSTRAINTS_LENGTH = 0x0000_1000 | ERR,
        /// <summary>
        /// Error when <see cref="InputData{T2}.Positions"/> contains duplicate or fully collinear points,
        /// detected during the Delaunay triangulation step.
        /// </summary>
        ERR_DELAUNAY_DUPLICATES_OR_COLLINEAR = 0x0000_2000 | ERR,
        /// <summary>
        /// Error when constrained triangulation gets stuck during the Sloan algorithm.
        /// Consider increasing <see cref="TriangulationSettings.SloanMaxIters"/>.
        /// </summary>
        ERR_SLOAN_ITERS_EXCEEDED             = 0x0000_4000 | ERR,
        /// <summary>
        /// Error when mesh refinement is scheduled for a type T that does not support refinement.
        /// </summary>
        ERR_REFINEMENT_UNSUPPORTED           = 0x0000_8000 | ERR,
#pragma warning restore IDE0055
    }

    /// <summary>
    /// An <see cref="Enum"/> representing the type of transformation applied to input positions before triangulation.
    /// </summary>
    public enum Preprocessor
    {
        /// <summary>
        /// Identity transformation.
        /// </summary>
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

    /// <summary>
    /// An <see cref="Enum"/> representing the UV mapping method used in the re-triangulation utility.
    /// </summary>
    /// <seealso cref="Extensions.Retriangulate"/>
    /// <seealso cref="RetriangulateMeshJob"/>
    public enum UVMap { None, Planar, Barycentric };

    /// <summary>
    /// An <see cref="Enum"/> representing possible 3D-to-2D projection axes, used in the re-triangulation utility.
    /// </summary>
    /// <seealso cref="Extensions.Retriangulate"/>
    /// <seealso cref="RetriangulateMeshJob"/>
    public enum Axis { XY, XZ, YX, YZ, ZX, ZY }

    /// <summary>
    /// A helper class for setting up refinement thresholds.
    /// </summary>
    [Serializable]
    public class RefinementThresholds
    {
        /// <summary>
        /// Specifies the maximum area constraint for triangles in the resulting mesh refinement.
        /// Ensures that no triangle in the mesh has an area larger than the specified value.
        /// </summary>
        [field: SerializeField, Min(0)]
        [field: Tooltip(
            "Specifies the maximum area constraint for triangles in the resulting mesh refinement. " +
            "Ensures that no triangle in the mesh has an area larger than the specified value."
        )]
        public float Area { get; set; } = 1f;
        /// <summary>
        /// Specifies the refinement angle constraint for triangles in the resulting mesh.
        /// Ensures that no triangle in the mesh has an angle smaller than the specified value.
        /// </summary>
        /// <remarks>
        /// Expressed in <em>radians</em>. Note that in the literature, the upper boundary for convergence is approximately π / 6.
        /// </remarks>
        [field: SerializeField, Range(0, math.PI / 4)]
        [field: Tooltip(
            "Specifies the refinement angle constraint for triangles in the resulting mesh. " +
            "Ensures that no triangle in the mesh has an angle smaller than the specified value.\n\n" +
            "Expressed in <i>radians</i>. Note that in the literature, the upper boundary for convergence is approximately π / 6.")]
        public float Angle { get; set; } = math.radians(5);
    }

    /// <summary>
    /// A helper class for configuring triangulation parameters.
    /// </summary>
    [Serializable]
    public class TriangulationSettings
    {
        /// <summary>
        /// If set to <see langword="true"/>, the provided <see cref="InputData{T2}"/> and <see cref="TriangulationSettings"/> 
        /// will be validated before executing the triangulation procedure. The input <see cref="InputData{T2}.Positions"/>, 
        /// <see cref="InputData{T2}.ConstraintEdges"/>, and <see cref="TriangulationSettings"/> have certain restrictions. 
        /// For more details, see the <see href="https://andywiecko.github.io/BurstTriangulator/manual/advanced/input-validation.html">manual</see>.
        /// If any of the validation conditions are not met, the triangulation will not be performed. 
        /// This can be detected as an error by checking the <see cref="OutputData{T2}.Status"/> value (native, and usable in jobs).
        /// Additionally, if <see cref="Verbose"/> is set to <see langword="true"/>, corresponding errors/warnings will be logged in the Console.
        /// Note that some conditions may result in warnings only.
        /// </summary>
        /// <remarks>
        /// Input validation can be expensive. If you are certain of your input, consider disabling this option for additional performance.
        /// </remarks>
        [field: Header("General")]
        [field: SerializeField, Tooltip(
            "If set to true, the provided Input and TriangulationSettings will be validated before executing the triangulation procedure. " +
            "The input Positions, ConstraintEdges, and TriangulationSettings have certain restrictions. " +
            "For more details, see the manual. If any of the validation conditions are not met, the triangulation will not be performed. " +
            "This can be detected as an error by checking the Output.Status value (native, and usable in jobs)." +
            "Additionally, if Verbose is set to true, corresponding errors/warnings will be logged in the Console. " +
            "Note that some conditions may result in warnings only.\n" +
            "\n" +
            "<b>Input validation can be expensive. If you are certain of your input, consider disabling this option for additional performance.</b>"
        )]
        public bool ValidateInput { get; set; } = true;
        /// <summary>
        /// If set to <see langword="true"/>, caught errors and warnings with <see cref="Triangulator"/> will be logged in the Console.
        /// </summary>
        /// <remarks>
        /// See also the <see cref="ValidateInput"/> settings.
        /// </remarks>
        /// <seealso cref="ValidateInput"/>
        [field: SerializeField, Tooltip("If set to true, caught errors and warnings with Triangulator will be logged in the Console.")]
        public bool Verbose { get; set; } = true;
        /// <summary>
        /// Preprocessing algorithm for the input data. Default is <see cref="Preprocessor.None"/>.
        /// </summary>
        [field: SerializeField, Tooltip("Preprocessing algorithm for the input data.")]
        public Preprocessor Preprocessor { get; set; } = Preprocessor.None;

        /// <summary>
        /// If set to <see langword="true"/>, holes and boundaries will be created automatically
        /// depending on the provided <see cref="InputData{T2}.ConstraintEdges"/>.
        /// </summary>
        /// <remarks>
        /// The current implementation detects only <em>1-level islands</em>.
        /// It will not detect holes in <em>solid</em> meshes inside other holes.
        /// </remarks>
        [field: Header("Constraints and holes")]
        [field: SerializeField]
        [field: Tooltip(
            "If set to true, holes and boundaries will be created automatically " +
            "depending on the provided Input.ConstraintEdges.\n" +
            "\n" +
            "The current implementation detects only <i>1-level islands</i>. " +
            "It will not detect holes in <i>solid</i> meshes inside other holes."
        )]
        public bool AutoHolesAndBoundary { get; set; } = false;
        /// <summary>
        /// If <see langword="true"/> the mesh boundary is restored using <see cref="InputData{T}.ConstraintEdges"/>.
        /// </summary>
        [field: SerializeField]
        [field: Tooltip("If true the mesh boundary is restored using Input.ConstraintEdges.")]
        public bool RestoreBoundary { get; set; } = false;
        /// <summary>
        /// Max iteration count during Sloan's algorithm (constraining edges).
        /// <b>Modify this only if you know what you are doing.</b>
        /// </summary>
        [field: SerializeField, Min(0)]
        [field: Tooltip(
            "Max iteration count during <i>Sloan's algorithm</i> (constraining edges). " +
            "<b>Modify this only if you know what you are doing.</b>"
        )]
        public int SloanMaxIters { get; set; } = 1_000_000;

        /// <summary>
        /// If <see langword="true"/> refines mesh using
        /// <see href="https://en.wikipedia.org/wiki/Delaunay_refinement#Ruppert's_algorithm">Ruppert's algorithm</see>.
        /// </summary>
        [field: Header("Refinement")]
        [field: SerializeField]
        [field: Tooltip("If true refines mesh using <i>Ruppert's algorithm</i>.")]
        public bool RefineMesh { get; set; } = false;
        /// <summary>
        /// Thresholds used in mesh refinement. See <see cref="RefinementThresholds.Angle"/> and <see cref="RefinementThresholds.Area"/>.
        /// </summary>
        [field: SerializeField, Tooltip("Thresholds used in mesh refinement. See Angle and Area.")]
        public RefinementThresholds RefinementThresholds { get; private set; } = new();
        /// <summary>
        /// Constant used in <em>concentric shells</em> segment splitting.
        /// <b>Modify this only if you know what you are doing!</b>
        /// </summary>
        [field: SerializeField, Min(0)]
        [field: Tooltip(
            "Constant used in <i>concentric shells</i> segment splitting. " +
            "<b>Modify this only if you know what you are doing!</b>"
            )]
        public float ConcentricShellsParameter { get; set; } = 0.001f;
    }

    /// <summary>
    /// A managed helper class that contains all native buffers for triangulation input.
    /// </summary>
    /// <typeparam name="T2">The coordinate type. Supported types include:
    /// <see cref="float2"/>,
    /// <see cref="Vector2"/>,
    /// <see cref="double2"/>,
    /// <see cref="fp2"/>,
    /// and
    /// <see cref="int2"/>.
    /// For more information on type restrictions, refer to the documentation.
    /// </typeparam>
    public class InputData<T2> where T2 : unmanaged
    {
        /// <summary>
        /// Positions of points used in triangulation.
        /// </summary>
        public NativeArray<T2> Positions { get; set; }
        /// <summary>
        /// Optional buffer for constraint edges. This array constrains specific edges to be included in the final 
        /// triangulation result. It should contain indexes corresponding to the <see cref="Positions"/> of the edges 
        /// in the format [a₀, a₁, b₀, b₁, c₀, c₁, ...], where (a₀, a₁), (b₀, b₁), (c₀, c₁), etc., represent the constraint edges.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b> If refinement is enabled, the provided constraints may be split during the refinement process.
        /// </remarks>
        public NativeArray<int> ConstraintEdges { get; set; }
        /// <summary>
        /// Optional buffer containing seeds for holes. These hole seeds serve as starting points for a removal process that 
        /// mimics the spread of a virus. During this process, <see cref="ConstraintEdges"/> act as barriers to prevent further propagation.
        /// For more information, refer to the documentation.
        /// </summary>
        public NativeArray<T2> HoleSeeds { get; set; }
        /// <summary>
        /// Optional buffer used to mark constraints that should be ignored during the seed planting step.
        /// The buffer length should be half the length of <see cref="ConstraintEdges"/>.
        /// </summary>
        public NativeArray<bool> IgnoreConstraintForPlantingSeeds { get; set; }
    }

    /// <summary>
    /// Allocation free input class with implicit cast to <see cref="InputData{T2}"/>.
    /// </summary>
    /// <exclude />
    [Obsolete("Use AsNativeArray(out Handle) instead! You can learn more in the project manual.")]
    public class ManagedInput<T2> where T2 : unmanaged
    {
        public T2[] Positions { get; set; }
        public int[] ConstraintEdges { get; set; }
        public T2[] HoleSeeds { get; set; }

        public static implicit operator InputData<T2>(ManagedInput<T2> input) => new()
        {
            Positions = input.Positions == null ? default : input.Positions.AsNativeArray(),
            ConstraintEdges = input.ConstraintEdges == null ? default : input.ConstraintEdges.AsNativeArray(),
            HoleSeeds = input.HoleSeeds == null ? default : input.HoleSeeds.AsNativeArray(),
        };
    }

    /// <summary>
    /// A managed helper class that contains all native buffers for triangulation output.
    /// </summary>
    /// <typeparam name="T2">The coordinate type. Supported types include:
    /// <see cref="float2"/>,
    /// <see cref="Vector2"/>,
    /// <see cref="double2"/>,
    /// <see cref="fp2"/>,
    /// and
    /// <see cref="int2"/>.
    /// For more information on type restrictions, refer to the documentation.
    /// </typeparam>
    public class OutputData<T2> where T2 : unmanaged
    {
        /// <summary>
        /// Positions of triangulation points.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b> This buffer may include additional points than <see cref="InputData{T2}.Positions"/> if refinement is enabled. 
        /// Additionally, the positions might differ slightly (by a small ε) if a <see cref="TriangulationSettings.Preprocessor"/> is applied.
        /// </remarks>
        public NativeList<T2> Positions => owner.outputPositions;
        /// <summary>
        /// Continuous buffer of resulting triangles. All triangles are guaranteed to be oriented clockwise.
        /// </summary>
        public NativeList<int> Triangles => owner.triangles;
        /// <summary>
        /// Status of the triangulation. Retrieve this value to detect any errors that occurred during triangulation.
        /// </summary>
        public NativeReference<Status> Status => owner.status;
        /// <summary>
        /// Continuous buffer of resulting halfedges. A value of -1 indicates that there is no corresponding opposite halfedge.
        /// For more information, refer to the documentation on halfedges.
        /// </summary>
        public NativeList<int> Halfedges => owner.halfedges;
        /// <summary>
        /// Buffer corresponding to <see cref="Halfedges"/>. <see langword="true"/> indicates that the halfedge is constrained, <see langword="false"/> otherwise.
        /// </summary>
        /// <seealso cref="IgnoredHalfedgesForPlantingSeeds"/>
        public NativeList<bool> ConstrainedHalfedges => owner.constrainedHalfedges;
        /// <summary>
        /// Buffer corresponding to <see cref="Halfedges"/>. <see langword="true"/> indicates that the halfedge was ignored during planting seed step, <see langword="false"/> otherwise. Constraint edges to ignore can be set in input using <see cref="InputData{T2}.IgnoreConstraintForPlantingSeeds"/>.
        /// </summary>
        /// <remarks>
        /// This buffer is particularly useful when using <see cref="UnsafeTriangulator"/>.
        /// </remarks>
        /// <seealso cref="ConstrainedHalfedges"/>
        public NativeList<bool> IgnoredHalfedgesForPlantingSeeds => owner.ignoredHalfedgesForPlantingSeeds;

        private readonly Triangulator<T2> owner;
        /// <exclude />
        [Obsolete("This will be converted into internal ctor.")]
        public OutputData(Triangulator<T2> owner) => this.owner = owner;
    }

    /// <summary>
    /// A handle that prevents an object from being deallocated by the garbage collector (GC).
    /// Call <see cref="Free"/> to release the object.
    /// </summary>
    /// <seealso cref="Extensions.AsNativeArray{T}(T[], out Handle)"/>
    public readonly struct Handle
    {
        private readonly ulong gcHandle;
        /// <summary>
        /// Creates a <see cref="Handle"/>.
        /// </summary>
        /// <param name="gcHandle">The handle value, which can be obtained e.g. from 
        /// <see cref="UnsafeUtility.PinGCArrayAndGetDataAddress(Array, out ulong)"/> or 
        /// <see cref="UnsafeUtility.PinGCObjectAndGetAddress(object, out ulong)"/>.
        /// </param>
        /// <seealso cref="Extensions.AsNativeArray{T}(T[], out Handle)"/>
        public Handle(ulong gcHandle) => this.gcHandle = gcHandle;
        /// <summary>
        /// Releases the handle, allowing the object to be collected by the garbage collector.
        /// </summary>
        public readonly void Free() => UnsafeUtility.ReleaseGCObject(gcHandle);
    }

    /// <summary>
    /// A base class for triangulation, which provides the core functionality for performing triangulation on 2D geometric data.
    /// It acts as a wrapper for <see cref="Triangulator{T2}"/> where T2 is <see cref="double2"/>.
    /// </summary>
    /// <remarks>
    /// The triangulation process can be configured using the parameters available in <see cref="Settings"/>.
    /// Provide the input data via <see cref="Input"/>, and then execute the triangulation using
    /// <see cref="Run"/> or <see cref="Schedule(JobHandle)"/>.
    /// The triangulation results will be available in <see cref="Output"/>.
    /// Ensure you call <see cref="Dispose"/> on the object after use to release resources.
    /// Example usage:
    /// <code>
    /// using var positions = new NativeArray&lt;double2&gt;(new[]
    /// {
    ///     new (0, 0), new (1, 0), new (1, 1), new (0, 1)
    /// }, Allocator.Persistent);
    /// using var triangulator = new Triangulator(Allocator.Persistent)
    /// {
    ///     Input = { Positions = positions },
    ///     Settings = { ValidateInput = false },
    /// };
    ///
    /// triangulator.Run();
    ///
    /// var triangles = triangulator.Output.Triangles;
    /// </code>
    /// For advanced customization of the triangulation process, refer to <see cref="UnsafeTriangulator{T2}"/>.
    /// </remarks>
    /// <seealso cref="Triangulator{T2}"/>
    /// <seealso cref="UnsafeTriangulator{T2}"/>
    public class Triangulator : INativeDisposable
    {
        /// <summary>
        /// Settings used for triangulation.
        /// </summary>
        public TriangulationSettings Settings { get => impl.Settings; set => impl.Settings = value; }
        /// <summary>
        /// Input data used for triangulation.
        /// </summary>
        public InputData<double2> Input { get => impl.Input; set => impl.Input = value; }
        /// <summary>
        /// Output data resulting from the triangulation process.
        /// </summary>
        public OutputData<double2> Output => impl.Output;
        private readonly Triangulator<double2> impl;
        /// <summary>
        /// Initializes a new instance of the <see cref="Triangulator"/> class with the specified <paramref name="capacity"/> and memory <paramref name="allocator"/>.
        /// </summary>
        /// <param name="capacity">The capacity of the triangulator.</param>
        /// <param name="allocator">The allocator to use.</param>
        public Triangulator(int capacity, Allocator allocator) => impl = new(capacity, allocator);
        /// <summary>
        /// Initializes a new instance of the <see cref="Triangulator"/> class with the default capacity (16×1024) and specified memory <paramref name="allocator"/>.
        /// </summary>
        /// <param name="allocator">The allocator to use.</param>
        public Triangulator(Allocator allocator) => impl = new(allocator);

        /// <summary>
        /// Releases all resources (memory and safety handles).
        /// </summary>
        public void Dispose() => impl.Dispose();

        /// <summary>
        /// Creates and schedules a job that releases all resources (memory and safety handles).
        /// </summary>
        /// <param name="dependencies">The dependency for the new job.</param>
        /// <remarks>
        /// Note: Consider using <see cref="UnsafeTriangulator"/>, which provides more customization for data management.
        /// </remarks>
        /// <returns>
        /// The handle of the new job. The job depends upon <paramref name="dependencies"/> and releases all resources (memory and safety handles).
        /// </returns>
        public JobHandle Dispose(JobHandle dependencies) => impl.Dispose(dependencies);

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

    /// <summary>
    /// A generic class for triangulation of coordinate type <typeparamref name="T2"/>, which provides the core functionality for performing triangulation on 2D geometric data.
    /// </summary>
    /// <remarks>
    /// The triangulation process can be configured using the parameters available in <see cref="Settings"/>. 
    /// Provide the input data via <see cref="Input"/>, and then execute the triangulation using a proper extension
    /// <see cref="Extensions.Run"/> or <see cref="Extensions.Schedule"/>.
    /// The triangulation results will be available in <see cref="Output"/>.
    /// Ensure you call <see cref="Dispose"/> on the object after use to release resources.
    /// Example usage:
    /// <code>
    /// using var positions = new NativeArray&lt;double2&gt;(new[]
    /// {
    ///     new (0, 0), new (1, 0), new (1, 1), new (0, 1)
    /// }, Allocator.Persistent);
    /// using var triangulator = new Triangulator&lt;double2&gt;(Allocator.Persistent)
    /// {
    ///     Input = { Positions = positions },
    ///     Settings = { ValidateInput = false },
    /// };
    ///
    /// triangulator.Run();
    ///
    /// var triangles = triangulator.Output.Triangles;
    /// </code>
    /// For advanced customization of the triangulation process, refer to <see cref="UnsafeTriangulator{T2}"/>.
    /// </remarks>
    /// <typeparam name="T2">The coordinate type. Supported types include:
    /// <see cref="float2"/>,
    /// <see cref="Vector2"/>,
    /// <see cref="double2"/>,
    /// <see cref="fp2"/>,
    /// and
    /// <see cref="int2"/>.
    /// For more information on type restrictions, refer to the documentation.
    /// </typeparam>
    /// <seealso cref="Triangulator"/>
    /// <seealso cref="UnsafeTriangulator{T2}"/>
    /// <seealso cref="Extensions"/>
    public class Triangulator<T2> : INativeDisposable where T2 : unmanaged
    {
        /// <summary>
        /// Settings used for triangulation.
        /// </summary>
        public TriangulationSettings Settings { get; set; } = new();
        /// <summary>
        /// Input data used for triangulation.
        /// </summary>
        public InputData<T2> Input { get; set; } = new();
        /// <summary>
        /// Output data resulting from the triangulation process.
        /// </summary>
        public OutputData<T2> Output { get; }

        internal NativeList<T2> outputPositions;
        internal NativeList<int> triangles;
        internal NativeList<int> halfedges;
        internal NativeList<bool> constrainedHalfedges;
        internal NativeList<bool> ignoredHalfedgesForPlantingSeeds;
        internal NativeReference<Status> status;

        /// <summary>
        /// Initializes a new instance of the <see cref="Triangulator{T2}"/> class with the specified <paramref name="capacity"/> and memory <paramref name="allocator"/>.
        /// </summary>
        /// <param name="capacity">The capacity of the triangulator.</param>
        /// <param name="allocator">The allocator to use.</param>
        public Triangulator(int capacity, Allocator allocator)
        {
            outputPositions = new(capacity, allocator);
            triangles = new(6 * capacity, allocator);
            status = new(Status.OK, allocator);
            halfedges = new(6 * capacity, allocator);
            constrainedHalfedges = new(6 * capacity, allocator);
            ignoredHalfedgesForPlantingSeeds = new(6 * capacity, allocator);
#pragma warning disable CS0618
            Output = new(this);
#pragma warning restore CS0618
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Triangulator{T2}"/> class with the default capacity (16×1024) and memory <paramref name="allocator"/>.
        /// </summary>
        /// <param name="allocator">The allocator to use.</param>
        public Triangulator(Allocator allocator) : this(capacity: 16 * 1024, allocator) { }

        /// <summary>
        /// Releases all resources (memory and safety handles).
        /// </summary>
        public void Dispose()
        {
            outputPositions.Dispose();
            triangles.Dispose();
            status.Dispose();
            halfedges.Dispose();
            constrainedHalfedges.Dispose();
            ignoredHalfedgesForPlantingSeeds.Dispose();
        }

        /// <summary>
        /// Creates and schedules a job that releases all resources (memory and safety handles).
        /// </summary>
        /// <param name="dependencies">The dependency for the new job.</param>
        /// <remarks>
        /// Note: Consider using <see cref="UnsafeTriangulator"/>, which provides more customization for data management.
        /// </remarks>
        /// <returns>
        /// The handle of the new job. The job depends upon <paramref name="dependencies"/> and releases all resources (memory and safety handles).
        /// </returns>
        public JobHandle Dispose(JobHandle dependencies)
        {
            dependencies = outputPositions.Dispose(dependencies);
            dependencies = triangles.Dispose(dependencies);
            dependencies = status.Dispose(dependencies);
            dependencies = halfedges.Dispose(dependencies);
            dependencies = constrainedHalfedges.Dispose(dependencies);
            dependencies = ignoredHalfedgesForPlantingSeeds.Dispose(dependencies);
            return dependencies;
        }
    }

    /// <summary>
    /// Provides extension methods.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Returns <see cref="NativeArray{T}"/> view on managed <paramref name="array"/>.
        /// </summary>
        /// <typeparam name="T">The type of the elements.</typeparam>
        /// <param name="array">Array to </param>
        /// <returns>View on managed <paramref name="array"/> with <see cref="NativeArray{T}"/>.</returns>
        /// <exclude />
        [Obsolete("Use AsNativeArray(out Handle) instead! You can learn more in the project manual.")]
        unsafe public static NativeArray<T> AsNativeArray<T>(this T[] array) where T : unmanaged
        {
            var ret = default(NativeArray<T>);
            // In Unity 2023.2+ pointers are not required, one can use Span<T> instead.
            fixed (void* ptr = array)
            {
                ret = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(ptr, array.Length, Allocator.None);
            }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var m_SafetyHandle = AtomicSafetyHandle.Create();
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref ret, m_SafetyHandle);
#endif
            return ret;
        }

        /// <summary>
        /// Returns <see cref="NativeArray{T}"/> view on managed <paramref name="array"/>
        /// with <paramref name="handle"/> to prevents from deallocation.
        /// <para/>
        /// <b>Warning!</b> User has to call <see cref="Handle.Free"/> 
        /// manually to release the data for GC! Read more in the project manual.
        /// </summary>
        /// <typeparam name="T">The type of the elements.</typeparam>
        /// <param name="array">Array to view.</param>
        /// <param name="handle">A handle that prevents the <paramref name="array"/> from being deallocated by the GC.</param>
        /// <returns><see cref="NativeArray{T}"/> view on managed <paramref name="array"/> with <see cref="NativeArray{T}"/>.</returns>
        public static unsafe NativeArray<T> AsNativeArray<T>(this T[] array, out Handle handle) where T : unmanaged
        {
            var ptr = UnsafeUtility.PinGCArrayAndGetDataAddress(array, out var gcHandle);
            var ret = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(ptr, array.Length, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var m_SafetyHandle = AtomicSafetyHandle.Create();
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref ret, m_SafetyHandle);
#endif
            handle = new(gcHandle);
            return ret;
        }

        /// <summary>
        /// Retriangulates <paramref name="mesh"/> in place using the provided <paramref name="settings"/>.
        /// </summary>
        /// <remarks>
        /// This method supports non-uniform meshes, including those with <em>windmill</em>-like connections between triangles.
        /// It also provides limited UV interpolation (see <paramref name="uvMap"/>),
        /// however, in complex cases, manual handling of UVs may be required after retriangulation.
        /// Refer to the documentation for more details. Advanced customization is available through <see cref="RetriangulateMeshJob"/>.
        /// </remarks>
        /// <param name="mesh">The mesh to be retriangulated.</param>
        /// <param name="settings">
        /// The settings used during retriangulation. If <see langword="null"/> is provided,
        /// the default settings with <see cref="TriangulationSettings.AutoHolesAndBoundary"/> <b>enabled</b> will be used.
        /// <b>Note:</b> It is recommended to enable <see cref="TriangulationSettings.AutoHolesAndBoundary"/> in <paramref name="settings"/>, as it is disabled by default.
        /// </param>
        /// <param name="axisInput">The axis from <see cref="Mesh.vertices"/> to use for retriangulation, by default <see cref="Axis.XY"/>.</param>
        /// <param name="axisOutput">The axis used to write the retriangulation result, by default <see cref="Axis.XY"/>.</param>
        /// <param name="uvMap">The UV mapping method for UV interpolation, by default <see cref="UVMap.None"/>.</param>
        /// <param name="uvChannelIndex">The UV channel index, in the range <tt>[0..7]</tt>.</param>
        /// <param name="subMeshIndex">The sub-mesh to modify.</param>
        /// <param name="generateInitialUVPlanarMap">If <see langword="true"/>, an initial UV map is generated using <see cref="UVMap.Planar"/> interpolation.</param>
        /// <param name="recalculateBounds">If <see langword="true"/>, recalculates the bounding volume of the Mesh and all of its sub-meshes with the vertex data.</param>
        /// <param name="recalculateNormals">If <see langword="true"/>, recalculates the normals of the Mesh from the triangles and vertices.</param>
        /// <param name="recalculateTangents">If <see langword="true"/>, recalculates the tangents of the Mesh from the normals and texture coordinates.</param>
        /// <param name="insertTriangleMidPoints">If <see langword="true"/>, additional points are inserted at the center of mass of the initial triangles before retriangulation.</param>
        /// <param name="insertEdgeMidPoints">If <see langword="true"/>, additional points are inserted at the center of the initial edges before retriangulation.</param>
        /// <exception cref="InvalidOperationException">Thrown when the provided mesh does not have a Position vertex component.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="uvChannelIndex"/> is out of the valid range <tt>[0, 7]</tt>.</exception>
        /// <exception cref="Exception">Thrown when retriangulation fails during the process. The exception should contain <see cref="Status"/>.</exception>
        /// <seealso cref="RetriangulateMeshJob"/>
        // TODO:
        // - add "assume uniform" (perf)
        // - add "third axis interpolation" (feat)
        public static void Retriangulate(this Mesh mesh,
            TriangulationSettings settings = default,
            Axis axisInput = Axis.XY,
            Axis axisOutput = Axis.XY,
            UVMap uvMap = UVMap.None,
            int uvChannelIndex = 0, // [0..7]
            int subMeshIndex = 0,
            bool generateInitialUVPlanarMap = false,
            bool recalculateBounds = true,
            bool recalculateNormals = true,
            bool recalculateTangents = true,
            bool insertTriangleMidPoints = false,
            bool insertEdgeMidPoints = false
        )
        {
            using var meshDataArray = Mesh.AcquireReadOnlyMeshData(mesh);
            // NOTE: It is guaranteed that meshDataArray.Length is 1 when constructing with `Mesh`.
            var meshData = meshDataArray[0];

            if (!meshData.HasVertexAttribute(VertexAttribute.Position))
            {
                throw new InvalidOperationException("Mesh data does not have Position vertex component.");
            }

            var hasUV = uvChannelIndex switch
            {
                0 => meshData.HasVertexAttribute(VertexAttribute.TexCoord0),
                1 => meshData.HasVertexAttribute(VertexAttribute.TexCoord1),
                2 => meshData.HasVertexAttribute(VertexAttribute.TexCoord2),
                3 => meshData.HasVertexAttribute(VertexAttribute.TexCoord3),
                4 => meshData.HasVertexAttribute(VertexAttribute.TexCoord4),
                5 => meshData.HasVertexAttribute(VertexAttribute.TexCoord5),
                6 => meshData.HasVertexAttribute(VertexAttribute.TexCoord6),
                7 => meshData.HasVertexAttribute(VertexAttribute.TexCoord7),
                _ => throw new ArgumentException($"{nameof(uvChannelIndex)} is out of range! It must be in the range [0, 7]."),
            };

            using var outputPositions = new NativeList<float3>(Allocator.Persistent);
            using var outputTriangles = new NativeList<int>(Allocator.Persistent);
            using var outputUVs = new NativeList<float2>(Allocator.Persistent);
            using var status = new NativeReference<Status>(Allocator.Persistent);
            new RetriangulateMeshJob(meshData, outputPositions, outputTriangles,
                outputUVs: hasUV ? outputUVs : default,
                status,
                args: settings ?? Args.Default(autoHolesAndBoundary: true),
                axisInput,
                axisOutput,
                uvMap,
                uvChannelIndex,
                subMeshIndex,
                generateInitialUVPlanarMap,
                insertTriangleMidPoints,
                insertEdgeMidPoints
            ).Run();

            if (status.Value != Status.OK)
            {
                throw new Exception($"Mesh re-triangulation failed with status: {status.Value}.");
            }

            mesh.SetVertices(outputPositions.AsArray());
            mesh.SetIndices(outputTriangles.AsArray(), MeshTopology.Triangles, subMeshIndex);
            if (hasUV) mesh.SetUVs(uvChannelIndex, outputUVs.AsArray());
            if (recalculateBounds) mesh.RecalculateBounds();
            if (recalculateNormals) mesh.RecalculateNormals();
            if (recalculateTangents) mesh.RecalculateTangents();
        }

        /// <summary>
        /// Perform the job's Execute method immediately on the same thread.
        /// </summary>
        public static void Run(this Triangulator<float2> @this) =>
            new TriangulationJob<float, float2, float, TransformFloat, UtilsFloat>(@this).Run();
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
        public static JobHandle Schedule(this Triangulator<float2> @this, JobHandle dependencies = default) =>
            new TriangulationJob<float, float2, float, TransformFloat, UtilsFloat>(@this).Schedule(dependencies);

        /// <summary>
        /// Perform the job's Execute method immediately on the same thread.
        /// </summary>
        public static void Run(this Triangulator<Vector2> @this) =>
            new TriangulationJob<float, float2, float, TransformFloat, UtilsFloat>(
                input: new() { Positions = @this.Input.Positions.Reinterpret<float2>(), ConstraintEdges = @this.Input.ConstraintEdges, HoleSeeds = @this.Input.HoleSeeds.Reinterpret<float2>(), IgnoreConstraintForPlantingSeeds = @this.Input.IgnoreConstraintForPlantingSeeds },
                output: new() { Triangles = @this.triangles, Halfedges = @this.halfedges, Positions = UnsafeUtility.As<NativeList<Vector2>, NativeList<float2>>(ref @this.outputPositions), Status = @this.status, ConstrainedHalfedges = @this.constrainedHalfedges, IgnoredHalfedgesForPlantingSeeds = @this.ignoredHalfedgesForPlantingSeeds },
                args: @this.Settings
        ).Run();
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
        public static JobHandle Schedule(this Triangulator<Vector2> @this, JobHandle dependencies = default) =>
            new TriangulationJob<float, float2, float, TransformFloat, UtilsFloat>(
                input: new() { Positions = @this.Input.Positions.Reinterpret<float2>(), ConstraintEdges = @this.Input.ConstraintEdges, HoleSeeds = @this.Input.HoleSeeds.Reinterpret<float2>(), IgnoreConstraintForPlantingSeeds = @this.Input.IgnoreConstraintForPlantingSeeds },
                output: new() { Triangles = @this.triangles, Halfedges = @this.halfedges, Positions = UnsafeUtility.As<NativeList<Vector2>, NativeList<float2>>(ref @this.outputPositions), Status = @this.status, ConstrainedHalfedges = @this.constrainedHalfedges, IgnoredHalfedgesForPlantingSeeds = @this.ignoredHalfedgesForPlantingSeeds },
                args: @this.Settings
        ).Schedule(dependencies);

        /// <summary>
        /// Perform the job's Execute method immediately on the same thread.
        /// </summary>
        public static void Run(this Triangulator<double2> @this) =>
            new TriangulationJob<double, double2, double, TransformDouble, UtilsDouble>(@this).Run();
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
        public static JobHandle Schedule(this Triangulator<double2> @this, JobHandle dependencies = default) =>
            new TriangulationJob<double, double2, double, TransformDouble, UtilsDouble>(@this).Schedule(dependencies);

        /// <summary>
        /// Perform the job's Execute method immediately on the same thread.
        /// </summary>
        public static void Run(this Triangulator<int2> @this) =>
            new TriangulationJob<int, int2, long, TransformInt, UtilsInt>(@this).Run();
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
        public static JobHandle Schedule(this Triangulator<int2> @this, JobHandle dependencies = default) =>
            new TriangulationJob<int, int2, long, TransformInt, UtilsInt>(@this).Schedule(dependencies);

#if UNITY_MATHEMATICS_FIXEDPOINT
        /// <summary>
        /// Perform the job's Execute method immediately on the same thread.
        /// </summary>
        public static void Run(this Triangulator<fp2> @this) =>
            new TriangulationJob<fp, fp2, fp, TransformFp, UtilsFp>(@this).Run();
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
        public static JobHandle Schedule(this Triangulator<fp2> @this, JobHandle dependencies = default) =>
            new TriangulationJob<fp, fp2, fp, TransformFp, UtilsFp>(@this).Schedule(dependencies);
#endif
    }

    /// <summary>
    /// A collection of utility functions related to triangulation.
    /// </summary>
    public static class Utilities
    {
        /// <summary>
        /// Generates <paramref name="halfedges"/> using the provided <paramref name="triangles"/>.
        /// </summary>
        /// <param name="halfedges">The buffer to be filled with halfedges. It must have the same length as <paramref name="triangles"/>.</param>
        /// <param name="triangles">The triangles used for halfedge generation.</param>
        /// <param name="allocator">The allocator to use for temporary data.</param>
        public static void GenerateHalfedges(Span<int> halfedges, ReadOnlySpan<int> triangles, Allocator allocator)
        {
            ThrowCheckGenerateHalfedges(halfedges, triangles);

            halfedges.Fill(-1);

            using var tmp = new NativeHashMap<int2, int>(triangles.Length, allocator);
            for (int he = 0; he < halfedges.Length; he++)
            {
                var e0 = triangles[he];
                var e1 = triangles[NextHalfedge(he)];
                (e0, e1) = e0 < e1 ? (e0, e1) : (e1, e0);
                var edge = math.int2(e0, e1);

                if (tmp.TryGetValue(edge, out var ohe))
                {
                    halfedges[he] = ohe;
                    halfedges[ohe] = he;
                }
                else
                {
                    tmp.Add(edge, he);
                }
            }
        }

        /// <summary>
        /// Generates triangle <paramref name="colors"/> using the provided <paramref name="halfedges"/>.
        /// Triangles that share a common edge are assigned the same color index.
        /// The resulting <paramref name="colors"/> contains values in the range <tt>[0, <paramref name="colorsCount"/>)</tt>.
        /// Check the documentation for further details.
        /// </summary>
        /// <param name="colors">A buffer that will be populated with triangle colors. Its length must be three times smaller than <paramref name="halfedges"/>.</param>
        /// <param name="halfedges">The halfedge data used for generating colors.</param>
        /// <param name="colorsCount">The total number of unique colors assigned.</param>
        /// <param name="allocator">The allocator to use for temporary data.</param>
        /// <seealso cref="GenerateHalfedges(Span{int}, ReadOnlySpan{int}, Allocator)"/>
        public static void GenerateTriangleColors(Span<int> colors, ReadOnlySpan<int> halfedges, out int colorsCount, Allocator allocator)
        {
            ThrowCheckGenerateTriangleColors(colors, halfedges);

            colorsCount = 0;
            colors.Fill(-1);

            using var heQueue = new NativeQueueList<int>(allocator);
            for (int t = 0; t < colors.Length; t++)
            {
                if (colors[t] == -1)
                {
                    heQueue.Enqueue(3 * t + 0);
                    heQueue.Enqueue(3 * t + 1);
                    heQueue.Enqueue(3 * t + 2);
                    colors[t] = colorsCount;
                    BFS(colorsCount++, colors, heQueue, halfedges);
                }
            }

            static void BFS(int color, Span<int> colors, NativeQueueList<int> heQueue, ReadOnlySpan<int> halfedges)
            {
                while (heQueue.TryDequeue(out var he))
                {
                    var ohe = halfedges[he];
                    var t = ohe / 3;
                    if (ohe == -1 || colors[t] != -1)
                    {
                        continue;
                    }

                    heQueue.Enqueue(3 * t + 0);
                    heQueue.Enqueue(3 * t + 1);
                    heQueue.Enqueue(3 * t + 2);
                    colors[t] = color;
                }
            }
        }

        /// <summary>
        /// Inserts a sub-mesh, defined by (<paramref name="subpositions"/>, <paramref name="subtriangles"/>), into the main mesh
        /// represented by (<paramref name="positions"/>, <paramref name="triangles"/>).
        /// The <paramref name="subtriangles"/> will be adjusted by the initial count of <paramref name="positions"/>
        /// to ensure proper indexing before insertion.
        /// </summary>
        /// <typeparam name="T">The data type of the positions.</typeparam>
        /// <param name="positions">The list of positions representing the main mesh.</param>
        /// <param name="triangles">The list of triangles defining the mesh.</param>
        /// <param name="subpositions">The positions buffer of the sub-mesh to insert.</param>
        /// <param name="subtriangles">The triangles buffer of the sub-mesh to insert.</param>
        public static unsafe void InsertSubMesh<T>(NativeList<T> positions, NativeList<int> triangles, ReadOnlySpan<T> subpositions, ReadOnlySpan<int> subtriangles)
            where T : unmanaged
        {
            var offset = positions.Length;
            var t0 = triangles.Length;

            fixed (void* p = subpositions)
            {
                positions.AddRange(p, subpositions.Length);
            }

            fixed (void* t = subtriangles)
            {
                triangles.AddRange(t, subtriangles.Length);
            }

            for (int i = t0; i < triangles.Length; i++)
            {
                triangles[i] += offset;
            }
        }

        /// <summary>
        /// Returns the next halfedge index after <paramref name="he"/>.
        /// Useful for iterating over a triangle mesh.
        /// </summary>
        /// <remarks>
        /// This method calculates the next halfedge index using:
        /// <c>he % 3 == 2 ? he - 2 : he + 1</c>.
        /// </remarks>
        /// <param name="he">The current halfedge index. Should be non-negative.</param>
        /// <returns>The next halfedge index.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int NextHalfedge(int he) => he % 3 == 2 ? he - 2 : he + 1;

        /// <summary>
        /// A job for performing manual retriangulation, similar to <see cref="Extensions.Retriangulate"/>.
        /// </summary>
        /// <seealso cref="Extensions.Retriangulate"/>
        [BurstCompile]
        public struct RetriangulateMeshJob : IJob
        {
            [ReadOnly]
            private Mesh.MeshData meshData;

            private NativeList<float3> outputPositions;
            private NativeList<int> outputTriangles;
            [NativeDisableContainerSafetyRestriction]
            private NativeList<float2> outputUVs;
            [NativeDisableContainerSafetyRestriction]
            private NativeReference<Status> status;
            private readonly Args args;
            private readonly Axis axisInput, axisOutput;
            private readonly UVMap uvMap;
            private readonly int uvChannelIndex, subMeshIndex;
            private readonly bool generateInitialUVPlanarMap, insertTriangleMidPoints, insertEdgeMidPoints;

            /// <summary>
            /// Constructs a new retriangulation job. Use <see cref="IJobExtensions.Run"/>, <see cref="IJobExtensions.Schedule"/>,
            /// or <see cref="Execute"/> to compute the result.
            /// </summary>
            /// <param name="meshData">The mesh data to be retriangulated.</param>
            /// <param name="outputPositions">A native list containing the retriangulated positions. Must be allocated by the user.</param>
            /// <param name="outputTriangles">A native list containing the retriangulated triangles. Must be allocated by the user.</param>
            /// <param name="outputUVs">An optional native list containing the retriangulated UVs. Must be allocated by the user.</param>
            /// <param name="status">The status of the retriangulation (optional native reference).</param>
            /// <param name="args">
            /// The settings used during retriangulation. If <see langword="default"/> is provided,
            /// the default settings with <see cref="Args.AutoHolesAndBoundary"/> <b>enabled</b> will be used.
            /// <b>Note:</b> It is recommended to enable <see cref="Args.AutoHolesAndBoundary"/> in <paramref name="args"/>, as it is disabled by default.
            /// </param>
            /// <param name="axisInput">The axis from <see cref="Mesh.vertices"/> to use for retriangulation, by default <see cref="Axis.XY"/>.</param>
            /// <param name="axisOutput">The axis used to write the retriangulation result, by default <see cref="Axis.XY"/>.</param>
            /// <param name="uvMap">The UV mapping method for UV interpolation, by default <see cref="UVMap.None"/>.</param>
            /// <param name="uvChannelIndex">The UV channel index, in the range <tt>[0..7]</tt>.</param>
            /// <param name="subMeshIndex">The sub-mesh to modify.</param>
            /// <param name="generateInitialUVPlanarMap">If <see langword="true"/>, an initial UV map is generated using <see cref="UVMap.Planar"/> interpolation.</param>
            /// <param name="insertTriangleMidPoints">If <see langword="true"/>, additional points are inserted at the center of mass of the initial triangles before retriangulation.</param>
            /// <param name="insertEdgeMidPoints">If <see langword="true"/>, additional points are inserted at the center of the initial edges before retriangulation.</param>
            public RetriangulateMeshJob(
                Mesh.MeshData meshData,
                NativeList<float3> outputPositions,
                NativeList<int> outputTriangles,
                NativeList<float2> outputUVs = default,
                NativeReference<Status> status = default,
                Args? args = default,
                Axis axisInput = Axis.XY,
                Axis axisOutput = Axis.XY,
                UVMap uvMap = UVMap.None,
                int uvChannelIndex = 0,
                int subMeshIndex = 0,
                bool generateInitialUVPlanarMap = false,
                bool insertTriangleMidPoints = false,
                bool insertEdgeMidPoints = false
            )
            {
                this.meshData = meshData;
                this.outputPositions = outputPositions;
                this.outputTriangles = outputTriangles;
                this.outputUVs = outputUVs;
                this.status = status;
                this.args = args ?? Args.Default(autoHolesAndBoundary: true);
                this.axisInput = axisInput;
                this.axisOutput = axisOutput;
                this.uvMap = uvMap;
                this.uvChannelIndex = uvChannelIndex;
                this.subMeshIndex = subMeshIndex;
                this.generateInitialUVPlanarMap = generateInitialUVPlanarMap;
                this.insertTriangleMidPoints = insertTriangleMidPoints;
                this.insertEdgeMidPoints = insertEdgeMidPoints;
            }

            public void Execute()
            {
                var indexCount = meshData.GetSubMesh(subMeshIndex).indexCount;
                using var inputPositions = new NativeArray<Vector3>(meshData.vertexCount, Allocator.Temp);
                using var inputTriangles = new NativeArray<int>(indexCount, Allocator.Temp);
                using var inputUVs = new NativeArray<Vector2>(meshData.vertexCount, Allocator.Temp);

                meshData.GetVertices(inputPositions);
                meshData.GetIndices(inputTriangles, subMeshIndex);
                if (outputUVs.IsCreated) meshData.GetUVs(uvChannelIndex, inputUVs);

                using var positions = new NativeArray<double2>(inputPositions.Length, Allocator.Temp);
                using var colors = new NativeArray<int>(inputTriangles.Length / 3, Allocator.Temp);
                using var halfedges = new NativeArray<int>(inputTriangles.Length, Allocator.Temp);

                GenerateHalfedges(halfedges, inputTriangles, Allocator.Temp);
                GenerateInputPositions(positions, inputPositions.Reinterpret<float3>(), out var min, out var max);
                GenerateTriangleColors(colors, halfedges, out var colorsCount, Allocator.Temp);

                if (generateInitialUVPlanarMap)
                {
                    GenerateUVsWithPlanarMap(inputUVs.Reinterpret<float2>(), positions, min, max);
                }

                var tmpStatus = default(NativeReference<Status>);
                status = status.IsCreated ? status : tmpStatus = new(Allocator.Temp);

                outputPositions.Clear();
                outputTriangles.Clear();
                if (outputUVs.IsCreated) outputUVs.Clear();
                status.Value = Status.OK;

                for (int i = 0; i < colorsCount; i++)
                {
                    ProcessSubMesh(color: i,
                        positions.AsReadOnly(), inputTriangles, colors, inputUVs.Reinterpret<float2>(),
                        min, max
                    );
                    if (status.Value != Status.OK)
                    {
                        break;
                    }
                }

                if (colorsCount == 0)
                {
                    if (args.Verbose)
                    {
                        Debug.LogWarning(
                            "[Triangulator]: colorsCount = 0 during retriangulation. " +
                            "The input mesh is empty or the triangle indices do not match the input vertices."
                        );
                    }

                    GenerateOutputPositions(inputPositions.Reinterpret<float3>(), positions);
                    outputPositions.AddRange(inputPositions.Reinterpret<float3>());
                    outputTriangles.AddRange(inputTriangles);
                    if (outputUVs.IsCreated) outputUVs.AddRange(inputUVs.Reinterpret<float2>());
                }

                if (tmpStatus.IsCreated) tmpStatus.Dispose();
            }

            private readonly void GenerateInputPositions(Span<double2> positions, ReadOnlySpan<float3> inputPositions, out double2 min, out double2 max)
            {
                min = double.MaxValue;
                max = double.MinValue;

                for (int i = 0; i < inputPositions.Length; i++)
                {
                    var q = (double3)inputPositions[i];
                    var p = axisInput switch
                    {
                        Axis.XY => q.xy,
                        Axis.XZ => q.xz,
                        Axis.YX => q.yx,
                        Axis.YZ => q.yz,
                        Axis.ZX => q.zx,
                        Axis.ZY => q.zy,
                        _ => throw new NotImplementedException()
                    };

                    min = math.min(min, p);
                    max = math.max(max, p);
                    positions[i] = p;
                }
            }

            private readonly void GenerateOutputPositions(Span<float3> outputPositions, ReadOnlySpan<double2> inputPositions)
            {
                for (int i = 0; i < inputPositions.Length; i++)
                {
                    var q = math.float3((float2)inputPositions[i], 0);
                    var p = axisOutput switch
                    {
                        Axis.XY => q.xyz,
                        Axis.XZ => q.xzy,
                        Axis.YX => q.yxz,
                        Axis.YZ => q.zxy,
                        Axis.ZX => q.yzx,
                        Axis.ZY => q.zyx,
                        _ => throw new NotImplementedException()
                    };

                    outputPositions[i] = p;
                }
            }

            private static void UnpackSubMesh(int color, Span<int> map, NativeList<double2> subpositions, NativeList<int> subtriangles, NativeList<float2> subuvs, ReadOnlySpan<double2> positions, ReadOnlySpan<int> triangles, ReadOnlySpan<int> colors, ReadOnlySpan<float2> uvs)
            {
                map.Fill(-1);

                using var visited = new NativeArray<bool>(positions.Length, Allocator.Temp);
                for (int i = 0; i < triangles.Length / 3; i++)
                {
                    if (colors[i] == color)
                    {
                        var (t0, t1, t2) = (triangles[3 * i + 0], triangles[3 * i + 1], triangles[3 * i + 2]);
                        TryVisitPoint(t0, positions, uvs, visited, map);
                        TryVisitPoint(t1, positions, uvs, visited, map);
                        TryVisitPoint(t2, positions, uvs, visited, map);

                        void TryVisitPoint(int p, ReadOnlySpan<double2> positions, ReadOnlySpan<float2> uvs, Span<bool> visited, Span<int> map)
                        {
                            if (!visited[p])
                            {
                                visited[p] = true;
                                map[p] = subpositions.Length;
                                subpositions.Add(positions[p]);
                                subuvs.Add(uvs[p]);
                            }
                            subtriangles.Add(map[p]);
                        }
                    }
                }
            }

            private static NativeArray<int> GenerateConstraints(ReadOnlySpan<int> triangles, Allocator allocator)
            {
                using var halfedges = new NativeArray<int>(triangles.Length, allocator);
                GenerateHalfedges(halfedges, triangles, allocator);

                using var constraints = new NativeList<int>(allocator);
                for (int i = 0; i < halfedges.Length; i++)
                {
                    var he = halfedges[i];
                    if (he == -1)
                    {
                        var e0 = triangles[i];
                        var e1 = triangles[NextHalfedge(i)];
                        constraints.Add(e0);
                        constraints.Add(e1);
                    }
                }
                return constraints.ToArray(allocator);
            }

            private static NativeArray<int> GenerateConstraintsAndSplitEdges(NativeList<double2> subpositions, NativeList<float2> subuvs, ReadOnlySpan<int> triangles, Allocator allocator)
            {
                using var halfedges = new NativeArray<int>(triangles.Length, allocator);
                GenerateHalfedges(halfedges, triangles, allocator);

                using var constraints = new NativeList<int>(allocator);
                for (int i = 0; i < halfedges.Length; i++)
                {
                    var he = halfedges[i];
                    var e0 = triangles[i];
                    var e1 = triangles[NextHalfedge(i)];

                    if (e0 < e1 || he == -1)
                    {

                        var ei = subpositions.Length;
                        subpositions.Add(0.5 * (subpositions[e0] + subpositions[e1]));
                        subuvs.Add(0.5f * (subuvs[e0] + subuvs[e1]));

                        if (he == -1)
                        {
                            constraints.Add(e0);
                            constraints.Add(ei);
                            constraints.Add(ei);
                            constraints.Add(e1);
                        }
                    }
                }
                return constraints.ToArray(allocator);
            }

            private readonly void GenerateUVs(NativeList<float2> subuvs, double2 min, double2 max,
                ReadOnlySpan<double2> subpositions, ReadOnlySpan<int> subtriangles, ReadOnlySpan<double2> outputPositions)
            {
                subuvs.Length = outputPositions.Length;

                var uvs = subuvs.AsArray().AsSpan()[subpositions.Length..];
                var positions = outputPositions[subpositions.Length..];
                var inputUVs = subuvs.AsArray().AsReadOnlySpan()[..subpositions.Length];

                switch (uvMap)
                {
                    case UVMap.None:
                        break;

                    case UVMap.Planar:
                        GenerateUVsWithPlanarMap(uvs, positions, min, max);
                        break;

                    case UVMap.Barycentric:
                        GenerateUVsWithBarycentricMap(uvs, positions, subtriangles, inputUVs, subpositions);
                        break;

                    default:
                        throw new NotImplementedException();
                }
            }

            private static void GenerateUVsWithPlanarMap(Span<float2> uvs, ReadOnlySpan<double2> positions, double2 min, double2 max)
            {
                var range = max - min;
                for (int i = 0; i < uvs.Length; i++)
                {
                    var uv = (positions[i] - min) / range;
                    uvs[i] = (float2)uv;
                }
            }

            private static void GenerateUVsWithBarycentricMap(Span<float2> uvs, ReadOnlySpan<double2> positions, ReadOnlySpan<int> inputTriangles, ReadOnlySpan<float2> inputUVs, ReadOnlySpan<double2> inputPositions)
            {
                for (int i = 0; i < uvs.Length; i++)
                {
                    var p = positions[i];
                    // NOTE: this can be optimized from O(n m) to O(n log m). Use tree/grid/...
                    for (int j = 0; j < inputTriangles.Length / 3; j++)
                    {
                        var (t0, t1, t2) = (inputTriangles[3 * j + 0], inputTriangles[3 * j + 1], inputTriangles[3 * j + 2]);
                        var (q0, q1, q2) = (inputPositions[t0], inputPositions[t1], inputPositions[t2]);

                        var bar = barycoords(p, q0, q1, q2);
                        if (math.all(-math.EPSILON <= bar & bar <= 1 + math.EPSILON))
                        {
                            var b = (float3)bar;
                            uvs[i] = b.x * inputUVs[t0] + b.y * inputUVs[t1] + b.z * inputUVs[t2];
                            break;
                        }

                        static double3 barycoords(double2 p, double2 a, double2 b, double2 c)
                        {
                            static double cross(double2 x, double2 y) => x.x * y.y - x.y * y.x;
                            var (v0, v1, v2) = (b - a, c - a, p - a);
                            var v = cross(v2, v1) / cross(v0, v1);
                            var w = cross(v0, v2) / cross(v0, v1);
                            var u = 1 - v - w;
                            return math.double3(u, v, w);
                        }
                    }
                }
            }

            private readonly void InsertTriangleMidPoints(NativeList<double2> subpositions, NativeList<float2> subuvs, ReadOnlySpan<int> subtriangles)
            {
                for (int t = 0; t < subtriangles.Length / 3; t++)
                {
                    var (t0, t1, t2) = (subtriangles[3 * t + 0], subtriangles[3 * t + 1], subtriangles[3 * t + 2]);
                    var (p0, p1, p2) = (subpositions[t0], subpositions[t1], subpositions[t2]);
                    var p = (p0 + p1 + p2) / 3;
                    subpositions.Add(p);

                    var (uv0, uv1, uv2) = (subuvs[t0], subuvs[t1], subuvs[t2]);
                    var uv = (uv0 + uv1 + uv2) / 3;
                    subuvs.Add(uv);
                }
            }

            private void ProcessSubMesh(
                int color,
                ReadOnlySpan<double2> positions, ReadOnlySpan<int> triangles, ReadOnlySpan<int> colors, ReadOnlySpan<float2> uvs,
                double2 min, double2 max
            )
            {
                using var subtriangles = new NativeList<int>(Allocator.Temp);
                using var subpositions = new NativeList<double2>(Allocator.Temp);
                using var subuvs = new NativeList<float2>(Allocator.Temp);
                using var map = new NativeArray<int>(positions.Length, Allocator.Temp);

                UnpackSubMesh(color, map, subpositions, subtriangles, subuvs, positions, triangles, colors, uvs);

                using var subconstraints = insertEdgeMidPoints ?
                    GenerateConstraintsAndSplitEdges(subpositions, subuvs, subtriangles.AsReadOnly(), Allocator.Temp) :
                    GenerateConstraints(subtriangles.AsReadOnly(), Allocator.Temp);

                if (insertTriangleMidPoints)
                {
                    InsertTriangleMidPoints(subpositions, subuvs, subtriangles.AsReadOnly());
                }

                using var tmpPositionsT2 = new NativeList<double2>(Allocator.Temp);
                using var tmpTriangles = new NativeList<int>(Allocator.Temp);
                // NOTE: This ToArray allocation is required due to Unity.Collections bug. See issue #333 for more details.
                using var p = subpositions.ToArray(Allocator.Temp);
                new UnsafeTriangulator<double2>().Triangulate(
                    input: new() { Positions = p, ConstraintEdges = subconstraints },
                    output: new() { Positions = tmpPositionsT2, Triangles = tmpTriangles, Status = status },
                    args, Allocator.Temp
                );

                if (status.Value != Status.OK)
                {
                    return;
                }

                using var tmpPositions = new NativeArray<float3>(tmpPositionsT2.Length, Allocator.Temp);
                GenerateOutputPositions(tmpPositions, tmpPositionsT2.AsReadOnly());

                InsertSubMesh(
                    positions: outputPositions,
                    triangles: outputTriangles,
                    subpositions: tmpPositions,
                    subtriangles: tmpTriangles.AsReadOnly()
                );

                if (outputUVs.IsCreated)
                {
                    GenerateUVs(subuvs, min, max, subpositions.AsReadOnly(), subtriangles.AsReadOnly(), tmpPositionsT2.AsReadOnly());
                    outputUVs.AddRange(subuvs.AsArray());
                }
            }
        }

        [System.Diagnostics.Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ThrowCheckGenerateHalfedges(ReadOnlySpan<int> halfedges, ReadOnlySpan<int> triangles)
        {
            if (halfedges.Length != triangles.Length)
            {
                throw new ArgumentException(
                    $"The provided halfedges[{halfedges.Length}] does not match the length of triangles[{triangles.Length}]."
                );
            }
        }

        [System.Diagnostics.Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ThrowCheckGenerateTriangleColors(ReadOnlySpan<int> colors, ReadOnlySpan<int> halfedges)
        {
            if (3 * colors.Length != halfedges.Length)
            {
                throw new ArgumentException(
                    $"The provided colors[{colors.Length}] must be one-third of the length of halfedges [{halfedges.Length}]."
                );
            }
        }
    }
}

namespace andywiecko.BurstTriangulator.LowLevel.Unsafe
{
    /// <summary>
    /// <b>Obsolete:</b> use <see cref="NativeInputData{T2}"/> instead.
    /// </summary>
    /// <exclude />
    [Obsolete("Use " + nameof(NativeInputData<T2>) + "<> instead.")]
    public struct InputData<T2> where T2 : unmanaged
    {
        public NativeArray<T2> Positions;
        public NativeArray<int> ConstraintEdges;
        public NativeArray<T2> HoleSeeds;
        public NativeArray<bool> IgnoreConstraintForPlantingSeeds;
        public static implicit operator NativeInputData<T2>(InputData<T2> @this) => new()
        {
            Positions = @this.Positions,
            ConstraintEdges = @this.ConstraintEdges,
            HoleSeeds = @this.HoleSeeds,
            IgnoreConstraintForPlantingSeeds = @this.IgnoreConstraintForPlantingSeeds,
        };
    }

    /// <summary>
    /// <b>Obsolete:</b> use <see cref="NativeOutputData{T2}"/> instead.
    /// </summary>
    /// <exclude />
    [Obsolete("Use " + nameof(NativeOutputData<T2>) + "<> instead.")]
    public struct OutputData<T2> where T2 : unmanaged
    {
        public NativeList<T2> Positions;
        public NativeList<int> Triangles;
        public NativeReference<Status> Status;
        public NativeList<int> Halfedges;
        public NativeList<bool> ConstrainedHalfedges;
        public NativeList<bool> IgnoredHalfedgesForPlantingSeeds;
        public static implicit operator NativeOutputData<T2>(OutputData<T2> @this) => new()
        {
            Positions = @this.Positions,
            Triangles = @this.Triangles,
            Status = @this.Status,
            Halfedges = @this.Halfedges,
            ConstrainedHalfedges = @this.ConstrainedHalfedges,
            IgnoredHalfedgesForPlantingSeeds = @this.IgnoredHalfedgesForPlantingSeeds,
        };
    }

    /// <summary>
    /// Native correspondence to <see cref="BurstTriangulator.InputData{T2}"/>.
    /// </summary>
    /// <seealso cref="BurstTriangulator.InputData{T2}"/>
    public struct NativeInputData<T2> where T2 : unmanaged
    {
        /// <summary>
        /// Positions of points used in triangulation.
        /// </summary>
        public NativeArray<T2> Positions;
        /// <summary>
        /// Optional buffer for constraint edges. This array constrains specific edges to be included in the final 
        /// triangulation result. It should contain indexes corresponding to the <see cref="Positions"/> of the edges 
        /// in the format [a₀, a₁, b₀, b₁, c₀, c₁, ...], where (a₀, a₁), (b₀, b₁), (c₀, c₁), etc., represent the constraint edges.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b> If refinement is enabled, the provided constraints may be split during the refinement process.
        /// </remarks>
        public NativeArray<int> ConstraintEdges;
        /// <summary>
        /// Optional buffer containing seeds for holes. These hole seeds serve as starting points for a removal process that 
        /// mimics the spread of a virus. During this process, <see cref="ConstraintEdges"/> act as barriers to prevent further propagation.
        /// For more information, refer to the documentation.
        /// </summary>
        public NativeArray<T2> HoleSeeds;
        /// <summary>
        /// Optional buffer used to mark constraints that should be ignored during the seed planting step.
        /// The buffer length should be half the length of <see cref="ConstraintEdges"/>.
        /// </summary>
        public NativeArray<bool> IgnoreConstraintForPlantingSeeds;
    }

    /// <summary>
    /// Native correspondence to <see cref="BurstTriangulator.OutputData{T2}"/>.
    /// </summary>
    /// <seealso cref="BurstTriangulator.OutputData{T2}"/>
    public struct NativeOutputData<T2> where T2 : unmanaged
    {
        /// <summary>
        /// Positions of triangulation points.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b> This buffer may include additional points than <see cref="NativeInputData{T2}.Positions"/> if refinement is enabled. 
        /// Additionally, the positions might differ slightly (by a small ε) if a <see cref="Args.Preprocessor"/> is applied.
        /// </remarks>
        public NativeList<T2> Positions;
        /// <summary>
        /// Continuous buffer of resulting triangles. All triangles are guaranteed to be oriented clockwise.
        /// </summary>
        public NativeList<int> Triangles;
        /// <summary>
        /// Status of the triangulation. Retrieve this value to detect any errors that occurred during triangulation.
        /// </summary>
        public NativeReference<Status> Status;
        /// <summary>
        /// Continuous buffer of resulting halfedges. A value of -1 indicates that there is no corresponding opposite halfedge.
        /// For more information, refer to the documentation on halfedges.
        /// </summary>
        public NativeList<int> Halfedges;
        /// <summary>
        /// Buffer corresponding to <see cref="Halfedges"/>. <see langword="true"/> indicates that the halfedge is constrained, <see langword="false"/> otherwise.
        /// </summary>
        /// <seealso cref="IgnoredHalfedgesForPlantingSeeds"/>
        public NativeList<bool> ConstrainedHalfedges;
        /// <summary>
        /// Buffer corresponding to <see cref="Halfedges"/>. <see langword="true"/> indicates that the halfedge was ignored during planting seed step, <see langword="false"/> otherwise. Constraint edges to ignore can be set in input using <see cref="NativeInputData{T2}.IgnoreConstraintForPlantingSeeds"/>.
        /// </summary>
        /// <seealso cref="ConstrainedHalfedges"/>
        public NativeList<bool> IgnoredHalfedgesForPlantingSeeds;
    }

    /// <summary>
    /// Native correspondence to <see cref="TriangulationSettings"/>.
    /// </summary>
    /// <seealso cref="TriangulationSettings"/>
    public readonly struct Args
    {
        public readonly Preprocessor Preprocessor;
        public readonly int SloanMaxIters;
        // NOTE: Only blittable types are supported for Burst compiled static methods.
        //       Unfortunately bool type is non-blittable and required marshaling for compilation.
        //       Learn more about blittable here: https://learn.microsoft.com/en-us/dotnet/framework/interop/blittable-and-non-blittable-types
        [MarshalAs(UnmanagedType.U1)]
        public readonly bool AutoHolesAndBoundary, RefineMesh, RestoreBoundary, ValidateInput, Verbose;
        public readonly float ConcentricShellsParameter, RefinementThresholdAngle, RefinementThresholdArea;

        /// <summary>
        /// Constructs a new <see cref="Args"/>.
        /// </summary>
        /// <remarks>
        /// Use <see cref="Default"/> and <see cref="With"/> for easy construction.
        /// </remarks>
        public Args(
            Preprocessor preprocessor,
            int sloanMaxIters,
            bool autoHolesAndBoundary, bool refineMesh, bool restoreBoundary, bool validateInput, bool verbose,
            float concentricShellsParameter, float refinementThresholdAngle, float refinementThresholdArea
        )
        {
            AutoHolesAndBoundary = autoHolesAndBoundary;
            ConcentricShellsParameter = concentricShellsParameter;
            Preprocessor = preprocessor;
            RefineMesh = refineMesh;
            RestoreBoundary = restoreBoundary;
            SloanMaxIters = sloanMaxIters;
            ValidateInput = validateInput;
            Verbose = verbose;
            RefinementThresholdAngle = refinementThresholdAngle;
            RefinementThresholdArea = refinementThresholdArea;
        }

        /// <summary>
        /// Construct <see cref="Args"/> with default values (same as <see cref="TriangulationSettings"/> defaults).
        /// </summary>
        public static Args Default(
            Preprocessor preprocessor = Preprocessor.None,
            int sloanMaxIters = 1_000_000,
            bool autoHolesAndBoundary = false, bool refineMesh = false, bool restoreBoundary = false, bool validateInput = true, bool verbose = true,
            float concentricShellsParameter = 0.001f, float refinementThresholdAngle = 0.0872664626f, float refinementThresholdArea = 1f
        ) => new(
            preprocessor,
            sloanMaxIters,
            autoHolesAndBoundary, refineMesh, restoreBoundary, validateInput, verbose,
            concentricShellsParameter, refinementThresholdAngle, refinementThresholdArea
        );

        public static implicit operator Args(TriangulationSettings settings) => new(
            autoHolesAndBoundary: settings.AutoHolesAndBoundary,
            concentricShellsParameter: settings.ConcentricShellsParameter,
            preprocessor: settings.Preprocessor,
            refineMesh: settings.RefineMesh,
            restoreBoundary: settings.RestoreBoundary,
            sloanMaxIters: settings.SloanMaxIters,
            validateInput: settings.ValidateInput,
            verbose: settings.Verbose,
            refinementThresholdAngle: settings.RefinementThresholds.Angle,
            refinementThresholdArea: settings.RefinementThresholds.Area
        );

        /// <summary>
        /// Returns a new <see cref="Args"/> but with changed selected parameter(s) values.
        /// </summary>
        public Args With(
            Preprocessor? preprocessor = null,
            int? sloanMaxIters = null,
            bool? autoHolesAndBoundary = null, bool? refineMesh = null, bool? restoreBoundary = null, bool? validateInput = null, bool? verbose = null,
            float? concentricShellsParameter = null, float? refinementThresholdAngle = null, float? refinementThresholdArea = null
        ) => new(
            preprocessor ?? Preprocessor,
            sloanMaxIters ?? SloanMaxIters,
            autoHolesAndBoundary ?? AutoHolesAndBoundary, refineMesh ?? RefineMesh, restoreBoundary ?? RestoreBoundary, validateInput ?? ValidateInput, verbose ?? Verbose,
            concentricShellsParameter ?? ConcentricShellsParameter, refinementThresholdAngle ?? RefinementThresholdAngle, refinementThresholdArea ?? RefinementThresholdArea
        );
    }

    /// <summary>
    /// A wrapper for <see cref="UnsafeTriangulator{T2}"/> where T2 is <see cref="double2"/>.
    /// </summary>
    /// <seealso cref="UnsafeTriangulator{T2}"/>
    /// <seealso cref="Extensions"/>
    public readonly struct UnsafeTriangulator { }

    /// <summary>
    /// A readonly struct that corresponds to <see cref="Triangulator{T2}"/>.
    /// This struct can be used directly in a native context within the jobs pipeline.
    /// The API is accessible through <see cref="Extensions"/>.
    /// </summary>
    /// <remarks>
    /// <i>Unsafe</i> in this context indicates that using the method may be challenging for beginner users.
    /// The user is responsible for managing data allocation (both input and output).
    /// Some permutations of the method calls may not be supported.
    /// Refer to the documentation for more details. The term <i>unsafe</i> does <b>not</b> refer to memory safety.
    /// </remarks>
    /// <typeparam name="T2">The coordinate type. Supported types include:
    /// <see cref="float2"/>,
    /// <see cref="Vector2"/>,
    /// <see cref="double2"/>,
    /// <see cref="fp2"/>,
    /// and
    /// <see cref="int2"/>.
    /// For more information on type restrictions, refer to the documentation.
    /// </typeparam>
    /// <seealso cref="Extensions"/>
    public readonly struct UnsafeTriangulator<T2> where T2 : unmanaged { }

    /// <summary>
    /// Provides extension methods.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Performs triangulation on the given <paramref name="input"/>, producing the result in <paramref name="output"/> based on the settings specified in <paramref name="args"/>.
        /// This method corresponds to the native implementation of <see cref="Triangulator.Run"/>.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// The <paramref name="input"/> and <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void Triangulate(this UnsafeTriangulator @this, NativeInputData<double2> input, NativeOutputData<double2> output, Args args, Allocator allocator) => new UnsafeTriangulator<double2>().Triangulate(input, output, args, allocator);
        /// <summary>
        /// Constrains the edge formed by the point indices (<paramref name="pi"/>, <paramref name="pj"/>)
        /// using the provided <paramref name="output"/> triangulation data.
        /// </summary>
        /// <remarks>
        /// <para><b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </para>
        /// <para><b>Limitations:</b>
        /// This method is designed to work only on a <b>bulk</b> mesh and does not support constraining edges that pass through holes in the triangulation.
        /// </para>
        /// </remarks>
        /// <param name="pi">The index of the first point of the edge to constrain.</param>
        /// <param name="pj">The index of the second point of the edge to constrain.</param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        /// <param name="ignoreForPlantingSeeds">
        /// If <see langword="true"/>, the halfedges corresponding to (<paramref name="pi"/>, <paramref name="pj"/>) are ignored during the seed planting step.
        /// </param>
        public static void ConstrainEdge(this UnsafeTriangulator @this, NativeOutputData<double2> output, int pi, int pj, Args args, Allocator allocator, bool ignoreForPlantingSeeds = false) => new UnsafeTriangulator<double2>().ConstrainEdge(output, pi, pj, args, allocator, ignoreForPlantingSeeds);
        /// <summary>
        /// Plants hole seeds defined in <paramref name="input"/> (or restores boundaries or auto-holes if specified in <paramref name="args"/>)
        /// within the triangulation data in <paramref name="output"/>, using the settings specified in <paramref name="args"/>.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="input"/> and <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void PlantHoleSeeds(this UnsafeTriangulator @this, NativeInputData<double2> input, NativeOutputData<double2> output, Args args, Allocator allocator) => new UnsafeTriangulator<double2>().PlantHoleSeeds(input, output, args, allocator);
        /// <summary>
        /// Refines the mesh for a valid triangulation in <paramref name="output"/>.
        /// Refinement parameters can be provided with the selected precision type T in generics, which is especially useful for fixed-point arithmetic.
        /// Refinement parameters in <see cref="Args"/> are restricted to <see cref="float"/> precision.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        /// <param name="angleThreshold">Expressed in <em>radians</em>. Default: 5° = 0.0872664626 rad.</param>
        /// <param name="constrainBoundary">Used to constrain boundary halfedges. Since the refinement algorithm (whether for constrained triangulation or not) requires constrained halfedges at the boundary, not setting this option may cause unexpected behavior, especially when the restoreBoundary option is disabled.</param>
        public static void RefineMesh(this UnsafeTriangulator @this, NativeOutputData<double2> output, Allocator allocator, double areaThreshold = 1, double angleThreshold = 0.0872664626, double concentricShells = 0.001, bool constrainBoundary = false) => new UnsafeTriangulator<double2>().RefineMesh(output, allocator, areaThreshold, angleThreshold, concentricShells, constrainBoundary);
        /// <summary>
        /// Inserts a point into the given triangulation <paramref name="output"/> within the triangle at index <paramref name="tId"/>, using the specified barycentric coordinates <paramref name="bar"/>.
        /// For faster triangle lookup when inserting a point at specific coordinates, it is recommended to use an acceleration structure (e.g., bounding volume tree, buckets, etc.).
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// Only point insertion within the mesh bulk is supported.
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="tId">The index of the triangle in which the point should be inserted.</param>
        /// <param name="bar">
        /// The barycentric coordinates of the point to insert inside the triangle <paramref name="tId"/>. 
        /// All coordinates should be in the range (0, 1), and <paramref name="bar"/>.x + <paramref name="bar"/>.y + <paramref name="bar"/>.z must equal 1.
        /// </param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void DynamicInsertPoint(this UnsafeTriangulator @this, NativeOutputData<double2> output, int tId, double3 bar, Allocator allocator) => new UnsafeTriangulator<double2>().DynamicInsertPoint(output, tId, bar, allocator);
        /// <summary>
        /// Splits the halfedge specified by <paramref name="he"/> by inserting a point at a position determined by linear interpolation.
        /// The position is interpolated between the start and end points of the halfedge in the triangulation <paramref name="output"/> 
        /// using <paramref name="alpha"/> as the interpolation parameter.
        /// This method preserves the "constrained" state of the halfedge, meaning that if the specified halfedge is constrained, 
        /// the two resulting sub-segments will also be marked as constrained.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="he">The index of the halfedge to split.</param>
        /// <param name="alpha">
        /// The interpolation parameter for positioning the new point between the start and end points of the halfedge, 
        /// where <c>p = (1 - alpha) * start + alpha * end</c>.
        /// </param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void DynamicSplitHalfedge(this UnsafeTriangulator @this, NativeOutputData<double2> output, int he, double alpha, Allocator allocator) => new UnsafeTriangulator<double2>().DynamicSplitHalfedge(output, he, alpha, allocator);
        /// <summary>
        /// Removes the specified point <paramref name="pId"/> from the <paramref name="output"/> data
        /// and re-triangulates the affected region to maintain a valid triangulation.
        /// This method supports only the removal of <b>bulk</b> points, i.e., points that are not located on the triangulation boundary.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="pId">The index of the <b>bulk</b> point to remove.</param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void DynamicRemoveBulkPoint(this UnsafeTriangulator @this, NativeOutputData<double2> output, int pId, Allocator allocator) => new UnsafeTriangulator<double2>().DynamicRemoveBulkPoint(output, pId, allocator);

        /// <summary>
        /// Performs triangulation on the given <paramref name="input"/>, producing the result in <paramref name="output"/> based on the settings specified in <paramref name="args"/>.
        /// This method corresponds to the native implementation of <see cref="Triangulator.Run"/>.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// The <paramref name="input"/> and <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void Triangulate(this UnsafeTriangulator<float2> @this, NativeInputData<float2> input, NativeOutputData<float2> output, Args args, Allocator allocator) => new UnsafeTriangulator<float, float2, float, TransformFloat, UtilsFloat>().Triangulate(input, output, args, allocator);
        /// <summary>
        /// Constrains the edge formed by the point indices (<paramref name="pi"/>, <paramref name="pj"/>)
        /// using the provided <paramref name="output"/> triangulation data.
        /// </summary>
        /// <remarks>
        /// <para><b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </para>
        /// <para><b>Limitations:</b>
        /// This method is designed to work only on a <b>bulk</b> mesh and does not support constraining edges that pass through holes in the triangulation.
        /// </para>
        /// </remarks>
        /// <param name="pi">The index of the first point of the edge to constrain.</param>
        /// <param name="pj">The index of the second point of the edge to constrain.</param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        /// <param name="ignoreForPlantingSeeds">
        /// If <see langword="true"/>, the halfedges corresponding to (<paramref name="pi"/>, <paramref name="pj"/>) are ignored during the seed planting step.
        /// </param>
        public static void ConstrainEdge(this UnsafeTriangulator<float2> @this, NativeOutputData<float2> output, int pi, int pj, Args args, Allocator allocator, bool ignoreForPlantingSeeds = false) => new UnsafeTriangulator<float, float2, float, TransformFloat, UtilsFloat>().ConstrainEdge(output, pi, pj, args, allocator, ignoreForPlantingSeeds);
        /// <summary>
        /// Plants hole seeds defined in <paramref name="input"/> (or restores boundaries or auto-holes if specified in <paramref name="args"/>)
        /// within the triangulation data in <paramref name="output"/>, using the settings specified in <paramref name="args"/>.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="input"/> and <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void PlantHoleSeeds(this UnsafeTriangulator<float2> @this, NativeInputData<float2> input, NativeOutputData<float2> output, Args args, Allocator allocator) => new UnsafeTriangulator<float, float2, float, TransformFloat, UtilsFloat>().PlantHoleSeeds(input, output, args, allocator);
        /// <summary>
        /// Refines the mesh for a valid triangulation in <paramref name="output"/>.
        /// Refinement parameters can be provided with the selected precision type T in generics, which is especially useful for fixed-point arithmetic.
        /// Refinement parameters in <see cref="Args"/> are restricted to <see cref="float"/> precision.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        /// <param name="angleThreshold">Expressed in <em>radians</em>. Default: 5° = 0.0872664626 rad.</param>
        /// <param name="constrainBoundary">Used to constrain boundary halfedges. Since the refinement algorithm (whether for constrained triangulation or not) requires constrained halfedges at the boundary, not setting this option may cause unexpected behavior, especially when the restoreBoundary option is disabled.</param>
        public static void RefineMesh(this UnsafeTriangulator<float2> @this, NativeOutputData<float2> output, Allocator allocator, float areaThreshold = 1, float angleThreshold = 0.0872664626f, float concentricShells = 0.001f, bool constrainBoundary = false) => new UnsafeTriangulator<float, float2, float, TransformFloat, UtilsFloat>().RefineMesh(output, allocator, 2 * areaThreshold, angleThreshold, concentricShells, constrainBoundary);
        /// <summary>
        /// Inserts a point into the given triangulation <paramref name="output"/> within the triangle at index <paramref name="tId"/>, using the specified barycentric coordinates <paramref name="bar"/>.
        /// For faster triangle lookup when inserting a point at specific coordinates, it is recommended to use an acceleration structure (e.g., bounding volume tree, buckets, etc.).
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// Only point insertion within the mesh bulk is supported.
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="tId">The index of the triangle in which the point should be inserted.</param>
        /// <param name="bar">
        /// The barycentric coordinates of the point to insert inside the triangle <paramref name="tId"/>. 
        /// All coordinates should be in the range (0, 1), and <paramref name="bar"/>.x + <paramref name="bar"/>.y + <paramref name="bar"/>.z must equal 1.
        /// </param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void DynamicInsertPoint(this UnsafeTriangulator<float2> @this, NativeOutputData<float2> output, int tId, float3 bar, Allocator allocator)
        {
            var (t0, t1, t2) = (output.Triangles[3 * tId + 0], output.Triangles[3 * tId + 1], output.Triangles[3 * tId + 2]);
            var (p0, p1, p2) = (output.Positions[t0], output.Positions[t1], output.Positions[t2]);
            var p = bar.x * p0 + bar.y * p1 + bar.z * p2;
            new UnsafeTriangulator<float, float2, float, TransformFloat, UtilsFloat>().DynamicInsertPoint(output, tId, p, allocator);
        }
        /// <summary>
        /// Splits the halfedge specified by <paramref name="he"/> by inserting a point at a position determined by linear interpolation.
        /// The position is interpolated between the start and end points of the halfedge in the triangulation <paramref name="output"/> 
        /// using <paramref name="alpha"/> as the interpolation parameter.
        /// This method preserves the "constrained" state of the halfedge, meaning that if the specified halfedge is constrained, 
        /// the two resulting sub-segments will also be marked as constrained.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="he">The index of the halfedge to split.</param>
        /// <param name="alpha">
        /// The interpolation parameter for positioning the new point between the start and end points of the halfedge, 
        /// where <c>p = (1 - alpha) * start + alpha * end</c>.
        /// </param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void DynamicSplitHalfedge(this UnsafeTriangulator<float2> @this, NativeOutputData<float2> output, int he, float alpha, Allocator allocator) => new UnsafeTriangulator<float, float2, float, TransformFloat, UtilsFloat>().DynamicSplitHalfedge(output, he, alpha, allocator);
        /// <summary>
        /// Removes the specified point <paramref name="pId"/> from the <paramref name="output"/> data
        /// and re-triangulates the affected region to maintain a valid triangulation.
        /// This method supports only the removal of <b>bulk</b> points, i.e., points that are not located on the triangulation boundary.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="pId">The index of the <b>bulk</b> point to remove.</param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void DynamicRemoveBulkPoint(this UnsafeTriangulator<float2> @this, NativeOutputData<float2> output, int pId, Allocator allocator) => new UnsafeTriangulator<float, float2, float, TransformFloat, UtilsFloat>().DynamicRemoveBulkPoint(output, pId, allocator);

        /// <summary>
        /// Performs triangulation on the given <paramref name="input"/>, producing the result in <paramref name="output"/> based on the settings specified in <paramref name="args"/>.
        /// This method corresponds to the native implementation of <see cref="Triangulator.Run"/>.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// The <paramref name="input"/> and <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void Triangulate(this UnsafeTriangulator<Vector2> @this, NativeInputData<Vector2> input, NativeOutputData<Vector2> output, Args args, Allocator allocator) => new UnsafeTriangulator<float2>().Triangulate(UnsafeUtility.As<NativeInputData<Vector2>, NativeInputData<float2>>(ref input), UnsafeUtility.As<NativeOutputData<Vector2>, NativeOutputData<float2>>(ref output), args, allocator);
        /// <summary>
        /// Constrains the edge formed by the point indices (<paramref name="pi"/>, <paramref name="pj"/>)
        /// using the provided <paramref name="output"/> triangulation data.
        /// </summary>
        /// <remarks>
        /// <para><b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </para>
        /// <para><b>Limitations:</b>
        /// This method is designed to work only on a <b>bulk</b> mesh and does not support constraining edges that pass through holes in the triangulation.
        /// </para>
        /// </remarks>
        /// <param name="pi">The index of the first point of the edge to constrain.</param>
        /// <param name="pj">The index of the second point of the edge to constrain.</param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        /// <param name="ignoreForPlantingSeeds">
        /// If <see langword="true"/>, the halfedges corresponding to (<paramref name="pi"/>, <paramref name="pj"/>) are ignored during the seed planting step.
        /// </param>
        public static void ConstrainEdge(this UnsafeTriangulator<Vector2> @this, NativeOutputData<Vector2> output, int pi, int pj, Args args, Allocator allocator, bool ignoreForPlantingSeeds = false) => new UnsafeTriangulator<float2>().ConstrainEdge(UnsafeUtility.As<NativeOutputData<Vector2>, NativeOutputData<float2>>(ref output), pi, pj, args, allocator, ignoreForPlantingSeeds);
        /// <summary>
        /// Plants hole seeds defined in <paramref name="input"/> (or restores boundaries or auto-holes if specified in <paramref name="args"/>)
        /// within the triangulation data in <paramref name="output"/>, using the settings specified in <paramref name="args"/>.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="input"/> and <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void PlantHoleSeeds(this UnsafeTriangulator<Vector2> @this, NativeInputData<Vector2> input, NativeOutputData<Vector2> output, Args args, Allocator allocator) => new UnsafeTriangulator<float2>().PlantHoleSeeds(UnsafeUtility.As<NativeInputData<Vector2>, NativeInputData<float2>>(ref input), UnsafeUtility.As<NativeOutputData<Vector2>, NativeOutputData<float2>>(ref output), args, allocator);
        /// <summary>
        /// Refines the mesh for a valid triangulation in <paramref name="output"/>.
        /// Refinement parameters can be provided with the selected precision type T in generics, which is especially useful for fixed-point arithmetic.
        /// Refinement parameters in <see cref="Args"/> are restricted to <see cref="float"/> precision.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        /// <param name="angleThreshold">Expressed in <em>radians</em>. Default: 5° = 0.0872664626 rad.</param>
        /// <param name="constrainBoundary">Used to constrain boundary halfedges. Since the refinement algorithm (whether for constrained triangulation or not) requires constrained halfedges at the boundary, not setting this option may cause unexpected behavior, especially when the restoreBoundary option is disabled.</param>
        public static void RefineMesh(this UnsafeTriangulator<Vector2> @this, NativeOutputData<Vector2> output, Allocator allocator, float areaThreshold = 1, float angleThreshold = 0.0872664626f, float concentricShells = 0.001f, bool constrainBoundary = false) => new UnsafeTriangulator<float2>().RefineMesh(UnsafeUtility.As<NativeOutputData<Vector2>, NativeOutputData<float2>>(ref output), allocator, areaThreshold, angleThreshold, concentricShells, constrainBoundary);
        /// <summary>
        /// Inserts a point into the given triangulation <paramref name="output"/> within the triangle at index <paramref name="tId"/>, using the specified barycentric coordinates <paramref name="bar"/>.
        /// For faster triangle lookup when inserting a point at specific coordinates, it is recommended to use an acceleration structure (e.g., bounding volume tree, buckets, etc.).
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// Only point insertion within the mesh bulk is supported.
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="tId">The index of the triangle in which the point should be inserted.</param>
        /// <param name="bar">
        /// The barycentric coordinates of the point to insert inside the triangle <paramref name="tId"/>. 
        /// All coordinates should be in the range (0, 1), and <paramref name="bar"/>.x + <paramref name="bar"/>.y + <paramref name="bar"/>.z must equal 1.
        /// </param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void DynamicInsertPoint(this UnsafeTriangulator<Vector2> @this, NativeOutputData<Vector2> output, int tId, Vector3 bar, Allocator allocator) =>
            new UnsafeTriangulator<float2>().DynamicInsertPoint(UnsafeUtility.As<NativeOutputData<Vector2>, NativeOutputData<float2>>(ref output), tId, bar, allocator);
        /// <summary>
        /// Splits the halfedge specified by <paramref name="he"/> by inserting a point at a position determined by linear interpolation.
        /// The position is interpolated between the start and end points of the halfedge in the triangulation <paramref name="output"/> 
        /// using <paramref name="alpha"/> as the interpolation parameter.
        /// This method preserves the "constrained" state of the halfedge, meaning that if the specified halfedge is constrained, 
        /// the two resulting sub-segments will also be marked as constrained.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="he">The index of the halfedge to split.</param>
        /// <param name="alpha">
        /// The interpolation parameter for positioning the new point between the start and end points of the halfedge, 
        /// where <c>p = (1 - alpha) * start + alpha * end</c>.
        /// </param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void DynamicSplitHalfedge(this UnsafeTriangulator<Vector2> @this, NativeOutputData<Vector2> output, int he, float alpha, Allocator allocator) => new UnsafeTriangulator<float2>().DynamicSplitHalfedge(UnsafeUtility.As<NativeOutputData<Vector2>, NativeOutputData<float2>>(ref output), he, alpha, allocator);
        /// <summary>
        /// Removes the specified point <paramref name="pId"/> from the <paramref name="output"/> data
        /// and re-triangulates the affected region to maintain a valid triangulation.
        /// This method supports only the removal of <b>bulk</b> points, i.e., points that are not located on the triangulation boundary.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="pId">The index of the <b>bulk</b> point to remove.</param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void DynamicRemoveBulkPoint(this UnsafeTriangulator<Vector2> @this, NativeOutputData<Vector2> output, int pId, Allocator allocator) => new UnsafeTriangulator<float2>().DynamicRemoveBulkPoint(UnsafeUtility.As<NativeOutputData<Vector2>, NativeOutputData<float2>>(ref output), pId, allocator);

        /// <summary>
        /// Performs triangulation on the given <paramref name="input"/>, producing the result in <paramref name="output"/> based on the settings specified in <paramref name="args"/>.
        /// This method corresponds to the native implementation of <see cref="Triangulator.Run"/>.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// The <paramref name="input"/> and <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void Triangulate(this UnsafeTriangulator<double2> @this, NativeInputData<double2> input, NativeOutputData<double2> output, Args args, Allocator allocator) => new UnsafeTriangulator<double, double2, double, TransformDouble, UtilsDouble>().Triangulate(input, output, args, allocator);
        /// <summary>
        /// Constrains the edge formed by the point indices (<paramref name="pi"/>, <paramref name="pj"/>)
        /// using the provided <paramref name="output"/> triangulation data.
        /// </summary>
        /// <remarks>
        /// <para><b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </para>
        /// <para><b>Limitations:</b>
        /// This method is designed to work only on a <b>bulk</b> mesh and does not support constraining edges that pass through holes in the triangulation.
        /// </para>
        /// </remarks>
        /// <param name="pi">The index of the first point of the edge to constrain.</param>
        /// <param name="pj">The index of the second point of the edge to constrain.</param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        /// <param name="ignoreForPlantingSeeds">
        /// If <see langword="true"/>, the halfedges corresponding to (<paramref name="pi"/>, <paramref name="pj"/>) are ignored during the seed planting step.
        /// </param>
        public static void ConstrainEdge(this UnsafeTriangulator<double2> @this, NativeOutputData<double2> output, int pi, int pj, Args args, Allocator allocator, bool ignoreForPlantingSeeds = false) => new UnsafeTriangulator<double, double2, double, TransformDouble, UtilsDouble>().ConstrainEdge(output, pi, pj, args, allocator, ignoreForPlantingSeeds);
        /// <summary>
        /// Plants hole seeds defined in <paramref name="input"/> (or restores boundaries or auto-holes if specified in <paramref name="args"/>)
        /// within the triangulation data in <paramref name="output"/>, using the settings specified in <paramref name="args"/>.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="input"/> and <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void PlantHoleSeeds(this UnsafeTriangulator<double2> @this, NativeInputData<double2> input, NativeOutputData<double2> output, Args args, Allocator allocator) => new UnsafeTriangulator<double, double2, double, TransformDouble, UtilsDouble>().PlantHoleSeeds(input, output, args, allocator);
        /// <summary>
        /// Refines the mesh for a valid triangulation in <paramref name="output"/>.
        /// Refinement parameters can be provided with the selected precision type T in generics, which is especially useful for fixed-point arithmetic.
        /// Refinement parameters in <see cref="Args"/> are restricted to <see cref="float"/> precision.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        /// <param name="angleThreshold">Expressed in <em>radians</em>. Default: 5° = 0.0872664626 rad.</param>
        /// <param name="constrainBoundary">Used to constrain boundary halfedges. Since the refinement algorithm (whether for constrained triangulation or not) requires constrained halfedges at the boundary, not setting this option may cause unexpected behavior, especially when the restoreBoundary option is disabled.</param>
        public static void RefineMesh(this UnsafeTriangulator<double2> @this, NativeOutputData<double2> output, Allocator allocator, double areaThreshold = 1, double angleThreshold = 0.0872664626, double concentricShells = 0.001, bool constrainBoundary = false) => new UnsafeTriangulator<double, double2, double, TransformDouble, UtilsDouble>().RefineMesh(output, allocator, 2 * areaThreshold, angleThreshold, concentricShells, constrainBoundary);
        /// <summary>
        /// Inserts a point into the given triangulation <paramref name="output"/> within the triangle at index <paramref name="tId"/>, using the specified barycentric coordinates <paramref name="bar"/>.
        /// For faster triangle lookup when inserting a point at specific coordinates, it is recommended to use an acceleration structure (e.g., bounding volume tree, buckets, etc.).
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// Only point insertion within the mesh bulk is supported.
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="tId">The index of the triangle in which the point should be inserted.</param>
        /// <param name="bar">
        /// The barycentric coordinates of the point to insert inside the triangle <paramref name="tId"/>. 
        /// All coordinates should be in the range (0, 1), and <paramref name="bar"/>.x + <paramref name="bar"/>.y + <paramref name="bar"/>.z must equal 1.
        /// </param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void DynamicInsertPoint(this UnsafeTriangulator<double2> @this, NativeOutputData<double2> output, int tId, double3 bar, Allocator allocator)
        {
            var (t0, t1, t2) = (output.Triangles[3 * tId + 0], output.Triangles[3 * tId + 1], output.Triangles[3 * tId + 2]);
            var (p0, p1, p2) = (output.Positions[t0], output.Positions[t1], output.Positions[t2]);
            var p = bar.x * p0 + bar.y * p1 + bar.z * p2;
            new UnsafeTriangulator<double, double2, double, TransformDouble, UtilsDouble>().DynamicInsertPoint(output, tId, p, allocator);
        }
        /// <summary>
        /// Splits the halfedge specified by <paramref name="he"/> by inserting a point at a position determined by linear interpolation.
        /// The position is interpolated between the start and end points of the halfedge in the triangulation <paramref name="output"/> 
        /// using <paramref name="alpha"/> as the interpolation parameter.
        /// This method preserves the "constrained" state of the halfedge, meaning that if the specified halfedge is constrained, 
        /// the two resulting sub-segments will also be marked as constrained.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="he">The index of the halfedge to split.</param>
        /// <param name="alpha">
        /// The interpolation parameter for positioning the new point between the start and end points of the halfedge, 
        /// where <c>p = (1 - alpha) * start + alpha * end</c>.
        /// </param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void DynamicSplitHalfedge(this UnsafeTriangulator<double2> @this, NativeOutputData<double2> output, int he, double alpha, Allocator allocator) => new UnsafeTriangulator<double, double2, double, TransformDouble, UtilsDouble>().DynamicSplitHalfedge(output, he, alpha, allocator);
        /// <summary>
        /// Removes the specified point <paramref name="pId"/> from the <paramref name="output"/> data
        /// and re-triangulates the affected region to maintain a valid triangulation.
        /// This method supports only the removal of <b>bulk</b> points, i.e., points that are not located on the triangulation boundary.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="pId">The index of the <b>bulk</b> point to remove.</param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void DynamicRemoveBulkPoint(this UnsafeTriangulator<double2> @this, NativeOutputData<double2> output, int pId, Allocator allocator) => new UnsafeTriangulator<double, double2, double, TransformDouble, UtilsDouble>().DynamicRemoveBulkPoint(output, pId, allocator);

        /// <summary>
        /// Performs triangulation on the given <paramref name="input"/>, producing the result in <paramref name="output"/> based on the settings specified in <paramref name="args"/>.
        /// This method corresponds to the native implementation of <see cref="Triangulator.Run"/>.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// The <paramref name="input"/> and <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void Triangulate(this UnsafeTriangulator<int2> @this, NativeInputData<int2> input, NativeOutputData<int2> output, Args args, Allocator allocator) => new UnsafeTriangulator<int, int2, long, TransformInt, UtilsInt>().Triangulate(input, output, args, allocator);
        /// <summary>
        /// Constrains the edge formed by the point indices (<paramref name="pi"/>, <paramref name="pj"/>)
        /// using the provided <paramref name="output"/> triangulation data.
        /// </summary>
        /// <remarks>
        /// <para><b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </para>
        /// <para><b>Limitations:</b>
        /// This method is designed to work only on a <b>bulk</b> mesh and does not support constraining edges that pass through holes in the triangulation.
        /// </para>
        /// </remarks>
        /// <param name="pi">The index of the first point of the edge to constrain.</param>
        /// <param name="pj">The index of the second point of the edge to constrain.</param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        /// <param name="ignoreForPlantingSeeds">
        /// If <see langword="true"/>, the halfedges corresponding to (<paramref name="pi"/>, <paramref name="pj"/>) are ignored during the seed planting step.
        /// </param>
        public static void ConstrainEdge(this UnsafeTriangulator<int2> @this, NativeOutputData<int2> output, int pi, int pj, Args args, Allocator allocator, bool ignoreForPlantingSeeds = false) => new UnsafeTriangulator<int, int2, long, TransformInt, UtilsInt>().ConstrainEdge(output, pi, pj, args, allocator, ignoreForPlantingSeeds);
        /// <summary>
        /// Plants hole seeds defined in <paramref name="input"/> (or restores boundaries or auto-holes if specified in <paramref name="args"/>)
        /// within the triangulation data in <paramref name="output"/>, using the settings specified in <paramref name="args"/>.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="input"/> and <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void PlantHoleSeeds(this UnsafeTriangulator<int2> @this, NativeInputData<int2> input, NativeOutputData<int2> output, Args args, Allocator allocator) => new UnsafeTriangulator<int, int2, long, TransformInt, UtilsInt>().PlantHoleSeeds(input, output, args, allocator);

#if UNITY_MATHEMATICS_FIXEDPOINT
        /// <summary>
        /// Performs triangulation on the given <paramref name="input"/>, producing the result in <paramref name="output"/> based on the settings specified in <paramref name="args"/>.
        /// This method corresponds to the native implementation of <see cref="Triangulator.Run"/>.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// The <paramref name="input"/> and <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void Triangulate(this UnsafeTriangulator<fp2> @this, NativeInputData<fp2> input, NativeOutputData<fp2> output, Args args, Allocator allocator) => new UnsafeTriangulator<fp, fp2, fp, TransformFp, UtilsFp>().Triangulate(input, output, args, allocator);
        /// <summary>
        /// Constrains the edge formed by the point indices (<paramref name="pi"/>, <paramref name="pj"/>)
        /// using the provided <paramref name="output"/> triangulation data.
        /// </summary>
        /// <remarks>
        /// <para><b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </para>
        /// <para><b>Limitations:</b>
        /// This method is designed to work only on a <b>bulk</b> mesh and does not support constraining edges that pass through holes in the triangulation.
        /// </para>
        /// </remarks>
        /// <param name="pi">The index of the first point of the edge to constrain.</param>
        /// <param name="pj">The index of the second point of the edge to constrain.</param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        /// <param name="ignoreForPlantingSeeds">
        /// If <see langword="true"/>, the halfedges corresponding to (<paramref name="pi"/>, <paramref name="pj"/>) are ignored during the seed planting step.
        /// </param>
        public static void ConstrainEdge(this UnsafeTriangulator<fp2> @this, NativeOutputData<fp2> output, int pi, int pj, Args args, Allocator allocator, bool ignoreForPlantingSeeds = false) => new UnsafeTriangulator<fp, fp2, fp, TransformFp, UtilsFp>().ConstrainEdge(output, pi, pj, args, allocator, ignoreForPlantingSeeds);
        /// <summary>
        /// Plants hole seeds defined in <paramref name="input"/> (or restores boundaries or auto-holes if specified in <paramref name="args"/>)
        /// within the triangulation data in <paramref name="output"/>, using the settings specified in <paramref name="args"/>.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="input"/> and <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void PlantHoleSeeds(this UnsafeTriangulator<fp2> @this, NativeInputData<fp2> input, NativeOutputData<fp2> output, Args args, Allocator allocator) => new UnsafeTriangulator<fp, fp2, fp, TransformFp, UtilsFp>().PlantHoleSeeds(input, output, args, allocator);
        /// <summary>
        /// Refines the mesh for a valid triangulation in <paramref name="output"/>.
        /// Refinement parameters can be provided with the selected precision type T in generics, which is especially useful for fixed-point arithmetic.
        /// Refinement parameters in <see cref="Args"/> are restricted to <see cref="float"/> precision.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        /// <param name="angleThreshold">Expressed in <em>radians</em>. Default: 5° = 0.0872664626 rad.</param>
        /// <param name="constrainBoundary">Used to constrain boundary halfedges. Since the refinement algorithm (whether for constrained triangulation or not) requires constrained halfedges at the boundary, not setting this option may cause unexpected behavior, especially when the restoreBoundary option is disabled.</param>
        public static void RefineMesh(this UnsafeTriangulator<fp2> @this, NativeOutputData<fp2> output, Allocator allocator, fp? areaThreshold = null, fp? angleThreshold = null, fp? concentricShells = null, bool constrainBoundary = false) => new UnsafeTriangulator<fp, fp2, fp, TransformFp, UtilsFp>().RefineMesh(output, allocator, 2 * (areaThreshold ?? 1), angleThreshold ?? fp.FromRaw(374806602) /*Raw value for (fp)0.0872664626*/, concentricShells ?? fp.FromRaw(4294967) /*Raw value for (fp)1 / 1000*/, constrainBoundary);
        /// <summary>
        /// Inserts a point into the given triangulation <paramref name="output"/> within the triangle at index <paramref name="tId"/>, using the specified barycentric coordinates <paramref name="bar"/>.
        /// For faster triangle lookup when inserting a point at specific coordinates, it is recommended to use an acceleration structure (e.g., bounding volume tree, buckets, etc.).
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// Only point insertion within the mesh bulk is supported.
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="tId">The index of the triangle in which the point should be inserted.</param>
        /// <param name="bar">
        /// The barycentric coordinates of the point to insert inside the triangle <paramref name="tId"/>. 
        /// All coordinates should be in the range (0, 1), and <paramref name="bar"/>.x + <paramref name="bar"/>.y + <paramref name="bar"/>.z must equal 1.
        /// </param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void DynamicInsertPoint(this UnsafeTriangulator<fp2> @this, NativeOutputData<fp2> output, int tId, fp3 bar, Allocator allocator)
        {
            var (t0, t1, t2) = (output.Triangles[3 * tId + 0], output.Triangles[3 * tId + 1], output.Triangles[3 * tId + 2]);
            var (p0, p1, p2) = (output.Positions[t0], output.Positions[t1], output.Positions[t2]);
            var p = bar.x * p0 + bar.y * p1 + bar.z * p2;
            new UnsafeTriangulator<fp, fp2, fp, TransformFp, UtilsFp>().DynamicInsertPoint(output, tId, p, allocator);
        }
        /// <summary>
        /// Splits the halfedge specified by <paramref name="he"/> by inserting a point at a position determined by linear interpolation.
        /// The position is interpolated between the start and end points of the halfedge in the triangulation <paramref name="output"/> 
        /// using <paramref name="alpha"/> as the interpolation parameter.
        /// This method preserves the "constrained" state of the halfedge, meaning that if the specified halfedge is constrained, 
        /// the two resulting sub-segments will also be marked as constrained.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="he">The index of the halfedge to split.</param>
        /// <param name="alpha">
        /// The interpolation parameter for positioning the new point between the start and end points of the halfedge, 
        /// where <c>p = (1 - alpha) * start + alpha * end</c>.
        /// </param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void DynamicSplitHalfedge(this UnsafeTriangulator<fp2> @this, NativeOutputData<fp2> output, int he, fp alpha, Allocator allocator) => new UnsafeTriangulator<fp, fp2, fp, TransformFp, UtilsFp>().DynamicSplitHalfedge(output, he, alpha, allocator);
        /// <summary>
        /// Removes the specified point <paramref name="pId"/> from the <paramref name="output"/> data
        /// and re-triangulates the affected region to maintain a valid triangulation.
        /// This method supports only the removal of <b>bulk</b> points, i.e., points that are not located on the triangulation boundary.
        /// </summary>
        /// <remarks>
        /// <b>Note:</b>
        /// This method requires that <paramref name="output"/> contains valid triangulation data.
        /// The <paramref name="output"/> native containers must be allocated by the user. Some buffers are optional; refer to the documentation for more details.
        /// </remarks>
        /// <param name="pId">The index of the <b>bulk</b> point to remove.</param>
        /// <param name="allocator">The allocator to use. If called from a job, consider using <see cref="Allocator.Temp"/>.</param>
        public static void DynamicRemoveBulkPoint(this UnsafeTriangulator<fp2> @this, NativeOutputData<fp2> output, int pId, Allocator allocator) => new UnsafeTriangulator<fp, fp2, fp, TransformFp, UtilsFp>().DynamicRemoveBulkPoint(output, pId, allocator);
#endif
    }

    /// <summary>
    /// Custom queue implementation which is a wrapper for <see cref="NativeList{T}"/>.
    /// This implementation is memory <b>extensive</b>.
    /// </summary>
    internal struct NativeQueueList<T> : IDisposable where T : unmanaged
    {
        public readonly bool IsCreated => impl.IsCreated;
        public readonly int Count => math.max(impl.Length - indexRef.Value, 0);

        private NativeList<T> impl;
        private NativeReference<int> indexRef;

        public NativeQueueList(int capacity, Allocator allocator)
        {
            impl = new(capacity, allocator);
            indexRef = new(0, allocator);
        }

        public NativeQueueList(Allocator allocator) : this(1, allocator) { }

        public ReadOnlySpan<T> AsReadOnlySpan() => impl.AsReadOnly().AsReadOnlySpan()[indexRef.Value..];
        public Span<T> AsSpan() => impl.AsArray().AsSpan()[indexRef.Value..];

        public void Clear()
        {
            impl.Clear();
            indexRef.Value = 0;
        }

        public void Dispose()
        {
            impl.Dispose();
            indexRef.Dispose();
        }

        public void Enqueue(T item) => impl.Add(item);
        public T Dequeue() => impl[indexRef.Value++];
        public readonly bool IsEmpty() => Count == 0;
        public bool TryDequeue(out T item)
        {
            var isEmpty = IsEmpty();
            if (isEmpty)
            {
                Clear();
            }
            item = isEmpty ? default : Dequeue();
            return !isEmpty;
        }
    }

    [BurstCompile]
    internal struct TriangulationJob<T, T2, TBig, TTransform, TUtils> : IJob
        where T : unmanaged, IComparable<T>
        where T2 : unmanaged
        where TBig : unmanaged, IComparable<TBig>
        where TTransform : unmanaged, ITransform<TTransform, T, T2>
        where TUtils : unmanaged, IUtils<T, T2, TBig>
    {
        private NativeArray<T2> inputPositions;
        [NativeDisableContainerSafetyRestriction]
        private NativeArray<int> constraints;
        [NativeDisableContainerSafetyRestriction]
        private NativeArray<T2> holeSeeds;
        [NativeDisableContainerSafetyRestriction]
        private NativeArray<bool> ignoreConstraintForPlantingSeeds;

        private NativeList<T2> outputPositions;
        private NativeList<int> triangles;
        private NativeList<int> halfedges;
        private NativeList<bool> constrainedHalfedges;
        private NativeList<bool> ignoredHalfedgesForPlantingSeeds;
        private NativeReference<Status> status;

        private readonly Args args;

        public TriangulationJob(NativeInputData<T2> input, NativeOutputData<T2> output, Args args)
        {
            inputPositions = input.Positions;
            constraints = input.ConstraintEdges;
            holeSeeds = input.HoleSeeds;
            ignoreConstraintForPlantingSeeds = input.IgnoreConstraintForPlantingSeeds;

            outputPositions = output.Positions;
            triangles = output.Triangles;
            halfedges = output.Halfedges;
            constrainedHalfedges = output.ConstrainedHalfedges;
            ignoredHalfedgesForPlantingSeeds = output.IgnoredHalfedgesForPlantingSeeds;
            status = output.Status;

            this.args = args;
        }

        public TriangulationJob(Triangulator<T2> @this)
        {
            inputPositions = @this.Input.Positions;
            constraints = @this.Input.ConstraintEdges;
            holeSeeds = @this.Input.HoleSeeds;
            ignoreConstraintForPlantingSeeds = @this.Input.IgnoreConstraintForPlantingSeeds;

            outputPositions = @this.Output.Positions;
            triangles = @this.Output.Triangles;
            halfedges = @this.Output.Halfedges;
            constrainedHalfedges = @this.Output.ConstrainedHalfedges;
            ignoredHalfedgesForPlantingSeeds = @this.Output.IgnoredHalfedgesForPlantingSeeds;
            status = @this.Output.Status;

            args = @this.Settings;
        }

        public void Execute()
        {
            new UnsafeTriangulator<T, T2, TBig, TTransform, TUtils>().Triangulate(
                input: new()
                {
                    Positions = inputPositions,
                    ConstraintEdges = constraints,
                    HoleSeeds = holeSeeds,
                    IgnoreConstraintForPlantingSeeds = ignoreConstraintForPlantingSeeds,
                },
                output: new()
                {
                    Positions = outputPositions,
                    Triangles = triangles,
                    Halfedges = halfedges,
                    ConstrainedHalfedges = constrainedHalfedges,
                    Status = status,
                    IgnoredHalfedgesForPlantingSeeds = ignoredHalfedgesForPlantingSeeds
                }, args, Allocator.Temp);
        }
    }

    internal readonly struct UnsafeTriangulator<T, T2, TBig, TTransform, TUtils>
        where T : unmanaged, IComparable<T>
        where T2 : unmanaged
        where TBig : unmanaged, IComparable<TBig>
        where TTransform : unmanaged, ITransform<TTransform, T, T2>
        where TUtils : unmanaged, IUtils<T, T2, TBig>
    {
        // NOTE: Caching ProfileMarker can boost performance for triangulations with small input (~10² triangles).
        private readonly struct Markers
        {
            public static readonly ProfilerMarker PreProcessInputStep = new(nameof(UnsafeTriangulator<T, T2, TBig, TTransform, TUtils>.PreProcessInputStep));
            public static readonly ProfilerMarker PostProcessInputStep = new(nameof(UnsafeTriangulator<T, T2, TBig, TTransform, TUtils>.PostProcessInputStep));
            public static readonly ProfilerMarker ValidateInputStep = new(nameof(UnsafeTriangulator<T, T2, TBig, TTransform, TUtils>.ValidateInputStep));
            public static readonly ProfilerMarker DelaunayTriangulationStep = new(nameof(UnsafeTriangulator<T, T2, TBig, TTransform, TUtils>.DelaunayTriangulationStep));
            public static readonly ProfilerMarker ConstrainEdgesStep = new(nameof(UnsafeTriangulator<T, T2, TBig, TTransform, TUtils>.ConstrainEdgesStep));
            public static readonly ProfilerMarker PlantingSeedStep = new(nameof(UnsafeTriangulator<T, T2, TBig, TTransform, TUtils>.PlantingSeedStep));
            public static readonly ProfilerMarker RefineMeshStep = new(nameof(UnsafeTriangulator<T, T2, TBig, TTransform, TUtils>.RefineMeshStep));
        }

        private static readonly TUtils utils = default;

        public void Triangulate(NativeInputData<T2> input, NativeOutputData<T2> output, Args args, Allocator allocator)
        {
            var tmpStatus = default(NativeReference<Status>);
            var tmpPositions = default(NativeList<T2>);
            var tmpHalfedges = default(NativeList<int>);
            var tmpConstrainedHalfedges = default(NativeList<bool>);
            var tmpIgnoredHalfedgesForPlantingSeeds = default(NativeList<bool>);
            output.Status = output.Status.IsCreated ? output.Status : tmpStatus = new(allocator);
            output.Positions = output.Positions.IsCreated ? output.Positions : tmpPositions = new(16 * 1024, allocator);
            output.Halfedges = output.Halfedges.IsCreated ? output.Halfedges : tmpHalfedges = new(6 * 16 * 1024, allocator);
            output.ConstrainedHalfedges = output.ConstrainedHalfedges.IsCreated ? output.ConstrainedHalfedges : tmpConstrainedHalfedges = new(6 * 16 * 1024, allocator);
            output.IgnoredHalfedgesForPlantingSeeds = output.IgnoredHalfedgesForPlantingSeeds.IsCreated ? output.IgnoredHalfedgesForPlantingSeeds : tmpIgnoredHalfedgesForPlantingSeeds = new(6 * 16 * 1024, allocator);

            output.Status.Value = Status.OK;
            output.Triangles.Clear();
            output.Positions.Clear();
            output.Halfedges.Clear();
            output.ConstrainedHalfedges.Clear();
            output.IgnoredHalfedgesForPlantingSeeds.Clear();

            PreProcessInputStep(input, output, args, out var localHoles, out var lt, allocator);
            new ValidateInputStep(input, output, args).Execute();
            new DelaunayTriangulationStep(output, args).Execute(allocator);
            new ConstrainEdgesStep(input, output, args).Execute(allocator);
            new PlantingSeedStep(output, args, localHoles).Execute(allocator, input.ConstraintEdges.IsCreated);
            new RefineMeshStep(output, args, lt).Execute(allocator, refineMesh: args.RefineMesh, constrainBoundary: !input.ConstraintEdges.IsCreated || !args.RestoreBoundary);
            PostProcessInputStep(output, args, lt);

            if (localHoles.IsCreated) localHoles.Dispose();
            if (tmpStatus.IsCreated) tmpStatus.Dispose();
            if (tmpPositions.IsCreated) tmpPositions.Dispose();
            if (tmpHalfedges.IsCreated) tmpHalfedges.Dispose();
            if (tmpConstrainedHalfedges.IsCreated) tmpConstrainedHalfedges.Dispose();
            if (tmpIgnoredHalfedgesForPlantingSeeds.IsCreated) tmpIgnoredHalfedgesForPlantingSeeds.Dispose();
        }

        public void ConstrainEdge(NativeOutputData<T2> output, int pi, int pj, Args args, Allocator allocator, bool ignoreForPlantingSeeds)
        {
            using var intersections = new NativeList<int>(allocator);
            using var unresolvedIntersections = new NativeList<int>(allocator);
            using var pointToHalfedge = new NativeArray<int>(output.Positions.Length, allocator);
            output.IgnoredHalfedgesForPlantingSeeds.Length = output.Halfedges.Length;

            FillPointToHalfedge(pointToHalfedge, output.Triangles.AsReadOnly());
            static void FillPointToHalfedge(Span<int> pointToHalfedge, ReadOnlySpan<int> triangles)
            {
                for (int i = 0; i < triangles.Length; i++)
                {
                    pointToHalfedge[triangles[i]] = i;
                }
            }

            new ConstrainEdgesStep.UnsafeSloan
            {
                Status = output.Status,
                Positions = output.Positions.AsReadOnly(),
                Triangles = output.Triangles,
                Halfedges = output.Halfedges,
                ConstrainedHalfedges = output.ConstrainedHalfedges,
                IgnoredHalfedgesForPlantingSeeds = output.IgnoredHalfedgesForPlantingSeeds,

                Intersections = intersections,
                UnresolvedIntersections = unresolvedIntersections,
                PointToHalfedge = pointToHalfedge,

                Args = args,
            }.TryApplyConstraint(new(pi, pj), ignoreForPlantingSeeds);
        }

        public void PlantHoleSeeds(NativeInputData<T2> input, NativeOutputData<T2> output, Args args, Allocator allocator)
        {
            new PlantingSeedStep(input, output, args).Execute(allocator, true);
        }

        public void RefineMesh(NativeOutputData<T2> output, Allocator allocator, T area2Threshold, T angleThreshold, T shells, bool constrainBoundary = false)
        {
            new RefineMeshStep(output, area2Threshold, angleThreshold, shells).Execute(allocator, refineMesh: true, constrainBoundary);
        }

        public void DynamicInsertPoint(NativeOutputData<T2> output, int tId, T2 p, Allocator allocator)
        {
            using var pathHalfedges = new NativeList<int>(allocator);
            using var pathPoints = new NativeList<int>(allocator);
            using var trianglesQueue = new NativeQueueList<int>(allocator);
            using var visitedTriangles = new NativeList<bool>(allocator);

            new RefineMeshStep.UnsafeBowerWatson
            {
                Circles = default,
                Output = output,
                PathHalfedges = pathHalfedges,
                PathPoints = pathPoints,
                TrianglesQueue = trianglesQueue,
                VisitedTriangles = visitedTriangles,
            }.UnsafeInsertPointBulk(p, tId);
        }

        public void DynamicSplitHalfedge(NativeOutputData<T2> output, int he, T alpha, Allocator allocator)
        {
            using var pathHalfedges = new NativeList<int>(allocator);
            using var pathPoints = new NativeList<int>(allocator);
            using var trianglesQueue = new NativeQueueList<int>(allocator);
            using var visitedTriangles = new NativeList<bool>(allocator);

            var triangles = output.Triangles;
            var halfedges = output.Halfedges;
            var constrainedHalfedges = output.ConstrainedHalfedges;

            var (i, j) = (triangles[he], triangles[NextHalfedge(he)]);
            var (p0, p1) = (output.Positions[i], output.Positions[j]);
            var p = utils.lerp(p0, p1, alpha);

            var bw = new RefineMeshStep.UnsafeBowerWatson
            {
                Circles = default,
                Output = output,
                PathHalfedges = pathHalfedges,
                PathPoints = pathPoints,
                TrianglesQueue = trianglesQueue,
                VisitedTriangles = visitedTriangles,
            };

            constrainedHalfedges[he] = false;
            var ohe = halfedges[he];
            if (ohe != -1)
            {
                constrainedHalfedges[ohe] = false;
            }

            if (ohe == -1)
            {
                bw.UnsafeInsertPointBoundary(p, he);

                //var h0 = triangles.Length - 3;
                var id = 3 * (pathPoints.Length - 1);
                var hi = halfedges.Length - 1;
                var hj = halfedges.Length - id;
                constrainedHalfedges[hi] = true;
                constrainedHalfedges[hj] = true;
            }
            else
            {
                bw.UnsafeInsertPointBulk(p, he / 3);

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

                var ohi = halfedges[hi];
                var ohj = halfedges[hj];

                constrainedHalfedges[hi] = true;
                constrainedHalfedges[ohi] = true;
                constrainedHalfedges[hj] = true;
                constrainedHalfedges[ohj] = true;
            }
        }

        public void DynamicRemoveBulkPoint(NativeOutputData<T2> output, int pId, Allocator allocator)
        {
            /// This utility removes the specified point `pId`. It is designed specifically for handling bulk points only!
            ///
            /// The algorithm operates as follows:
            ///
            /// 1. Iterate over all triangles containing `pId` to create:
            ///    loops of halfedges `heLoop`, points `pIdLoop`, and visited triangles.
            ///
            ///        p1  h1        p2 h2
            ///         o --------- o
            ///       .'             '.
            ///     .'                 '.
            ///   .'       * pId         'o p3 h3
            ///  o p0 h0               ..''
            ///   ....           ....''
            ///       '''....o'''
            ///                p4 h3
            ///
            ///   heLoop  = [h0, h1, h2, h3, h4]
            ///   pIdLoop = [p0, p1, p2, p3, p4]
            ///   oheLoop = [h0', h1', h2', h3', h4'], hi' = halfedges[hi]
            ///
            /// 2. Remove all visited triangles and adapt `oheLoop` to reflect the changes.
            /// 3. Triangulate (with restoring boundaries) the cavity created by `pIdLoop` points.
            /// 4. Merge the resulting triangulation with the newly created triangulated cavity.
            /// 5. Remove `pId` and adjust triangle indexes accordingly.
            using var heLoop = new NativeList<int>(allocator);
            BuildHeLoop(output, heLoop, pId);
            using var visitedTriangles = new NativeArray<bool>(output.Triangles.Length / 3, allocator);
            using var pIdLoop = new NativeArray<int>(heLoop.Length, allocator);
            using var oheLoop = new NativeArray<int>(heLoop.Length, allocator);
            using var oheConstrained = new NativeArray<bool>(heLoop.Length, allocator);
            BuildLoops(output, heLoop, visitedTriangles, pIdLoop, oheLoop, oheConstrained, out var tIdMinVisited);
            RemoveTriangles(output, visitedTriangles, tIdMinVisited, oheLoop);
            using var cavityTriangles = new NativeList<int>(allocator);
            using var cavityHalfedges = new NativeList<int>(allocator);
            TriangulateCavity(output, pIdLoop, cavityTriangles, cavityHalfedges, allocator);
            MergeTriangulations(output, pIdLoop, cavityTriangles, cavityHalfedges, oheLoop, oheConstrained);
            AdaptPoints(output, pId);
        }

        private static void BuildHeLoop(NativeOutputData<T2> output, NativeList<int> heLoop, int pId)
        {
            var h0 = -1;
            /// NOTE: This can be optimized to an O(1) operation by introducing a `pointToHalfedge` buffer.
            ///       However, `pointToHalfedge` is currently only utilized during the Sloan algorithm (constraints),
            ///       and other parts of the code are not yet adapted to incorporate `pointToHalfedge`.
            ///       That said, having O(n) complexity here is not a significant issue.
            for (int i = 0; i < output.Triangles.Length; i++)
            {
                if (output.Triangles[i] == pId)
                {
                    h0 = i;
                    break;
                }
            }

            h0 = NextHalfedge(h0);
            var h1 = NextHalfedge(h0);
            var p = output.Triangles[h0];
            var q = output.Triangles[h1];
            heLoop.Add(h0);
            while (p != q)
            {
                h0 = NextHalfedge(output.Halfedges[h1]);
                h1 = NextHalfedge(h0);
                q = output.Triangles[h1];
                heLoop.Add(h0);
            }
        }

        private static void BuildLoops(NativeOutputData<T2> output, NativeList<int> heLoop, NativeArray<bool> visitedTriangles, NativeArray<int> pIdLoop, NativeArray<int> oheLoop, NativeArray<bool> oheConstrained, out int tIdMinVisited)
        {
            tIdMinVisited = int.MaxValue;
            for (int i = 0; i < heLoop.Length; i++)
            {
                var he = heLoop[i];
                visitedTriangles[he / 3] = true;
                tIdMinVisited = math.min(he / 3, tIdMinVisited);
                pIdLoop[i] = output.Triangles[he];
                oheLoop[i] = output.Halfedges[he];
                oheConstrained[i] = output.ConstrainedHalfedges[he];
            }
        }

        private static void RemoveTriangles(NativeOutputData<T2> output, NativeArray<bool> visitedTriangles, int tIdMinVisited, NativeArray<int> halfedgeLoop)
        {
            static void DisableHe(NativeList<int> halfedges, int he, int rId)
            {
                var ohe = halfedges[3 * rId + he];
                if (ohe != -1)
                {
                    halfedges[ohe] = -1;
                }
            }

            static void AdaptHe(NativeList<int> halfedges, int he, int rId, int wId)
            {
                var ohe = halfedges[3 * rId + he];
                halfedges[3 * wId + he] = ohe;
                if (ohe != -1)
                {
                    halfedges[ohe] = 3 * wId + he;
                }
            }

            var triangles = output.Triangles;
            var halfedges = output.Halfedges;
            var constrainedHalfedges = output.ConstrainedHalfedges;

            // Reinterpret to a larger struct to make copies of whole triangles slightly more efficient
            var constrainedHalfedges3 = constrainedHalfedges.AsArray().Reinterpret<bool3>(1);
            var triangles3 = triangles.AsArray().Reinterpret<int3>(4);

            var wId = tIdMinVisited;
            for (int rId = tIdMinVisited; rId < triangles3.Length; rId++)
            {
                if (!visitedTriangles[rId])
                {
                    triangles3[wId] = triangles3[rId];
                    constrainedHalfedges3[wId] = constrainedHalfedges3[rId];
                    AdaptHe(halfedges, 0, rId, wId);
                    AdaptHe(halfedges, 1, rId, wId);
                    AdaptHe(halfedges, 2, rId, wId);
                    wId++;
                }
                else
                {
                    DisableHe(halfedges, 0, rId);
                    DisableHe(halfedges, 1, rId);
                    DisableHe(halfedges, 2, rId);

                    for (int i = 0; i < halfedgeLoop.Length; i++)
                    {
                        var he = halfedgeLoop[i];
                        halfedgeLoop[i] = he != -1 && he / 3 > wId ? he - 3 : he;
                    }
                }
            }

            // Trim the data to reflect removed triangles.
            triangles.Length = 3 * wId;
            constrainedHalfedges.Length = 3 * wId;
            halfedges.Length = 3 * wId;
        }

        private static void TriangulateCavity(NativeOutputData<T2> output, NativeArray<int> pIdLoop, NativeList<int> cavityTriangles, NativeList<int> cavityHalfedges, Allocator allocator)
        {
            using var cavityPositions = new NativeArray<T2>(pIdLoop.Length, allocator);
            using var cavityConstraints = new NativeArray<int>(2 * pIdLoop.Length, allocator);

            void BuildInput(NativeArray<T2> cavityPositions, NativeArray<int> cavityConstraints)
            {
                for (int i = 0; i < pIdLoop.Length; i++)
                {
                    cavityPositions[i] = output.Positions[pIdLoop[i]];
                }
                for (int i = 0; i < pIdLoop.Length - 1; i++)
                {
                    cavityConstraints[2 * i + 0] = i;
                    cavityConstraints[2 * i + 1] = i + 1;
                }
                cavityConstraints[^2] = pIdLoop.Length - 1;
                cavityConstraints[^1] = 0;
            }

            BuildInput(cavityPositions, cavityConstraints);

            new UnsafeTriangulator<T, T2, TBig, TTransform, TUtils>().Triangulate(
                input: new() { Positions = cavityPositions, ConstraintEdges = cavityConstraints },
                output: new() { Triangles = cavityTriangles, Halfedges = cavityHalfedges },
                args: Args.Default(restoreBoundary: true),
                allocator
            );
        }

        private static void MergeTriangulations(NativeOutputData<T2> output, NativeArray<int> pIdLoop, NativeList<int> cavityTriangles, NativeList<int> cavityHalfedges, NativeArray<int> oheLoop, NativeArray<bool> oheConstrained)
        {
            output.ConstrainedHalfedges.Length += cavityHalfedges.Length;

            var ell = output.Triangles.Length;
            for (int i = 0; i < cavityHalfedges.Length; i++)
            {
                var he = cavityHalfedges[i];
                if (he != -1)
                {
                    cavityHalfedges[i] = he + ell;
                }
                else
                {
                    var id = cavityTriangles[i];
                    var ohe = oheLoop[id];
                    output.ConstrainedHalfedges[i + ell] |= oheConstrained[id];
                    cavityHalfedges[i] = ohe;
                    if (ohe != -1)
                    {
                        output.Halfedges[ohe] = i + ell;
                    }
                }
            }

            for (int i = 0; i < cavityTriangles.Length; i++)
            {
                cavityTriangles[i] = pIdLoop[cavityTriangles[i]];
            }

            output.Triangles.AddRange(cavityTriangles.AsArray());
            output.Halfedges.AddRange(cavityHalfedges.AsArray());
        }

        private static void AdaptPoints(NativeOutputData<T2> output, int pId)
        {
            output.Positions.RemoveAt(pId);

            var triangles = output.Triangles;
            for (int i = 0; i < triangles.Length; i++)
            {
                var t = triangles[i];
                triangles[i] = t > pId ? t - 1 : t;
            }
        }

        private void PreProcessInputStep(NativeInputData<T2> input, NativeOutputData<T2> output, Args args, out NativeArray<T2> localHoles, out TTransform lt, Allocator allocator)
        {
            using var _ = Markers.PreProcessInputStep.Auto();

            var localPositions = output.Positions;
            localPositions.ResizeUninitialized(input.Positions.Length);
            if (args.Preprocessor == Preprocessor.PCA || args.Preprocessor == Preprocessor.COM)
            {
                lt = args.Preprocessor == Preprocessor.PCA ? default(TTransform).CalculatePCATransformation(input.Positions) : default(TTransform).CalculateLocalTransformation(input.Positions);
                for (int i = 0; i < input.Positions.Length; i++)
                {
                    localPositions[i] = lt.Transform(input.Positions[i]);
                }

                localHoles = input.HoleSeeds.IsCreated ? new(input.HoleSeeds.Length, allocator) : default;
                for (int i = 0; i < input.HoleSeeds.Length; i++)
                {
                    localHoles[i] = lt.Transform(input.HoleSeeds[i]);
                }
            }
            else if (args.Preprocessor == Preprocessor.None)
            {
                localPositions.CopyFrom(input.Positions);
                localHoles = input.HoleSeeds.IsCreated ? new(input.HoleSeeds, allocator) : default;
                lt = default(TTransform).Identity;
            }
            else
            {
                throw new ArgumentException();
            }
        }

        private void PostProcessInputStep(NativeOutputData<T2> output, Args args, TTransform lt)
        {
            if (args.Preprocessor == Preprocessor.None)
            {
                return;
            }

            using var _ = Markers.PostProcessInputStep.Auto();
            var inverse = lt.Inverse();
            for (int i = 0; i < output.Positions.Length; i++)
            {
                output.Positions[i] = inverse.Transform(output.Positions[i]);
            }
        }

        private struct ValidateInputStep
        {
            private NativeArray<T2>.ReadOnly positions;
            private NativeReference<Status> status;
            private readonly Args args;
            private NativeArray<int>.ReadOnly constraints;
            private NativeArray<T2>.ReadOnly holes;
            private NativeArray<bool>.ReadOnly ignoredConstraints;

            public ValidateInputStep(NativeInputData<T2> input, NativeOutputData<T2> output, Args args)
            {
                positions = output.Positions.AsReadOnly();
                status = output.Status;
                this.args = args;
                constraints = input.ConstraintEdges.AsReadOnly();
                holes = input.HoleSeeds.AsReadOnly();
                ignoredConstraints = input.IgnoreConstraintForPlantingSeeds.AsReadOnly();
            }

            public void Execute()
            {
                if (!args.ValidateInput)
                {
                    return;
                }

                using var _ = Markers.ValidateInputStep.Auto();

                ValidateArgs();
                ValidatePositions();
                ValidateConstraints();
                ValidateHoles();
                ValidateIgnoredConstraints();
            }

            private void ValidateArgs()
            {
                if (args.AutoHolesAndBoundary && !constraints.IsCreated)
                {
                    LogWarning($"[Triangulator]: AutoHolesAndBoundary is selected, but the ConstraintEdges buffer is not provided. This setting has no effect.");
                }

                if (args.RestoreBoundary && !constraints.IsCreated)
                {
                    LogWarning($"[Triangulator]: RestoreBoundary is selected, but the ConstraintEdges buffer is not provided. This setting has no effect.");
                }

                if (args.RefineMesh && !utils.SupportRefinement())
                {
                    LogError($"[Triangulator]: Invalid arguments! RefineMesh is selected, but the selected type T does not support mesh refinement.");
                    status.Value |= Status.ERR_ARGS_INVALID;
                }

                if (constraints.IsCreated && args.SloanMaxIters < 1)
                {
                    LogError($"[Triangulator]: Invalid arguments! SloanMaxIters must be a positive integer.");
                    status.Value |= Status.ERR_ARGS_INVALID;
                }

                if (args.RefineMesh && args.RefinementThresholdArea < 0)
                {
                    LogError($"[Triangulator]: Invalid arguments! RefinementThresholdArea must be a positive float.");
                    status.Value |= Status.ERR_ARGS_INVALID;
                }

                if (args.RefineMesh && args.RefinementThresholdAngle < 0 || args.RefinementThresholdAngle > math.PI / 4)
                {
                    LogError($"[Triangulator]: Invalid arguments! RefinementThresholdAngle must be in the range [0, π / 4]. Note that in the literature, the upper boundary for convergence is approximately π / 6.");
                    status.Value |= Status.ERR_ARGS_INVALID;
                }
            }

            private void ValidatePositions()
            {
                if (positions.Length < 3)
                {
                    LogError($"[Triangulator]: Positions.Length is less then 3!");
                    status.Value |= Status.ERR_INPUT_POSITIONS_LENGTH;
                }

                for (int i = 0; i < positions.Length; i++)
                {
                    if (math.any(!utils.isfinite(positions[i])))
                    {
                        LogError($"[Triangulator]: Positions[{i}] does not contain finite value: {positions[i]}!");
                        status.Value |= Status.ERR_INPUT_POSITIONS_UNDEFINED_VALUE;
                    }

                    var pi = positions[i];
                    for (int j = i + 1; j < positions.Length; j++)
                    {
                        var pj = positions[j];
                        if (math.all(utils.eq(pi, pj)))
                        {
                            LogError($"[Triangulator]: Positions[{i}] and [{j}] are duplicated with value: {pi}!");
                            status.Value |= Status.ERR_INPUT_POSITIONS_DUPLICATES;
                        }
                    }
                }
            }

            private void ValidateConstraints()
            {
                if (!constraints.IsCreated)
                {
                    return;
                }

                if (constraints.Length % 2 != 0)
                {
                    LogError($"[Triangulator]: Constraint input buffer does not contain even number of elements!");
                    status.Value |= Status.ERR_INPUT_CONSTRAINTS_LENGTH;
                    return;
                }

                var invalid = false;
                // Edge validation
                for (int i = 0; i < constraints.Length / 2; i++)
                {
                    var (a0Id, a1Id) = (constraints[2 * i], constraints[2 * i + 1]);
                    var count = positions.Length;
                    if (a0Id >= count || a0Id < 0 || a1Id >= count || a1Id < 0)
                    {
                        LogError($"[Triangulator]: ConstraintEdges[{i}] = ({a0Id}, {a1Id}) is out of range Positions.Length = {count}!");
                        status.Value |= Status.ERR_INPUT_CONSTRAINTS_OUT_OF_RANGE;
                        invalid = true;
                    }

                    if (a0Id == a1Id)
                    {
                        LogError($"[Triangulator]: ConstraintEdges[{i}] = ({a0Id}, {a1Id}) is length zero!");
                        status.Value |= Status.ERR_INPUT_CONSTRAINTS_SELF_LOOP;
                        invalid = true;
                    }
                }

                if (invalid)
                {
                    return;
                }

                // Edge-point validation
                for (int i = 0; i < constraints.Length / 2; i++)
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
                            LogError($"[Triangulator]: ConstraintEdges[{i}] = ({a0Id}, {a1Id}) = <({utils.X(a0)}, {utils.Y(a0)}), ({utils.X(a1)}, {utils.Y(a1)})> and Positions[{j}] = <({utils.X(p)}, {utils.Y(p)})> are collinear!");
                            status.Value |= Status.ERR_INPUT_CONSTRAINTS_COLLINEAR;
                        }
                    }
                }

                // Edge-edge validation
                for (int i = 0; i < constraints.Length / 2; i++)
                {
                    var (a0Id, a1Id) = (constraints[2 * i], constraints[2 * i + 1]);
                    var (a0, a1) = (positions[a0Id], positions[a1Id]);

                    for (int j = i + 1; j < constraints.Length / 2; j++)
                    {
                        var (b0Id, b1Id) = (constraints[2 * j], constraints[2 * j + 1]);

                        if (a0Id == b0Id && a1Id == b1Id || a0Id == b1Id && a1Id == b0Id)
                        {
                            LogError($"[Triangulator]: ConstraintEdges[{i}] = ({a0Id}, {a1Id}) and ConstraintEdges[{j}] = ({b0Id}, {b1Id}) are equivalent!");
                            status.Value |= Status.ERR_INPUT_CONSTRAINTS_DUPLICATES;
                        }

                        // One common point, cases should be filtered out at edge-point validation
                        if (a0Id == b0Id || a0Id == b1Id || a1Id == b0Id || a1Id == b1Id)
                        {
                            continue;
                        }

                        var (b0, b1) = (positions[b0Id], positions[b1Id]);
                        if (EdgeEdgeIntersection(a0, a1, b0, b1))
                        {
                            LogError($"[Triangulator]: ConstraintEdges[{i}] = ({a0Id}, {a1Id}) = <({utils.X(a0)}, {utils.Y(a0)}), ({utils.X(a1)}, {utils.Y(a1)})> and ConstraintEdges[{j}] = ({b0Id}, {b1Id}) = <({utils.X(b0)}, {utils.Y(b0)}), ({utils.X(b1)}, {utils.Y(b1)})> intersect!");
                            status.Value |= Status.ERR_INPUT_CONSTRAINTS_INTERSECTING;
                        }
                    }
                }
            }

            private void ValidateHoles()
            {
                if (!holes.IsCreated)
                {
                    return;
                }

                if (!constraints.IsCreated)
                {
                    LogWarning($"[Triangulator]: HoleSeeds buffer is provided, but ConstraintEdges is missing. This setting has no effect.");
                }

                for (int i = 0; i < holes.Length; i++)
                {
                    if (math.any(!utils.isfinite(holes[i])))
                    {
                        LogError($"[Triangulator]: HoleSeeds[{i}] does not contain finite value: {holes[i]}!");
                        status.Value |= Status.ERR_INPUT_HOLES_UNDEFINED_VALUE;
                    }
                }
            }

            private void ValidateIgnoredConstraints()
            {
                if (!ignoredConstraints.IsCreated)
                {
                    return;
                }

                if (!constraints.IsCreated)
                {
                    LogWarning($"[Triangulator]: IgnoreConstraintForPlantingSeeds buffer is provided, but ConstraintEdges is missing. This setting has no effect.");
                }

                if (constraints.IsCreated && ignoredConstraints.Length != constraints.Length / 2)
                {
                    LogError($"[Triangulator]: IgnoreConstraintForPlantingSeeds length must be equal to half the length of ConstraintEdges!");
                    status.Value |= Status.ERR_INPUT_IGNORED_CONSTRAINTS_LENGTH;
                }
            }

            private readonly void LogError(string message)
            {
                if (args.Verbose)
                {
                    Debug.LogError(message);
                }
            }

            private readonly void LogWarning(string message)
            {
                if (args.Verbose)
                {
                    Debug.LogWarning(message);
                }
            }
        }

        /// <summary>
        /// This step is based on the following projects:
        /// <list type="bullet">
        /// <item><see href="https://github.com/mapbox/delaunator">delaunator</see></item>
        /// <item><see href="https://github.com/nol1fe/delaunator-sharp/">delaunator-sharp</see></item>
        /// </list>
        /// </summary>
        private struct DelaunayTriangulationStep
        {
            private struct DistComparer : IComparer<int>
            {
                private NativeArray<TBig> dist;
                public DistComparer(NativeArray<TBig> dist) => this.dist = dist;
                public int Compare(int x, int y) => dist[x].CompareTo(dist[y]);
            }

            private NativeReference<Status> status;
            private NativeArray<T2>.ReadOnly positions;
            private NativeList<int> triangles;
            private NativeList<int> halfedges;
            private NativeList<bool> constrainedHalfedges;
            private NativeArray<int> hullNext, hullPrev, hullTri, hullHash;
            private NativeArray<int> EDGE_STACK;

            private readonly int hashSize;
            private readonly bool verbose;
            private int hullStart;
            private int trianglesLen;

            public DelaunayTriangulationStep(NativeOutputData<T2> output, Args args)
            {
                status = output.Status;
                positions = output.Positions.AsReadOnly();
                triangles = output.Triangles;
                halfedges = output.Halfedges;
                constrainedHalfedges = output.ConstrainedHalfedges;
                hullStart = int.MaxValue;
                verbose = args.Verbose;
                hashSize = (int)math.ceil(math.sqrt(positions.Length));
                trianglesLen = default;

                hullNext = default;
                hullPrev = default;
                hullTri = default;
                hullHash = default;
                EDGE_STACK = default;
            }

            public void Execute(Allocator allocator)
            {
                if (status.Value != Status.OK)
                {
                    return;
                }

                using var _ = Markers.DelaunayTriangulationStep.Auto();

                var n = positions.Length;
                var maxTriangles = math.max(2 * n - 5, 0);
                triangles.Length = 3 * maxTriangles;
                halfedges.Length = 3 * maxTriangles;

                var ids = new NativeArray<int>(n, allocator);

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

                // NOTE: Since `int` does not support NaN or infinity, a circumcenter check is required for int2 validation.
                // Be aware that the CircumRadiusSq calculation might overflow, leading to invalid or garbage results.
                if (i2 == int.MaxValue || math.any(utils.eq(utils.CircumCenter(p0, p1, positions[i2]), utils.MaxValue2())))
                {
                    if (verbose)
                    {
                        Debug.LogError("[Triangulator]: The provided input is not valid. There are either duplicate points or all are collinear.");
                    }
                    status.Value |= Status.ERR_DELAUNAY_DUPLICATES_OR_COLLINEAR;
                    ids.Dispose();
                    return;
                }

                using var _hullPrev = hullPrev = new(n, allocator);
                using var _hullNext = hullNext = new(n, allocator);
                using var _hullTri = hullTri = new(n, allocator);
                using var _hullHash = hullHash = new(hashSize, allocator);
                using var _EDGE_STACK = EDGE_STACK = new(math.min(3 * maxTriangles, 512), allocator);
                var dists = new NativeArray<TBig>(n, allocator);

                // Vertex closest to p1 and p2, as measured by the circumscribed circle radius of p1, p2, p3
                // Thus (p1,p2,p3) form a triangle close to the center of the point set, and it's guaranteed that there
                // are no other vertices inside this triangle.
                var p2 = positions[i2];

                // Swap the order of the vertices if the triangle is not oriented in the right direction
                if (utils.less(Orient2dFast(p0, p1, p2), utils.ZeroTBig()))
                {
                    (i1, i2) = (i2, i1);
                    (p1, p2) = (p2, p1);
                }

                // Sort all other vertices by their distance to the circumcenter of the initial triangle
                var c = utils.CircumCenter(p0, p1, p2);

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
                    var key = utils.hashkey(p, c, hashSize);
                    for (var j = 0; j < hashSize; j++)
                    {
                        start = hullHash[(key + j) % hashSize];
                        if (start != -1 && start != hullNext[start]) break;
                    }

                    start = hullPrev[start];
                    var e = start;
                    var q = hullNext[e];

                    while (!utils.less(Orient2dFast(p, positions[e], positions[q]), utils.ZeroTBig()))
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
                    while (utils.less(Orient2dFast(p, positions[next], positions[q]), utils.ZeroTBig()))
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

                        while (utils.less(Orient2dFast(p, positions[q], positions[e]), utils.ZeroTBig()))
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
                constrainedHalfedges.Length = trianglesLen;

                ids.Dispose();
                dists.Dispose();
            }

            private int Legalize(int a)
            {
                var stackSize = 0;
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
                        if (stackSize == 0) break;
                        a = EDGE_STACK[--stackSize];
                        continue;
                    }

                    var b0 = b - b % 3;
                    var al = a0 + (a + 1) % 3;
                    var bl = b0 + (b + 2) % 3;

                    var p0 = triangles[ar];
                    var pr = triangles[a];
                    var pl = triangles[al];
                    var p1 = triangles[bl];

                    var illegal = utils.InCircle(positions[p0], positions[pr], positions[pl], positions[p1]);

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
                        if (stackSize < EDGE_STACK.Length)
                        {
                            EDGE_STACK[stackSize++] = br;
                        }
                    }
                    else
                    {
                        if (stackSize == 0) break;
                        a = EDGE_STACK[--stackSize];
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

        /// <summary>
        /// This step implements <i>Sloan algorithm</i>.
        /// Read more in the paper:
        /// <see href="https://doi.org/10.1016/0045-7949(93)90239-A">
        /// S. W. Sloan. "A fast algorithm for generating constrained Delaunay triangulations." <i>Comput. Struct.</i> <b>47</b>.3:441-450 (1993).
        /// </see>
        /// </summary>
        private struct ConstrainEdgesStep
        {
            private NativeReference<Status> status;
            private NativeArray<T2>.ReadOnly positions;
            private NativeArray<int>.ReadOnly inputConstraintEdges;
            // NOTE: `triangles`, `halfedges`, and `constrainedHalfedges` can be NativeArray, however, Job system can throw here the exception:
            //
            // ```
            // InvalidOperationException: The Unity.Collections.NativeList`1[System.Int32]
            // has been declared as [WriteOnly] in the job, but you are reading from it.
            // ```
            //
            // See the `UsingTempAllocatorInJobTest` to learn more.
            private NativeList<int> triangles;
            private NativeList<int> halfedges;
            private NativeList<bool> constrainedHalfedges;
            private NativeList<bool> ignoredHalfedgesForPlantingSeeds;
            private NativeArray<bool> ignoreConstraintForPlantingSeeds;
            private readonly Args args;

            public ConstrainEdgesStep(NativeInputData<T2> input, NativeOutputData<T2> output, Args args)
            {
                status = output.Status;
                positions = output.Positions.AsReadOnly();
                triangles = output.Triangles;
                inputConstraintEdges = input.ConstraintEdges.AsReadOnly();
                ignoreConstraintForPlantingSeeds = input.IgnoreConstraintForPlantingSeeds;
                halfedges = output.Halfedges;
                constrainedHalfedges = output.ConstrainedHalfedges;
                ignoredHalfedgesForPlantingSeeds = output.IgnoredHalfedgesForPlantingSeeds;
                this.args = args;
            }

            public void Execute(Allocator allocator)
            {
                if (!inputConstraintEdges.IsCreated || status.Value != Status.OK)
                {
                    return;
                }

                using var _ = Markers.ConstrainEdgesStep.Auto();

                using var intersections = new NativeList<int>(allocator);
                using var unresolvedIntersections = new NativeList<int>(allocator);
                using var pointToHalfedge = new NativeArray<int>(positions.Length, allocator);
                ignoredHalfedgesForPlantingSeeds.Length = halfedges.Length;

                FillPointToHalfedge(pointToHalfedge, triangles.AsReadOnly());
                static void FillPointToHalfedge(Span<int> pointToHalfedge, ReadOnlySpan<int> triangles)
                {
                    for (int i = 0; i < triangles.Length; i++)
                    {
                        pointToHalfedge[triangles[i]] = i;
                    }
                }

                var sloan = new UnsafeSloan()
                {
                    Status = status,
                    Positions = positions,
                    Triangles = triangles,
                    Halfedges = halfedges,
                    ConstrainedHalfedges = constrainedHalfedges,
                    IgnoredHalfedgesForPlantingSeeds = ignoredHalfedgesForPlantingSeeds,

                    Intersections = intersections,
                    UnresolvedIntersections = unresolvedIntersections,
                    PointToHalfedge = pointToHalfedge,

                    Args = args,
                };

                for (int index = 0; index < inputConstraintEdges.Length / 2; index++)
                {
                    var c = math.int2(
                        inputConstraintEdges[2 * index + 0],
                        inputConstraintEdges[2 * index + 1]
                    );
                    c = c.x < c.y ? c.xy : c.yx; // Backward compatibility. To remove in the future.
                    sloan.TryApplyConstraint(c, ignoreForPlantingSeeds: ignoreConstraintForPlantingSeeds.IsCreated && ignoreConstraintForPlantingSeeds[index]);
                }
            }

            public struct UnsafeSloan
            {
                public NativeReference<Status> Status;
                public NativeArray<T2>.ReadOnly Positions;
                public NativeList<int> Triangles;
                public NativeList<int> Halfedges;
                public NativeList<bool> ConstrainedHalfedges;
                public NativeList<bool> IgnoredHalfedgesForPlantingSeeds;

                public NativeList<int> Intersections;
                public NativeList<int> UnresolvedIntersections;
                public NativeArray<int> PointToHalfedge;

                public Args Args;

                public void TryApplyConstraint(int2 c, bool ignoreForPlantingSeeds)
                {
                    Intersections.Clear();
                    UnresolvedIntersections.Clear();

                    CollectIntersections(c, ignoreForPlantingSeeds);

                    var iter = 0;
                    do
                    {
                        if (Status.Value != BurstTriangulator.Status.OK)
                        {
                            return;
                        }

                        (Intersections, UnresolvedIntersections) = (UnresolvedIntersections, Intersections);
                        TryResolveIntersections(c, ignoreForPlantingSeeds, ref iter);
                    } while (!UnresolvedIntersections.IsEmpty);
                }

                private void TryResolveIntersections(int2 c, bool ignoreForPlantingSeeds, ref int iter)
                {
                    for (int i = 0; i < Intersections.Length; i++)
                    {
                        if (IsMaxItersExceeded(iter++, Args.SloanMaxIters))
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

                        var h0 = Intersections[i];
                        var h1 = NextHalfedge(h0);
                        var h2 = NextHalfedge(h1);

                        var h3 = Halfedges[h0];
                        var h4 = NextHalfedge(h3);
                        var h5 = NextHalfedge(h4);

                        var _i = Triangles[h0];
                        var _j = Triangles[h1];
                        var _p = Triangles[h2];
                        var _q = Triangles[h5];

                        var (p0, p1, p2, p3) = (Positions[_i], Positions[_q], Positions[_j], Positions[_p]);
                        if (!IsConvexQuadrilateral(p0, p1, p2, p3))
                        {
                            UnresolvedIntersections.Add(h0);
                            continue;
                        }

                        // Swap edge (see figure above)
                        Triangles[h0] = _q;
                        Triangles[h3] = _p;
                        PointToHalfedge[_q] = h0;
                        PointToHalfedge[_p] = h3;
                        PointToHalfedge[_i] = h4;
                        PointToHalfedge[_j] = h1;
                        ReplaceHalfedge(h5, h0);
                        ReplaceHalfedge(h2, h3);
                        Halfedges[h2] = h5;
                        Halfedges[h5] = h2;
                        ConstrainedHalfedges[h2] = false;
                        ConstrainedHalfedges[h5] = false;
                        IgnoredHalfedgesForPlantingSeeds[h2] = false;
                        IgnoredHalfedgesForPlantingSeeds[h5] = false;

                        // Fix intersections
                        for (int j = i + 1; j < Intersections.Length; j++)
                        {
                            var tmp = Intersections[j];
                            Intersections[j] = tmp == h2 ? h3 : tmp == h5 ? h0 : tmp;
                        }
                        for (int j = 0; j < UnresolvedIntersections.Length; j++)
                        {
                            var tmp = UnresolvedIntersections[j];
                            UnresolvedIntersections[j] = tmp == h2 ? h3 : tmp == h5 ? h0 : tmp;
                        }

                        var swapped = math.int2(_p, _q);
                        if (math.all(c.xy == swapped.xy) || math.all(c.xy == swapped.yx))
                        {
                            ConstrainedHalfedges[h2] = true;
                            ConstrainedHalfedges[h5] = true;
                            IgnoredHalfedgesForPlantingSeeds[h2] = ignoreForPlantingSeeds;
                            IgnoredHalfedgesForPlantingSeeds[h5] = ignoreForPlantingSeeds;
                        }
                        if (EdgeEdgeIntersection(c, swapped))
                        {
                            UnresolvedIntersections.Add(h2);
                        }
                    }

                    Intersections.Clear();
                }

                /// <summary>
                /// Replaces <paramref name="h0"/> with <paramref name="h1"/>.
                /// </summary>
                private void ReplaceHalfedge(int h0, int h1)
                {
                    var h0p = Halfedges[h0];
                    Halfedges[h1] = h0p;
                    ConstrainedHalfedges[h1] = ConstrainedHalfedges[h0];
                    IgnoredHalfedgesForPlantingSeeds[h1] = IgnoredHalfedgesForPlantingSeeds[h0];

                    if (h0p != -1)
                    {
                        Halfedges[h0p] = h1;
                        ConstrainedHalfedges[h0p] = ConstrainedHalfedges[h0];
                        IgnoredHalfedgesForPlantingSeeds[h0p] = IgnoredHalfedgesForPlantingSeeds[h0];
                    }
                }

                private bool EdgeEdgeIntersection(int2 e1, int2 e2)
                {
                    var (a0, a1) = (Positions[e1.x], Positions[e1.y]);
                    var (b0, b1) = (Positions[e2.x], Positions[e2.y]);
                    return !(math.any(e1.xy == e2.xy | e1.xy == e2.yx)) && UnsafeTriangulator<T, T2, TBig, TTransform, TUtils>.EdgeEdgeIntersection(a0, a1, b0, b1);
                }

                private void CollectIntersections(int2 edge, bool ignoreForPlantingSeeds)
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
                    var h0init = PointToHalfedge[ci];
                    var h0 = h0init;
                    do
                    {
                        var h1 = NextHalfedge(h0);
                        if (Triangles[h1] == cj)
                        {
                            ConstrainedHalfedges[h0] = true;
                            IgnoredHalfedgesForPlantingSeeds[h0] = ignoreForPlantingSeeds;
                            var oh0 = Halfedges[h0];
                            if (oh0 != -1)
                            {
                                ConstrainedHalfedges[oh0] = true;
                                IgnoredHalfedgesForPlantingSeeds[oh0] = ignoreForPlantingSeeds;
                            }
                            break;
                        }
                        var h2 = NextHalfedge(h1);
                        if (EdgeEdgeIntersection(edge, new(Triangles[h1], Triangles[h2])))
                        {
                            UnresolvedIntersections.Add(h1);
                            tunnelInit = Halfedges[h1];
                            break;
                        }

                        h0 = Halfedges[h2];

                        // Boundary reached check other side
                        if (h0 == -1)
                        {
                            if (Triangles[h2] == cj)
                            {
                                ConstrainedHalfedges[h2] = true;
                                IgnoredHalfedgesForPlantingSeeds[h2] = ignoreForPlantingSeeds;
                            }

                            // possible that triangles[h2] == cj, not need to check
                            break;
                        }
                    } while (h0 != h0init);

                    h0 = Halfedges[h0init];
                    if (tunnelInit == -1 && h0 != -1)
                    {
                        h0 = NextHalfedge(h0);
                        // Same but reversed
                        do
                        {
                            var h1 = NextHalfedge(h0);
                            if (Triangles[h1] == cj)
                            {
                                ConstrainedHalfedges[h0] = true;
                                IgnoredHalfedgesForPlantingSeeds[h0] = ignoreForPlantingSeeds;
                                var oh0 = Halfedges[h0];
                                if (oh0 != -1)
                                {
                                    ConstrainedHalfedges[oh0] = true;
                                    IgnoredHalfedgesForPlantingSeeds[oh0] = ignoreForPlantingSeeds;
                                }
                                break;
                            }
                            var h2 = NextHalfedge(h1);
                            if (EdgeEdgeIntersection(edge, new(Triangles[h1], Triangles[h2])))
                            {
                                UnresolvedIntersections.Add(h1);
                                tunnelInit = Halfedges[h1];
                                break;
                            }

                            h0 = Halfedges[h0];
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

                        if (Triangles[h2p] == cj)
                        {
                            break;
                        }
                        else if (EdgeEdgeIntersection(edge, new(Triangles[h1p], Triangles[h2p])))
                        {
                            UnresolvedIntersections.Add(h1p);
                            tunnelInit = Halfedges[h1p];
                        }
                        else if (EdgeEdgeIntersection(edge, new(Triangles[h2p], Triangles[h0p])))
                        {
                            UnresolvedIntersections.Add(h2p);
                            tunnelInit = Halfedges[h2p];
                        }
                    }
                }

                private bool IsMaxItersExceeded(int iter, int maxIters)
                {
                    if (iter >= maxIters)
                    {
                        if (Args.Verbose)
                        {
                            Debug.LogError(
                                $"[Triangulator]: Sloan max iterations exceeded! This may suggest that input data is hard to resolve by Sloan's algorithm. " +
                                $"It usually happens when the scale of the input positions is not uniform. " +
                                $"Please try to post-process input data or increase {nameof(TriangulationSettings.SloanMaxIters)} value."
                            );
                        }
                        Status.Value |= BurstTriangulator.Status.ERR_SLOAN_ITERS_EXCEEDED;
                        return true;
                    }
                    return false;
                }
            }
        }

        private struct PlantingSeedStep
        {
            private NativeReference<Status> status;
            private NativeList<int> triangles;
            [ReadOnly]
            private NativeList<T2> positions;
            private NativeList<bool> constrainedHalfedges;
            private NativeList<int> halfedges;
            private NativeList<bool> ignoredHalfedges;
            private NativeArray<bool> visitedTriangles;
            private NativeQueueList<int> trianglesQueue;
            private NativeArray<T2> holes;

            private readonly Args args;
            private int tIdMinVisited;

            public PlantingSeedStep(NativeInputData<T2> input, NativeOutputData<T2> output, Args args) : this(output, args, input.HoleSeeds) { }

            public PlantingSeedStep(NativeOutputData<T2> output, Args args, NativeArray<T2> localHoles)
            {
                status = output.Status;
                triangles = output.Triangles;
                positions = output.Positions;
                constrainedHalfedges = output.ConstrainedHalfedges;
                halfedges = output.Halfedges;
                ignoredHalfedges = output.IgnoredHalfedgesForPlantingSeeds;
                holes = localHoles;
                this.args = args;

                visitedTriangles = default;
                trianglesQueue = default;

                tIdMinVisited = -1;
            }

            public void Execute(Allocator allocator, bool constraintsIsCreated)
            {
                if (!constraintsIsCreated || status.IsCreated && status.Value != Status.OK)
                {
                    return;
                }

                using var _ = Markers.PlantingSeedStep.Auto();

                using var _visitedTriangles = visitedTriangles = new(triangles.Length / 3, allocator);
                using var _trianglesQueue = trianglesQueue = new(allocator);

                if (args.AutoHolesAndBoundary) PlantAuto(allocator);
                if (holes.IsCreated) PlantHoleSeeds(holes);
                if (args.RestoreBoundary) PlantBoundarySeeds();

                RemoveVisitedTriangles();
            }

            private bool HalfedgeIsIgnored(int he) => ignoredHalfedges.IsCreated && ignoredHalfedges[he];

            private void PlantBoundarySeeds()
            {
                for (int he = 0; he < halfedges.Length; he++)
                {
                    if (halfedges[he] == -1 &&
                        !visitedTriangles[he / 3] &&
                        (!constrainedHalfedges[he] || HalfedgeIsIgnored(he)))
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

            private void RemoveVisitedTriangles()
            {
                static void DisableHe(NativeList<int> halfedges, int he, int rId)
                {
                    var ohe = halfedges[3 * rId + he];
                    if (ohe != -1)
                    {
                        halfedges[ohe] = -1;
                    }
                }

                static void AdaptHe(NativeList<int> halfedges, int he, int rId, int wId)
                {
                    var ohe = halfedges[3 * rId + he];
                    halfedges[3 * wId + he] = ohe;
                    if (ohe != -1)
                    {
                        halfedges[ohe] = 3 * wId + he;
                    }
                }

                if (tIdMinVisited == -1)
                {
                    return;
                }

                // Reinterpret to a larger struct to make copies of whole triangles slightly more efficient
                var constrainedHalfedges3 = constrainedHalfedges.AsArray().Reinterpret<bool3>(1);
                var triangles3 = triangles.AsArray().Reinterpret<int3>(4);

                var wId = tIdMinVisited;
                for (int rId = tIdMinVisited; rId < triangles3.Length; rId++)
                {
                    if (!visitedTriangles[rId])
                    {
                        triangles3[wId] = triangles3[rId];
                        constrainedHalfedges3[wId] = constrainedHalfedges3[rId];
                        AdaptHe(halfedges, 0, rId, wId);
                        AdaptHe(halfedges, 1, rId, wId);
                        AdaptHe(halfedges, 2, rId, wId);
                        wId++;
                    }
                    else
                    {
                        DisableHe(halfedges, 0, rId);
                        DisableHe(halfedges, 1, rId);
                        DisableHe(halfedges, 2, rId);
                    }
                }

                // Trim the data to reflect removed triangles.
                triangles.Length = 3 * wId;
                constrainedHalfedges.Length = 3 * wId;
                halfedges.Length = 3 * wId;
            }

            private void PlantSeed(int tId)
            {
                if (visitedTriangles[tId])
                {
                    return;
                }

                visitedTriangles[tId] = true;
                trianglesQueue.Enqueue(tId);
                tIdMinVisited = tIdMinVisited == -1 ? tId : math.min(tId, tIdMinVisited);

                // Search outwards from the seed triangle and mark all triangles
                // until we get to a constrained edge, or a previously visited triangle.
                while (trianglesQueue.TryDequeue(out tId))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var he = 3 * tId + i;
                        var ohe = halfedges[he];
                        if (constrainedHalfedges[he] && !HalfedgeIsIgnored(he) || ohe == -1)
                        {
                            continue;
                        }

                        var otherId = ohe / 3;
                        if (!visitedTriangles[otherId])
                        {
                            visitedTriangles[otherId] = true;
                            trianglesQueue.Enqueue(otherId);
                            tIdMinVisited = math.min(otherId, tIdMinVisited);
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
                    if (utils.PointInsideTriangle(p, a, b, c))
                    {
                        return tId;
                    }
                }

                return -1;
            }

            private void PlantAuto(Allocator allocator)
            {
                using var heQueue = new NativeQueueList<int>(allocator);
                using var loop = new NativeList<int>(allocator);
                var heVisited = new NativeArray<bool>(halfedges.Length, allocator);

                // Build boundary loop: 1st sweep
                for (int he = 0; he < halfedges.Length; he++)
                {
                    if (halfedges[he] != -1 || heVisited[he])
                    {
                        continue;
                    }

                    heVisited[he] = true;
                    if (constrainedHalfedges[he] && !HalfedgeIsIgnored(he))
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
                    if (constrainedHalfedges[ohe] && !HalfedgeIsIgnored(ohe))
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

                // Plant seeds for non visited constraint edges
                foreach (var h1 in loop.AsReadOnly())
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
                    if (constrainedHalfedges[ohe] && !HalfedgeIsIgnored(ohe))
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

                heVisited.Dispose();
            }
        }

        private struct RefineMeshStep
        {
            public readonly struct Circle
            {
                public readonly T2 Center;
                public readonly T RadiusSq;
                public Circle((T2 center, T radiusSq) circle) => (Center, RadiusSq) = (circle.center, circle.radiusSq);
            }

            private NativeReference<Status> status;
            private NativeList<int> triangles;
            private NativeList<T2> outputPositions;
            private NativeList<int> halfedges;
            private NativeList<bool> constrainedHalfedges;

            private NativeList<Circle> circles;
            private UnsafeBowerWatson bw;

            private readonly T maximumArea2, angleThreshold, shells;
            private readonly int initialPointsCount;

            public RefineMeshStep(NativeOutputData<T2> output, Args args, TTransform lt) : this(output,
                area2Threshold: utils.Cast(utils.mul(utils.Cast(utils.mul(utils.Const(2), utils.Const(args.RefinementThresholdArea))), lt.AreaScalingFactor)),
                angleThreshold: utils.Const(args.RefinementThresholdAngle),
                shells: utils.Const(args.ConcentricShellsParameter))
            { }

            public RefineMeshStep(NativeOutputData<T2> output, T area2Threshold, T angleThreshold, T shells)
            {
                status = output.Status;
                initialPointsCount = output.Positions.Length;
                maximumArea2 = area2Threshold;
                this.angleThreshold = angleThreshold;
                this.shells = shells;
                triangles = output.Triangles;
                outputPositions = output.Positions;
                halfedges = output.Halfedges;
                constrainedHalfedges = output.ConstrainedHalfedges;
                circles = default;
                bw = default;
            }

            public void Execute(Allocator allocator, bool refineMesh, bool constrainBoundary)
            {
                if (!refineMesh || status.IsCreated && status.Value != Status.OK)
                {
                    return;
                }

                using var _ = Markers.RefineMeshStep.Auto();

                if (!utils.SupportRefinement())
                {
                    Debug.LogError("Mesh refinement is not supported for this coordinate type.");
                    status.Value |= Status.ERR_REFINEMENT_UNSUPPORTED;
                    return;
                }

                if (constrainBoundary)
                {
                    for (int he = 0; he < constrainedHalfedges.Length; he++)
                    {
                        constrainedHalfedges[he] |= halfedges[he] == -1;
                    }
                }

                using var _circles = circles = new(allocator) { Length = triangles.Length / 3 };
                using var trianglesQueue = new NativeQueueList<int>(allocator);
                using var pathPoints = new NativeList<int>(allocator);
                using var pathHalfedges = new NativeList<int>(allocator);
                using var visitedTriangles = new NativeList<bool>(triangles.Length / 3, allocator);

                using var heQueue = new NativeQueueList<int>(triangles.Length, allocator);
                using var tQueue = new NativeQueueList<int>(triangles.Length, allocator);

                for (int tId = 0; tId < triangles.Length / 3; tId++)
                {
                    var (i, j, k) = (triangles[3 * tId + 0], triangles[3 * tId + 1], triangles[3 * tId + 2]);
                    circles[tId] = new(CalculateCircumCircle(i, j, k, outputPositions.AsArray()));
                }

                bw = new()
                {
                    Output = new()
                    {
                        Triangles = triangles,
                        Halfedges = halfedges,
                        Positions = outputPositions,
                        ConstrainedHalfedges = constrainedHalfedges,
                    },
                    Circles = circles,
                    TrianglesQueue = trianglesQueue,
                    PathHalfedges = pathHalfedges,
                    PathPoints = pathPoints,
                    VisitedTriangles = visitedTriangles,
                };

                // Collect encroached half-edges.
                for (int he = 0; he < constrainedHalfedges.Length; he++)
                {
                    if (constrainedHalfedges[he] && IsEncroached(he))
                    {
                        heQueue.Enqueue(he);
                    }
                }

                SplitEncroachedEdges(heQueue, tQueue: default); // ignore bad triangles in this run

                // Collect encroached triangles
                for (int tId = 0; tId < triangles.Length / 3; tId++)
                {
                    if (IsBadTriangle(tId))
                    {
                        tQueue.Enqueue(tId);
                    }
                }

                // Split triangles
                while (tQueue.TryDequeue(out var tId))
                {
                    if (tId != -1)
                    {
                        SplitTriangle(tId, heQueue, tQueue, allocator);
                    }
                }
            }

            private void SplitEncroachedEdges(NativeQueueList<int> heQueue, NativeQueueList<int> tQueue)
            {
                while (heQueue.TryDequeue(out var he))
                {
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

            private void SplitEdge(int he, NativeQueueList<int> heQueue, NativeQueueList<int> tQueue)
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
                    var alpha = utils.alpha(D: shells, dSquare: utils.Cast(utils.distancesq(e0, e1)));
                    // Swap points to provide symmetry in splitting
                    p = i < initialPointsCount ? utils.lerp(e0, e1, alpha) : utils.lerp(e1, e0, alpha);
                }

                constrainedHalfedges[he] = false;
                var ohe = halfedges[he];
                if (ohe != -1)
                {
                    constrainedHalfedges[ohe] = false;
                }

                if (halfedges[he] != -1)
                {
                    var cavityLength = bw.UnsafeInsertPointBulk(p, initTriangle: he / 3, heQueue, tQueue);
                    ProcessPathHalfedgesForEnqueueing(heQueue, tQueue, boundary: false, cavityLength);

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
                        heQueue.Enqueue(hi);
                    }
                    var ohi = halfedges[hi];
                    if (IsEncroached(ohi))
                    {
                        heQueue.Enqueue(ohi);
                    }
                    if (IsEncroached(hj))
                    {
                        heQueue.Enqueue(hj);
                    }
                    var ohj = halfedges[hj];
                    if (IsEncroached(ohj))
                    {
                        heQueue.Enqueue(ohj);
                    }

                    constrainedHalfedges[hi] = true;
                    constrainedHalfedges[ohi] = true;
                    constrainedHalfedges[hj] = true;
                    constrainedHalfedges[ohj] = true;
                }
                else
                {
                    var cavityLength = bw.UnsafeInsertPointBoundary(p, initHe: he, heQueue, tQueue);
                    ProcessPathHalfedgesForEnqueueing(heQueue, tQueue, boundary: true, cavityLength);

                    //var h0 = triangles.Length - 3;
                    var id = 3 * (cavityLength - 1);
                    var hi = halfedges.Length - 1;
                    var hj = halfedges.Length - id;

                    if (IsEncroached(hi))
                    {
                        heQueue.Enqueue(hi);
                    }

                    if (IsEncroached(hj))
                    {
                        heQueue.Enqueue(hj);
                    }

                    constrainedHalfedges[hi] = true;
                    constrainedHalfedges[hj] = true;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool IsBadTriangle(int tId)
            {
                var (i, j, k) = (triangles[3 * tId + 0], triangles[3 * tId + 1], triangles[3 * tId + 2]);
                var (a, b, c) = (outputPositions[i], outputPositions[j], outputPositions[k]);
                var area2 = Area2(a, b, c);
                return utils.greater(area2, maximumArea2) || AngleIsTooSmall(tId, angleThreshold);
            }

            private void SplitTriangle(int tId, NativeQueueList<int> heQueue, NativeQueueList<int> tQueue, Allocator allocator)
            {
                var c = circles[tId];
                var edges = new NativeList<int>(allocator);

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
                    var cavityLength = bw.UnsafeInsertPointBulk(c.Center, initTriangle: tId, heQueue, tQueue);
                    ProcessPathHalfedgesForEnqueueing(heQueue, tQueue, boundary: false, cavityLength);
                }
                else
                {
                    var (i, j, k) = (triangles[3 * tId + 0], triangles[3 * tId + 1], triangles[3 * tId + 2]);
                    var (xi, xj, xk) = (outputPositions[i], outputPositions[j], outputPositions[k]);
                    var area2 = Area2(xi, xj, xk);
                    if (utils.greater(area2, maximumArea2)) // TODO split permited
                    {
                        foreach (var he in edges.AsReadOnly())
                        {
                            heQueue.Enqueue(he);
                        }
                    }
                    if (!heQueue.IsEmpty())
                    {
                        tQueue.Enqueue(tId);
                        SplitEncroachedEdges(heQueue, tQueue);
                    }
                }

                edges.Dispose();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool AngleIsTooSmall(int tId, T minimumAngle)
            {
                var (i, j, k) = (triangles[3 * tId + 0], triangles[3 * tId + 1], triangles[3 * tId + 2]);
                var (pA, pB, pC) = (outputPositions[i], outputPositions[j], outputPositions[k]);
                return UnsafeTriangulator<T, T2, TBig, TTransform, TUtils>.AngleIsTooSmall(pA, pB, pC, minimumAngle);
            }

            public struct UnsafeBowerWatson
            {
                public NativeOutputData<T2> Output;
                public NativeList<Circle> Circles;

                public NativeQueueList<int> TrianglesQueue;
                public NativeList<int> PathPoints;
                public NativeList<int> PathHalfedges;
                public NativeList<bool> VisitedTriangles;

                /// <summary>
                /// Used to find minimal triangle id which is visited.
                /// </summary>
                private int tIdMinVisited;
                /// <summary>
                /// Used to find cavity boundary halfedge.
                /// </summary>
                private int heLoopId;

                public int UnsafeInsertPointBulk(T2 p, int initTriangle, NativeQueueList<int> heQueue = default, NativeQueueList<int> tQueue = default) => UnsafeInsertPoint(p, initTriangle, initHe: -1, boundary: false, heQueue, tQueue);

                public int UnsafeInsertPointBoundary(T2 p, int initHe, NativeQueueList<int> heQueue = default, NativeQueueList<int> tQueue = default) => UnsafeInsertPoint(p, initTriangle: -1, initHe, boundary: true, heQueue, tQueue);

                private int UnsafeInsertPoint(T2 p, int initTriangle, int initHe, bool boundary, NativeQueueList<int> heQueue = default, NativeQueueList<int> tQueue = default)
                {
                    var pId = Output.Positions.Length;
                    Output.Positions.Add(p);
                    ClearTmpBuffers();
                    RecalculateBadTriangles(p, initTriangle: boundary ? initHe / 3 : initTriangle);
                    BuildPolygon(initHe: boundary ? initHe : heLoopId, boundary);
                    ProcessBadTriangles(heQueue, tQueue);
                    BuildNewTriangles(pId, boundary);
                    return PathPoints.Length;
                }

                private void ClearTmpBuffers()
                {
                    TrianglesQueue.Clear();
                    PathPoints.Clear();
                    PathHalfedges.Clear();
                    VisitedTriangles.Clear();
                    VisitedTriangles.Length = Output.Triangles.Length / 3;
                }

                private void RecalculateBadTriangles(T2 p, int initTriangle)
                {
                    var triangles = Output.Triangles;
                    heLoopId = -1;
                    TrianglesQueue.Enqueue(initTriangle);
                    VisitedTriangles[initTriangle] = true;
                    tIdMinVisited = initTriangle;

                    while (TrianglesQueue.TryDequeue(out var tId))
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            var he = Output.Halfedges[3 * tId + i];
                            var otherId = he / 3;

                            if (he != -1 && VisitedTriangles[otherId])
                            {
                                continue;
                            }

                            if (he == -1 || Output.ConstrainedHalfedges[he])
                            {
                                heLoopId = heLoopId == -1 ? 3 * tId + i : heLoopId;
                                continue;
                            }

                            var circle = Circles.IsCreated ? Circles[otherId] : new(CalculateCircumCircle(triangles[3 * otherId + 0], triangles[3 * otherId + 1], triangles[3 * otherId + 2], Output.Positions.AsArray()));
                            if (utils.le(utils.Cast(utils.distancesq(circle.Center, p)), circle.RadiusSq))
                            {
                                TrianglesQueue.Enqueue(otherId);
                                VisitedTriangles[otherId] = true;
                                tIdMinVisited = math.min(tIdMinVisited, otherId);
                            }
                            else
                            {
                                heLoopId = heLoopId == -1 ? 3 * tId + i : heLoopId;
                            }
                        }
                    }
                }

                private void BuildPolygon(int initHe, bool boundary)
                {
                    var triangles = Output.Triangles;
                    var halfedges = Output.Halfedges;

                    if (!boundary)
                    {
                        PathPoints.Add(triangles[initHe]);
                        PathHalfedges.Add(halfedges[initHe]);
                    }

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
                        if (he == -1 || !VisitedTriangles[he / 3])
                        {
                            PathPoints.Add(triangles[id]);
                            PathHalfedges.Add(he);
                            continue;
                        }
                        id = he;
                    }

                    if (boundary)
                    {
                        PathPoints.Add(triangles[initHe]);
                        PathHalfedges.Add(-1);
                    }
                }

                private void ProcessBadTriangles(NativeQueueList<int> heQueue, NativeQueueList<int> tQueue)
                {
                    static void DisableHe(NativeList<int> halfedges, int he, int rId)
                    {
                        var ohe = halfedges[3 * rId + he];
                        if (ohe != -1)
                        {
                            halfedges[ohe] = -1;
                        }
                    }

                    static void AdaptHe(NativeList<int> halfedges, int he, int rId, int wId)
                    {
                        var ohe = halfedges[3 * rId + he];
                        halfedges[3 * wId + he] = ohe;
                        if (ohe != -1)
                        {
                            halfedges[ohe] = 3 * wId + he;
                        }
                    }

                    var triangles = Output.Triangles;
                    var halfedges = Output.Halfedges;
                    var constrainedHalfedges = Output.ConstrainedHalfedges;

                    // Reinterpret to a larger struct to make copies of whole triangles slightly more efficient
                    var constrainedHalfedges3 = constrainedHalfedges.AsArray().Reinterpret<bool3>(1);
                    var triangles3 = triangles.AsArray().Reinterpret<int3>(4);

                    var wId = tIdMinVisited;
                    for (int rId = tIdMinVisited; rId < triangles3.Length; rId++)
                    {
                        if (!VisitedTriangles[rId])
                        {
                            triangles3[wId] = triangles3[rId];
                            constrainedHalfedges3[wId] = constrainedHalfedges3[rId];
                            AdaptHe(halfedges, 0, rId, wId);
                            AdaptHe(halfedges, 1, rId, wId);
                            AdaptHe(halfedges, 2, rId, wId);
                            if (Circles.IsCreated)
                            {
                                Circles[wId] = Circles[rId];
                            }
                            wId++;
                        }
                        else
                        {
                            DisableHe(halfedges, 0, rId);
                            DisableHe(halfedges, 1, rId);
                            DisableHe(halfedges, 2, rId);

                            for (int i = 0; i < PathHalfedges.Length; i++)
                            {
                                if (PathHalfedges[i] > 3 * wId + 2)
                                {
                                    PathHalfedges[i] -= 3;
                                }
                            }

                            AdaptQueues(wId, heQueue, tQueue);
                        }
                    }

                    // Trim the data to reflect removed triangles.
                    triangles.Length = 3 * wId;
                    constrainedHalfedges.Length = 3 * wId;
                    halfedges.Length = 3 * wId;
                    if (Circles.IsCreated)
                    {
                        Circles.Length = wId;
                    }
                }

                private void BuildNewTriangles(int pId, bool boundary)
                {
                    // Note: In this method, we create new triangles with the inserted point `pId`.
                    //       There are two possible topologies to consider, depending on whether
                    //       the point is inserted on the boundary or not.
                    //
                    //       - **Star topology**. The inserted point (pId) lies entirely within the region,
                    //         forming new triangles radiating outward from it. All new triangles share pId
                    //         as a common vertex, resembling a star. Example:
                    //
                    //              ..................
                    //           ..' :.      ....''':
                    //        .'''    :...'''     :'
                    //        :.......* pId      :'
                    //        :.     .''..      .:
                    //         :.  .'     ''..  :
                    //          :.'...........:.'
                    //
                    //       - **Amphitheater topology**. The inserted point (pId) lies on the boundary,
                    //         creating new triangles that connect to the cavity edges. Example:
                    //
                    //             ....
                    //            .':  ''''''....
                    //           :'  :        ..:'':
                    //         .:     : ...'''     ''..
                    //       .:........*...............:.
                    //                    pId
                    //
                    var triangles = Output.Triangles;
                    var halfedges = Output.Halfedges;
                    var constrainedHalfedges = Output.ConstrainedHalfedges;

                    var createdTrianglesCount = boundary ? PathPoints.Length - 1 : PathPoints.Length;

                    // Build triangles/circles for inserted point pId.
                    var initTriangles = triangles.Length;
                    triangles.Length += 3 * createdTrianglesCount;
                    if (Circles.IsCreated)
                    {
                        Circles.Length += createdTrianglesCount;
                    }
                    for (int i = 0; i < PathPoints.Length - 1; i++)
                    {
                        triangles[initTriangles + 3 * i + 0] = pId;
                        triangles[initTriangles + 3 * i + 1] = PathPoints[i];
                        triangles[initTriangles + 3 * i + 2] = PathPoints[i + 1];
                        if (Circles.IsCreated)
                        {
                            Circles[initTriangles / 3 + i] = new(CalculateCircumCircle(pId, PathPoints[i], PathPoints[i + 1], Output.Positions.AsArray()));
                        }
                    }
                    if (!boundary)
                    {
                        triangles[^3] = pId;
                        triangles[^2] = PathPoints[^1];
                        triangles[^1] = PathPoints[0];
                        if (Circles.IsCreated)
                        {
                            Circles[^1] = new(CalculateCircumCircle(pId, PathPoints[^1], PathPoints[0], Output.Positions.AsArray()));
                        }
                    }

                    // Build half-edges for inserted point pId.
                    var heOffset = halfedges.Length;
                    halfedges.Length += 3 * createdTrianglesCount;
                    constrainedHalfedges.Length += 3 * createdTrianglesCount;
                    for (int i = 0; i < createdTrianglesCount - 1; i++)
                    {
                        var he = PathHalfedges[i];
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
                    var phe = boundary ? PathHalfedges[^2] : PathHalfedges[^1];
                    halfedges[heOffset + 3 * (createdTrianglesCount - 1) + 1] = phe;
                    if (phe != -1)
                    {
                        halfedges[phe] = heOffset + 3 * (createdTrianglesCount - 1) + 1;
                        constrainedHalfedges[heOffset + 3 * (createdTrianglesCount - 1) + 1] = constrainedHalfedges[phe];
                    }
                    else
                    {
                        constrainedHalfedges[heOffset + 3 * (createdTrianglesCount - 1) + 1] = true;
                    }
                    halfedges[heOffset] = boundary ? -1 : heOffset + 3 * (createdTrianglesCount - 1) + 2;
                    halfedges[heOffset + 3 * (createdTrianglesCount - 1) + 2] = boundary ? -1 : heOffset;
                }

                private readonly void AdaptQueues(int wId, NativeQueueList<int> heQueue, NativeQueueList<int> tQueue)
                {
                    // NOTE: we use write index `wId`, since queues will be changed during adaptation.
                    if (heQueue.IsCreated)
                    {
                        var heSpan = heQueue.AsSpan();
                        for (int i = 0; i < heSpan.Length; i++)
                        {
                            var he = heSpan[i];
                            if (he / 3 == wId)
                            {
                                heSpan[i] = -1;
                                continue;
                            }

                            if (he > 3 * wId + 2)
                            {
                                heSpan[i] -= 3;
                            }
                        }
                    }

                    if (tQueue.IsCreated)
                    {
                        var tSpan = tQueue.AsSpan();
                        for (int i = 0; i < tSpan.Length; i++)
                        {
                            var q = tSpan[i];
                            if (q == wId)
                            {
                                tSpan[i] = -1;
                                continue;
                            }

                            if (q > wId)
                            {
                                tSpan[i]--;
                            }
                        }
                    }
                }
            }

            private void ProcessPathHalfedgesForEnqueueing(NativeQueueList<int> heQueue, NativeQueueList<int> tQueue, bool boundary, int cavityLength)
            {
                var iters = boundary ? cavityLength - 1 : cavityLength;
                var heOffset = halfedges.Length - 3 * iters;

                if (heQueue.IsCreated)
                {
                    for (int i = 0; i < iters; i++)
                    {
                        var he = heOffset + 3 * i + 1;
                        if (constrainedHalfedges[he] && IsEncroached(he))
                        {
                            heQueue.Enqueue(he);
                        }
                        else if (tQueue.IsCreated && IsBadTriangle(he / 3))
                        {
                            tQueue.Enqueue(he / 3);
                        }
                    }
                }
            }
        }

        internal static bool AngleIsTooSmall(T2 pA, T2 pB, T2 pC, T minimumAngle)
        {
            // Implementation is based in dot product property:
            //    a·b = |a| |b| cos α
            var threshold = utils.cos(minimumAngle);

            var pAB = utils.normalizesafe(utils.diff(pB, pA));
            var pBC = utils.normalizesafe(utils.diff(pC, pB));
            var pCA = utils.normalizesafe(utils.diff(pA, pC));

            return utils.anygreaterthan(
                utils.dot(pAB, utils.neg(pCA)),
                utils.dot(pBC, utils.neg(pAB)),
                utils.dot(pCA, utils.neg(pBC)),
                threshold
            );
        }
        internal static T Area2(T2 a, T2 b, T2 c) => utils.abs(Cross(utils.diff(b, a), utils.diff(c, a)));
        private static T Cross(T2 a, T2 b) => utils.Cast(utils.diff(utils.mul(utils.X(a), utils.Y(b)), utils.mul(utils.Y(a), utils.X(b))));
        private static TBig CircumRadiusSq(T2 a, T2 b, T2 c) => utils.distancesq(utils.CircumCenter(a, b, c), a);
        private static (T2, T) CalculateCircumCircle(int i, int j, int k, NativeArray<T2> positions)
        {
            var (pA, pB, pC) = (positions[i], positions[j], positions[k]);
            return (utils.CircumCenter(pA, pB, pC), utils.Cast(CircumRadiusSq(pA, pB, pC)));
        }
        private static bool ccw(T2 a, T2 b, T2 c) => utils.greater(
            utils.mul(utils.diff(utils.Y(c), utils.Y(a)), utils.diff(utils.X(b), utils.X(a))),
            utils.mul(utils.diff(utils.Y(b), utils.Y(a)), utils.diff(utils.X(c), utils.X(a)))
        );
        /// <summary>
        /// Returns <see langword="true"/> if edge (<paramref name="a0"/>, <paramref name="a1"/>) intersects 
        /// (<paramref name="b0"/>, <paramref name="b1"/>), <see langword="false"/> otherwise.
        /// </summary>
        /// <remarks>
        /// This method will not catch intersecting collinear segments. See unit tests for more details.
        /// Segments intersecting only at their endpoints may or may not return <see langword="true"/>, depending on their orientation.
        /// </remarks>
        internal static bool EdgeEdgeIntersection(T2 a0, T2 a1, T2 b0, T2 b1) => ccw(a0, a1, b0) != ccw(a0, a1, b1) && ccw(b0, b1, a0) != ccw(b0, b1, a1);
        internal static bool IsConvexQuadrilateral(T2 a, T2 b, T2 c, T2 d) => true
            && utils.greater(utils.abs(Orient2dFast(a, c, b)), utils.EPSILON())
            && utils.greater(utils.abs(Orient2dFast(a, c, d)), utils.EPSILON())
            && utils.greater(utils.abs(Orient2dFast(b, d, a)), utils.EPSILON())
            && utils.greater(utils.abs(Orient2dFast(b, d, c)), utils.EPSILON())
            && EdgeEdgeIntersection(a, c, b, d)
        ;
        private static TBig Orient2dFast(T2 a, T2 b, T2 c) => utils.diff(
            utils.mul(utils.diff(utils.Y(a), utils.Y(c)), utils.diff(utils.X(b), utils.X(c))),
            utils.mul(utils.diff(utils.X(a), utils.X(c)), utils.diff(utils.Y(b), utils.Y(c)))
        );
        internal static bool PointLineSegmentIntersection(T2 a, T2 b0, T2 b1) => true
            && utils.le(utils.abs(Orient2dFast(a, b0, b1)), utils.EPSILON())
            && math.all(utils.ge(a, utils.min(b0, b1)) & utils.le(a, utils.max(b0, b1)));
    }

    /// <summary>
    /// A signed 128-bit integer.
    /// </summary>
    internal readonly struct I128
    {
        public readonly bool IsNegative => (hi & 0x8000000000000000UL) != 0;

        private readonly ulong hi, lo;

        public I128(ulong hi, ulong lo) => (this.hi, this.lo) = (hi, lo);

        public static I128 operator +(I128 a, I128 b)
        {
            var lo = a.lo + b.lo;
            var hi = a.hi + b.hi + (lo < a.lo ? 1UL : 0);
            return new(hi, lo);
        }

        public static I128 operator -(I128 a, I128 b)
        {
            var lo = a.lo - b.lo;
            var hi = a.hi - b.hi - (lo > a.lo ? 1UL : 0);
            return new(hi, lo);
        }

        public static I128 operator -(I128 a) => new I128(~a.hi, ~a.lo) + new I128(0, 1);

        /// <summary>
        /// Multiplies two 64-bit signed integers, without any possibility of overflow.
        /// </summary>
        public static I128 Multiply(long slhs, long srhs)
        {
            // From https://stackoverflow.com/a/58381061
            var negative = (slhs < 0) ^ (srhs < 0);
            ulong lhs = (ulong)math.abs(slhs);
            ulong rhs = (ulong)math.abs(srhs);

            // First calculate all of the cross products.
            ulong lo_lo = (lhs & 0xFFFFFFFFUL) * (rhs & 0xFFFFFFFFUL);
            ulong hi_lo = (lhs >> 32) * (rhs & 0xFFFFFFFFUL);
            ulong lo_hi = (lhs & 0xFFFFFFFFUL) * (rhs >> 32);
            ulong hi_hi = (lhs >> 32) * (rhs >> 32);

            // Now add the products together. These will never overflow.
            ulong cross = (lo_lo >> 32) + (hi_lo & 0xFFFFFFFFUL) + lo_hi;
            ulong upper = (hi_lo >> 32) + (cross >> 32) + hi_hi;

            var res = new I128(upper, (cross << 32) | (lo_lo & 0xFFFFFFFFUL));
            res = negative ? -res : res;
            return res;
        }
    }

    internal interface ITransform<TSelf, T, T2> where T : unmanaged where T2 : unmanaged
    {
        T AreaScalingFactor { get; }
        TSelf Identity { get; }
        TSelf Inverse();
        T2 Transform(T2 point);
        /// <summary>
        /// Returns PCA transformation of given <paramref name="positions"/>.
        /// Read more in the project manual.
        /// </summary>
        TSelf CalculatePCATransformation(NativeArray<T2> positions);
        /// <summary>
        /// Returns COM transformation of given <paramref name="positions"/>.
        /// Read more in the project manual about method restrictions for given type.
        /// </summary>
        TSelf CalculateLocalTransformation(NativeArray<T2> positions);
    }

    internal readonly struct TransformFloat : ITransform<TransformFloat, float, float2>
    {
        public readonly TransformFloat Identity => new(float2x2.identity, float2.zero);
        public readonly float AreaScalingFactor => math.abs(math.determinant(rotScale));

        private readonly float2x2 rotScale;
        private readonly float2 translation;

        public TransformFloat(float2x2 rotScale, float2 translation) => (this.rotScale, this.translation) = (rotScale, translation);
        private static TransformFloat Translate(float2 offset) => new(float2x2.identity, offset);
        private static TransformFloat Scale(float2 scale) => new(new float2x2(scale.x, 0, 0, scale.y), float2.zero);
        private static TransformFloat Rotate(float2x2 rotation) => new(rotation, float2.zero);
        public static TransformFloat operator *(TransformFloat lhs, TransformFloat rhs) => new(
            math.mul(lhs.rotScale, rhs.rotScale),
            math.mul(math.inverse(rhs.rotScale), lhs.translation) + rhs.translation
        );

        public TransformFloat Inverse() => new(math.inverse(rotScale), math.mul(rotScale, -translation));
        public float2 Transform(float2 point) => math.mul(rotScale, point + translation);

        public readonly TransformFloat CalculatePCATransformation(NativeArray<float2> positions)
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

        public readonly TransformFloat CalculateLocalTransformation(NativeArray<float2> positions)
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

        /// <summary>
        /// Solves <see href="https://en.wikipedia.org/wiki/Eigenvalues_and_eigenvectors">eigen problem</see> of the given <paramref name="matrix"/>.
        /// </summary>
        /// <param name="eigval">Eigen values.</param>
        /// <param name="eigvec">Eigen vectors.</param>
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

        /// <summary>
        /// Returns <see href="https://en.wikipedia.org/wiki/Kronecker_product">Kronecer product</see> of <paramref name="a"/> and <paramref name="b"/>.
        /// </summary>
        private static float2x2 Kron(float2 a, float2 b) => math.float2x2(a * b[0], a * b[1]);
    }

    internal readonly struct TransformDouble : ITransform<TransformDouble, double, double2>
    {
        public readonly TransformDouble Identity => new(double2x2.identity, double2.zero);
        public readonly double AreaScalingFactor => math.abs(math.determinant(rotScale));

        private readonly double2x2 rotScale;
        private readonly double2 translation;

        public TransformDouble(double2x2 rotScale, double2 translation) => (this.rotScale, this.translation) = (rotScale, translation);
        private static TransformDouble Translate(double2 offset) => new(double2x2.identity, offset);
        private static TransformDouble Scale(double2 scale) => new(new double2x2(scale.x, 0, 0, scale.y), double2.zero);
        private static TransformDouble Rotate(double2x2 rotation) => new(rotation, double2.zero);
        public static TransformDouble operator *(TransformDouble lhs, TransformDouble rhs) => new(
            math.mul(lhs.rotScale, rhs.rotScale),
            math.mul(math.inverse(rhs.rotScale), lhs.translation) + rhs.translation
        );

        public TransformDouble Inverse() => new(math.inverse(rotScale), math.mul(rotScale, -translation));
        public double2 Transform(double2 point) => math.mul(rotScale, point + translation);

        public readonly TransformDouble CalculatePCATransformation(NativeArray<double2> positions)
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

        public readonly TransformDouble CalculateLocalTransformation(NativeArray<double2> positions)
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

        /// <summary>
        /// Solves <see href="https://en.wikipedia.org/wiki/Eigenvalues_and_eigenvectors">eigen problem</see> of the given <paramref name="matrix"/>.
        /// </summary>
        /// <param name="eigval">Eigen values.</param>
        /// <param name="eigvec">Eigen vectors.</param>
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

        /// <summary>
        /// Returns <see href="https://en.wikipedia.org/wiki/Kronecker_product">Kronecer product</see> of <paramref name="a"/> and <paramref name="b"/>.
        /// </summary>
        private static double2x2 Kron(double2 a, double2 b) => math.double2x2(a * b[0], a * b[1]);
    }

    /// <summary>
    /// <b>Note:</b> translation transformation is only supported for type <see cref="int2"/>.
    /// </summary>
    internal readonly struct TransformInt : ITransform<TransformInt, int, int2>
    {
        public readonly TransformInt Identity => new(int2.zero);
        public readonly int AreaScalingFactor => 1;
        private readonly int2 translation;
        public TransformInt(int2 translation) => this.translation = translation;
        public TransformInt Inverse() => new(-translation);
        public int2 Transform(int2 point) => point + translation;
        public readonly TransformInt CalculatePCATransformation(NativeArray<int2> positions) => throw new NotImplementedException(
            "PCA is not implemented for int2 coordinates!"
        );

        public readonly TransformInt CalculateLocalTransformation(NativeArray<int2> positions)
        {
            int2 min = int.MaxValue, max = int.MinValue, com = 0;
            foreach (var p in positions)
            {
                min = math.min(p, min);
                max = math.max(p, max);
                com += p;
            }

            return new(-com / positions.Length);
        }
    }

#if UNITY_MATHEMATICS_FIXEDPOINT
    internal readonly struct TransformFp : ITransform<TransformFp, fp, fp2>
    {
        // NOTE: fpmath misses determinant and inverse functions.
        private static fp det(fp2x2 m) => m[0][0] * m[1][1] - m[0][1] * m[1][0];
        private static fp2x2 inv(fp2x2 m) => fpmath.fp2x2(m[1][1], -m[1][0], -m[0][1], m[0][0]) / det(m);
        public readonly TransformFp Identity => new(fp2x2.identity, fp2.zero);
        public readonly fp AreaScalingFactor => fpmath.abs(det(rotScale));

        private readonly fp2x2 rotScale;
        private readonly fp2 translation;

        public TransformFp(fp2x2 rotScale, fp2 translation) => (this.rotScale, this.translation) = (rotScale, translation);
        private static TransformFp Translate(fp2 offset) => new(fp2x2.identity, offset);
        private static TransformFp Scale(fp2 scale) => new(new fp2x2(scale.x, 0, 0, scale.y), fp2.zero);
        private static TransformFp Rotate(fp2x2 rotation) => new(rotation, fp2.zero);
        public static TransformFp operator *(TransformFp lhs, TransformFp rhs) => new(
            fpmath.mul(lhs.rotScale, rhs.rotScale),
            fpmath.mul(inv(rhs.rotScale), lhs.translation) + rhs.translation
        );

        public TransformFp Inverse() => new(inv(rotScale), fpmath.mul(rotScale, -translation));
        public fp2 Transform(fp2 point) => fpmath.mul(rotScale, point + translation);

        public readonly TransformFp CalculatePCATransformation(NativeArray<fp2> positions)
        {
            var com = (fp2)0;
            foreach (var p in positions)
            {
                com += p;
            }
            com /= positions.Length;

            var cov = fp2x2.zero;
            for (int i = 0; i < positions.Length; i++)
            {
                var q = positions[i] - com;
                cov += Kron(q, q);
            }
            cov /= positions.Length;

            Eigen(cov, out _, out var rotationMatrix);

            var partialTransform = Rotate(fpmath.transpose(rotationMatrix)) * Translate(-com);
            fp2 min = fp.max_value;
            fp2 max = fp.min_value;
            for (int i = 0; i < positions.Length; i++)
            {
                var p = partialTransform.Transform(positions[i]);
                min = fpmath.min(p, min);
                max = fpmath.max(p, max);
            }

            var c = (min + max) / 2;
            var s = (fp)2L / (max - min);

            return Scale(s) * Translate(-c) * partialTransform;
        }

        public readonly TransformFp CalculateLocalTransformation(NativeArray<fp2> positions)
        {
            fp2 min = fp.max_value, max = fp.min_value, com = fp2.zero;
            foreach (var p in positions)
            {
                min = fpmath.min(p, min);
                max = fpmath.max(p, max);
                com += p;
            }

            com /= positions.Length;
            var scale = 1 / fpmath.cmax(fpmath.max(fpmath.abs(max - com), fpmath.abs(min - com)));
            return Scale(scale) * Translate(-com);
        }

        /// <summary>
        /// Solves <see href="https://en.wikipedia.org/wiki/Eigenvalues_and_eigenvectors">eigen problem</see> of the given <paramref name="matrix"/>.
        /// </summary>
        /// <param name="eigval">Eigen values.</param>
        /// <param name="eigvec">Eigen vectors.</param>
        private static void Eigen(fp2x2 matrix, out fp2 eigval, out fp2x2 eigvec)
        {
            var a00 = matrix[0][0];
            var a11 = matrix[1][1];
            var a01 = matrix[0][1];

            var a00a11 = a00 - a11;
            var p1 = a00 + a11;
            var p2 = (a00a11 >= 0 ? 1 : -1) * fpmath.sqrt(a00a11 * a00a11 + 4 * a01 * a01);
            var lambda1 = p1 + p2;
            var lambda2 = p1 - p2;
            eigval = fpmath.fp2(lambda1, lambda2) / 2;

            var phi = fpmath.atan2(2 * a01, a00a11) / 2;

            eigvec = fpmath.fp2x2
            (
                m00: fpmath.cos(phi), m01: -fpmath.sin(phi),
                m10: fpmath.sin(phi), m11: fpmath.cos(phi)
            );
        }

        /// <summary>
        /// Returns <see href="https://en.wikipedia.org/wiki/Kronecker_product">Kronecer product</see> of <paramref name="a"/> and <paramref name="b"/>.
        /// </summary>
        private static fp2x2 Kron(fp2 a, fp2 b) => fpmath.fp2x2(a * b[0], a * b[1]);
    }
#endif

    /// <typeparam name="T">The raw coordinate type for a single axis. For example <see cref="float"/> or <see cref="int"/>.</typeparam>
    /// <typeparam name="T2">The 2D coordinate composed of Ts. For example <see cref="float2"/>.</typeparam>
    /// <typeparam name="TBig">A value that may have higher precision compared to <typeparamref name="T"/>. Used for squared distances and other products.</typeparam>
    internal interface IUtils<T, T2, TBig> where T : unmanaged where T2 : unmanaged where TBig : unmanaged
    {
        /// <summary>
        /// Cast a float to <typeparamref name="T"/>. Note that for integer coordinates, this will be floored.
        /// <b>Warning!</b> This operation may cause precision loss, use with caution.
        /// </summary>
        T Cast(TBig v);
        T2 CircumCenter(T2 a, T2 b, T2 c);
        T Const(float v);
        TBig EPSILON();
        bool InCircle(T2 a, T2 b, T2 c, T2 p);
        TBig MaxValue();
        T2 MaxValue2();
        T2 MinValue2();
        bool PointInsideTriangle(T2 p, T2 a, T2 b, T2 c);
        bool SupportRefinement();
        T X(T2 v);
        T Y(T2 v);
        T Zero();
        TBig ZeroTBig();
#pragma warning disable IDE1006
        T abs(T v);
        TBig abs(TBig v);
        /// <summary>
        /// Returns concentric shells segment splitting factor.
        /// </summary>
        /// <param name="D">Concentric shells parameter constant.</param>
        /// <param name="dSquare">Segment length squared.</param>
        /// <returns><i>alpha</i> in [0, 1] range.</returns>
        /// <remarks>
        /// Learn more in the paper:
        /// <see href="https://doi.org/10.1006/jagm.1995.1021">
        /// J. Ruppert. "A Delaunay Refinement Algorithm for Quality 2-Dimensional Mesh Generation". <i>J. Algorithms</i> <b>18</b>(3):548-585 (1995)
        /// </see>.
        /// </remarks>
        T alpha(T D, T dSquare);
        bool anygreaterthan(T a, T b, T c, T v);
        T2 avg(T2 a, T2 b);
        T cos(T v);
        T diff(T a, T b);
        TBig diff(TBig a, TBig b);
        T2 diff(T2 a, T2 b);
        TBig distancesq(T2 a, T2 b);
        T dot(T2 a, T2 b);
        bool2 eq(T2 v, T2 w);
        bool2 ge(T2 a, T2 b);
        bool greater(T a, T b);
        bool greater(TBig a, TBig b);
        int hashkey(T2 p, T2 c, int hashSize);
        bool2 isfinite(T2 v);
        bool le(T a, T b);
        bool le(TBig a, TBig b);
        bool2 le(T2 a, T2 b);
        T2 lerp(T2 a, T2 b, T v);
        bool less(TBig a, TBig b);
        T2 max(T2 v, T2 w);
        T2 min(T2 v, T2 w);
        TBig mul(T a, T b);
        T2 neg(T2 v);
        T2 normalizesafe(T2 v);
#pragma warning restore IDE1006
    }

    internal readonly struct UtilsFloat : IUtils<float, float2, float>
    {
        public readonly float Cast(float v) => v;
        public readonly float2 CircumCenter(float2 a, float2 b, float2 c)
        {
            var d = b - a;
            var e = c - a;

            var bl = math.lengthsq(d);
            var cl = math.lengthsq(e);

            var d2 = 0.5f / (d.x * e.y - d.y * e.x);

            return a + d2 * (bl * math.float2(e.y, -e.x) + cl * math.float2(-d.y, d.x));
        }
        public readonly float Const(float v) => v;
        public readonly float EPSILON() => math.EPSILON;
        public readonly bool InCircle(float2 a, float2 b, float2 c, float2 p)
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

            return dx * (ey * cp - bp * fy) - dy * (ex * cp - bp * fx) + ap * (ex * fy - ey * fx) < 0;
        }
        public readonly float MaxValue() => float.MaxValue;
        public readonly float2 MaxValue2() => float.MaxValue;
        public readonly float2 MinValue2() => float.MinValue;
        public readonly bool PointInsideTriangle(float2 p, float2 a, float2 b, float2 c)
        {
            static float cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;
            static float3 bar(float2 a, float2 b, float2 c, float2 p)
            {
                var (v0, v1, v2) = (b - a, c - a, p - a);
                var denInv = 1 / cross(v0, v1);
                var v = denInv * cross(v2, v1);
                var w = denInv * cross(v0, v2);
                var u = 1.0f - v - w;
                return new(u, v, w);
            }
            // NOTE: use barycentric property.
            return math.cmax(-bar(a, b, c, p)) <= 0;
        }
        public readonly bool SupportRefinement() => true;
        public readonly float X(float2 a) => a.x;
        public readonly float Y(float2 a) => a.y;
        public readonly float Zero() => 0;
        public readonly float ZeroTBig() => 0;
        public readonly float abs(float v) => math.abs(v);
        public readonly float alpha(float D, float dSquare)
        {
            var d = math.sqrt(dSquare);
            var k = (int)math.round(math.log2(0.5f * d / D));
            return D / d * (k < 0 ? math.pow(2, k) : 1 << k);
        }
        public readonly bool anygreaterthan(float a, float b, float c, float v) => math.any(math.float3(a, b, c) > v);
        public readonly float2 avg(float2 a, float2 b) => 0.5f * (a + b);
        public readonly float cos(float v) => math.cos(v);
        public readonly float diff(float a, float b) => a - b;
        public readonly float2 diff(float2 a, float2 b) => a - b;
        public readonly float distancesq(float2 a, float2 b) => math.distancesq(a, b);
        public readonly float dot(float2 a, float2 b) => math.dot(a, b);
        public readonly bool2 eq(float2 v, float2 w) => v == w;
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
        public readonly float2 max(float2 v, float2 w) => math.max(v, w);
        public readonly float2 min(float2 v, float2 w) => math.min(v, w);
        public readonly float mul(float a, float b) => a * b;
        public readonly float2 neg(float2 v) => -v;
        public readonly float2 normalizesafe(float2 v) => math.normalizesafe(v);
    }

    internal readonly struct UtilsDouble : IUtils<double, double2, double>
    {
        public readonly double Cast(double v) => v;
        public readonly double2 CircumCenter(double2 a, double2 b, double2 c)
        {
            var d = b - a;
            var e = c - a;

            var bl = math.lengthsq(d);
            var cl = math.lengthsq(e);

            var d2 = 0.5 / (d.x * e.y - d.y * e.x);

            return a + d2 * (bl * math.double2(e.y, -e.x) + cl * math.double2(-d.y, d.x));
        }
        public readonly double Const(float v) => v;
        public readonly double EPSILON() => math.EPSILON_DBL;
        public readonly bool InCircle(double2 a, double2 b, double2 c, double2 p)
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

            return dx * (ey * cp - bp * fy) - dy * (ex * cp - bp * fx) + ap * (ex * fy - ey * fx) < 0;
        }
        public readonly double MaxValue() => double.MaxValue;
        public readonly double2 MaxValue2() => double.MaxValue;
        public readonly double2 MinValue2() => double.MinValue;
        public readonly bool PointInsideTriangle(double2 p, double2 a, double2 b, double2 c)
        {
            static double cross(double2 a, double2 b) => a.x * b.y - a.y * b.x;
            static double3 bar(double2 a, double2 b, double2 c, double2 p)
            {
                var (v0, v1, v2) = (b - a, c - a, p - a);
                var denInv = 1 / cross(v0, v1);
                var v = denInv * cross(v2, v1);
                var w = denInv * cross(v0, v2);
                var u = 1 - v - w;
                return new(u, v, w);
            }
            // NOTE: use barycentric property.
            return math.cmax(-bar(a, b, c, p)) <= 0;
        }
        public readonly bool SupportRefinement() => true;
        public readonly double X(double2 a) => a.x;
        public readonly double Y(double2 a) => a.y;
        public readonly double Zero() => 0;
        public readonly double ZeroTBig() => 0;
        public readonly double abs(double v) => math.abs(v);
        public readonly double alpha(double D, double dSquare)
        {
            var d = math.sqrt(dSquare);
            var k = (int)math.round(math.log2(0.5 * d / D));
            return D / d * (k < 0 ? math.pow(2, k) : 1 << k);
        }
        public readonly bool anygreaterthan(double a, double b, double c, double v) => math.any(math.double3(a, b, c) > v);
        public readonly double2 avg(double2 a, double2 b) => 0.5f * (a + b);
        public readonly double cos(double v) => math.cos(v);
        public readonly double diff(double a, double b) => a - b;
        public readonly double2 diff(double2 a, double2 b) => a - b;
        public readonly double distancesq(double2 a, double2 b) => math.distancesq(a, b);
        public readonly double dot(double2 a, double2 b) => math.dot(a, b);
        public readonly bool2 eq(double2 v, double2 w) => v == w;
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
        public readonly double2 max(double2 v, double2 w) => math.max(v, w);
        public readonly double2 min(double2 v, double2 w) => math.min(v, w);
        public readonly double mul(double a, double b) => a * b;
        public readonly double2 neg(double2 v) => -v;
        public readonly double2 normalizesafe(double2 v) => math.normalizesafe(v);
    }

    internal readonly struct UtilsInt : IUtils<int, int2, long>
    {
        public readonly int Cast(long v) => (int)v;
        public readonly int2 CircumCenter(int2 a, int2 b, int2 c)
        {
            var d = b - a;
            var e = c - a;

            var bl = (long)d.x * d.x + (long)d.y * d.y;
            var cl = (long)e.x * e.x + (long)e.y * e.y;

            var div = (long)d.x * e.y - (long)d.y * e.x;
            // NOTE: In a case when div = 0 (i.e. circumcenter is not well defined) we use int.MaxValue to mimic the infinity.
            //       Doubles can represent all integers up to 2^53 exactly, so they can represent all int32 coordinates, and thus it is safe to cast here.
            return div == 0 ? new(int.MaxValue) : (int2)math.round(a + (0.5 / div) * (bl * math.double2(e.y, -e.x) + cl * math.double2(-d.y, d.x)));
        }
        public readonly int Const(float v) => (int)v;
        public readonly long EPSILON() => 0;
        public readonly bool InCircle(int2 a, int2 b, int2 c, int2 p)
        {
            // Do a coordinate change to check if the origin is inside abc instead.
            // Note: Will overflow if the coordinates differ by more than 2^31 (but this is not the limiting factor)
            a -= p;
            b -= p;
            c -= p;
            // TODO: Is it better for performance to check if the coordinates are small,
            // and if so, do the calculation with 64-bit arithmetic only?

            // Should not overflow since we cast to long
            var ap = (long)a.x * a.x + (long)a.y * a.y;
            var bp = (long)b.x * b.x + (long)b.y * b.y;
            var cp = (long)c.x * c.x + (long)c.y * c.y;

            // This is the calculation we want to do, but it may overflow for large coordinates.
            // Therefore we first do 64-bit multiplications, and then the final 3 multiplications with 128-bit arithmetic.
            // return a.x * (b.y * cp - bp * c.y) - a.y * (b.x * cp - bp * c.x) + ap * (b.x * c.y - b.y * c.x) < 0;

            // May overflow for coordinates larger than about 2^20.
            // Therefore, when verifying coordinates, we ensure that the bounding box is smaller than 2^20.
            var det1 = b.y * cp - bp * c.y;
            var det2 = b.x * cp - bp * c.x;
            var det3 = b.x * (long)c.y - b.y * (long)c.x;

            var res = I128.Multiply(a.x, det1) - I128.Multiply(a.y, det2) + I128.Multiply(ap, det3);

            return res.IsNegative;
        }
        public readonly long MaxValue() => long.MaxValue;
        public readonly int2 MaxValue2() => int.MaxValue;
        public readonly int2 MinValue2() => int.MinValue;
        public readonly bool PointInsideTriangle(int2 p, int2 a, int2 b, int2 c)
        {
            static long cross(int2 a, int2 b) => (long)a.x * b.y - (long)a.y * b.x;
            // NOTE: triangle orientation is guaranteed.
            return cross(p - a, b - a) >= 0 && cross(p - b, c - b) >= 0 && cross(p - c, a - c) >= 0;
        }
        public readonly bool SupportRefinement() => false;
        public readonly int X(int2 a) => a.x;
        public readonly int Y(int2 a) => a.y;
        public readonly int Zero() => 0;
        public readonly long ZeroTBig() => 0;
        public readonly int abs(int v) => math.abs(v);
        public readonly long abs(long v) => math.abs(v);
        public readonly int alpha(int D, int dSquare) => throw new NotImplementedException();
        public readonly bool anygreaterthan(int a, int b, int c, int v) => throw new NotImplementedException();
        public readonly int2 avg(int2 a, int2 b) => (a + b) / 2;
        public readonly int cos(int v) => throw new NotImplementedException();
        public readonly int diff(int a, int b) => a - b;
        public readonly long diff(long a, long b) => a - b;
        public readonly int2 diff(int2 a, int2 b) => a - b;
        public readonly long distancesq(int2 a, int2 b) => (long)(a - b).x * (a - b).x + (long)(a - b).y * (a - b).y;
        public readonly int dot(int2 a, int2 b) => throw new NotImplementedException();
        public readonly bool2 eq(int2 v, int2 w) => v == w;
        public readonly bool2 ge(int2 a, int2 b) => a >= b;
        public readonly bool greater(int a, int b) => a > b;
        public readonly bool greater(long a, long b) => a > b;
        public readonly int hashkey(int2 p, int2 c, int hashSize)
        {
            return (int)math.floor(pseudoAngle(p.x - c.x, p.y - c.y) * hashSize) % hashSize;

            static double pseudoAngle(int dx, int dy)
            {
                var dist = math.abs(dx) + math.abs(dy);
                if (dist == 0) return 0;
                var p = (double)dx / dist;
                return (dy > 0 ? 3 - p : 1 + p) / 4; // [0..1]
            }
        }
        // TODO: Validate really large coordinates with tests. Probably this should include check for v < 2^20.
        public readonly bool2 isfinite(int2 v) => true;
        public readonly bool le(int a, int b) => a <= b;
        public readonly bool le(long a, long b) => a <= b;
        public readonly bool2 le(int2 a, int2 b) => a <= b;
        public readonly int2 lerp(int2 a, int2 b, int v) => throw new NotImplementedException();
        public readonly bool less(long a, long b) => a < b;
        public readonly int2 max(int2 v, int2 w) => math.max(v, w);
        public readonly int2 min(int2 v, int2 w) => math.min(v, w);
        public readonly long mul(int a, int b) => (long)a * b;
        public readonly int2 neg(int2 v) => -v;
        public readonly int2 normalizesafe(int2 v) => throw new NotImplementedException();
    }

#if UNITY_MATHEMATICS_FIXEDPOINT
    internal readonly struct UtilsFp : IUtils<fp, fp2, fp>
    {
        public readonly fp Cast(fp v) => v;
        public readonly fp2 CircumCenter(fp2 a, fp2 b, fp2 c)
        {
            var d = b - a;
            var e = c - a;

            var bl = fpmath.lengthsq(d);
            var cl = fpmath.lengthsq(e);

            // NOTE: In a case when div = 0 (i.e. circumcenter is not well defined) we use fp.max_value to mimic the infinity.
            var div = d.x * e.y - d.y * e.x;
            return div == 0 ? fp.max_value : a + (fp)1L / 2L / div * (bl * fpmath.fp2(e.y, -e.x) + cl * fpmath.fp2(-d.y, d.x));
        }
        public readonly fp Const(float v) => (fp)v;
        public readonly fp EPSILON() => fp.FromRaw(1L);
        public readonly bool InCircle(fp2 a, fp2 b, fp2 c, fp2 p)
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

            return dx * (ey * cp - bp * fy) - dy * (ex * cp - bp * fx) + ap * (ex * fy - ey * fx) < 0;
        }
        public readonly fp MaxValue() => fp.max_value;
        public readonly fp2 MaxValue2() => fp.max_value;
        public readonly fp2 MinValue2() => fp.min_value;
        public readonly bool PointInsideTriangle(fp2 p, fp2 a, fp2 b, fp2 c)
        {
            static fp cross(fp2 a, fp2 b) => a.x * b.y - a.y * b.x;
            static fp3 bar(fp2 a, fp2 b, fp2 c, fp2 p)
            {
                var (v0, v1, v2) = (b - a, c - a, p - a);
                var denInv = 1 / cross(v0, v1);
                var v = denInv * cross(v2, v1);
                var w = denInv * cross(v0, v2);
                var u = (fp)1L - v - w;
                return new(u, v, w);
            }
            // NOTE: use barycentric property.
            return fpmath.cmax(-bar(a, b, c, p)) <= 0;
        }
        public readonly bool SupportRefinement() => true;
        public readonly fp X(fp2 a) => a.x;
        public readonly fp Y(fp2 a) => a.y;
        public readonly fp Zero() => 0;
        public readonly fp ZeroTBig() => 0;
        public readonly fp abs(fp v) => fpmath.abs(v);
        public readonly fp alpha(fp D, fp dSquare)
        {
            var d = fpmath.sqrt(dSquare);
            var k = (int)fpmath.round(fpmath.log2(d / D / 2L));
            return D / d * (k < 0 ? fpmath.pow(2, k) : 1 << k);
        }
        public readonly bool anygreaterthan(fp a, fp b, fp c, fp v) => math.any(fpmath.fp3(a, b, c) > v);
        public readonly fp2 avg(fp2 a, fp2 b) => (a + b) / 2;
        public readonly fp cos(fp v) => fpmath.cos(v);
        public readonly fp diff(fp a, fp b) => a - b;
        public readonly fp2 diff(fp2 a, fp2 b) => a - b;
        public readonly fp distancesq(fp2 a, fp2 b) => fpmath.distancesq(a, b);
        public readonly fp dot(fp2 a, fp2 b) => fpmath.dot(a, b);
        public readonly bool2 eq(fp2 v, fp2 w) => v == w;
        public readonly bool2 ge(fp2 a, fp2 b) => a >= b;
        public readonly bool greater(fp a, fp b) => a > b;
        public readonly int hashkey(fp2 p, fp2 c, int hashSize)
        {
            return (int)fpmath.floor(pseudoAngle(p.x - c.x, p.y - c.y) * hashSize) % hashSize;

            static fp pseudoAngle(fp dx, fp dy)
            {
                var p = dx / (fpmath.abs(dx) + fpmath.abs(dy));
                return (dy > 0 ? 3 - p : 1 + p) / 4; // [0..1]
            }
        }
        public readonly bool2 isfinite(fp2 v) => fpmath.isfinite(v);
        public readonly bool le(fp a, fp b) => a <= b;
        public readonly bool2 le(fp2 a, fp2 b) => a <= b;
        public readonly fp2 lerp(fp2 a, fp2 b, fp v) => fpmath.lerp(a, b, v);
        public readonly bool less(fp a, fp b) => a < b;
        public readonly fp2 max(fp2 v, fp2 w) => fpmath.max(v, w);
        public readonly fp2 min(fp2 v, fp2 w) => fpmath.min(v, w);
        public readonly fp mul(fp a, fp b) => a * b;
        public readonly fp2 neg(fp2 v) => -v;
        public readonly fp2 normalizesafe(fp2 v) => fpmath.normalizesafe(v);
    }
#endif
}