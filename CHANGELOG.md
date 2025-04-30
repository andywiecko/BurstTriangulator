# Change log

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

For online version see [Github Releases].

## [3.8.0] – 2025-04-30

### Added

- $\alpha$-shape filter for `Triangulator<T>` and corresponding extensions for `UnsafeTriangulator<T>`. Enable this via the `UseAlphaShapeFilter` in `TriangulatorSettings`. Additional configuration is available through `AlphaShapeSettings`. Options include:
  - `ProtectPoints` prevents triangle removal if it would leave any of its points unassigned to a triangle;
  - `ProtectConstraints` ensures triangles with constrained edges are preserved;
  - `PreventWindmills` avoids formation of windmill structures.
- `GeneratePointTriangleCount` utility in `Utilities`. This method fills an input buffer with the count of triangles each vertex index participates in, based on a given triangle index buffer.

## [3.7.0] – 2025-03-22

### Added

- Several triangle mesh related utilities, including:
  - `GenerateHalfedges`;
  - `GenerateTriangleColors`;
  - `InsertSubMesh`;
  - `NextHalfedge`;
  - `Retriangulate`, an extension for `UnityEngine.Mesh`;
  - `RetriangulateMeshJob`.
- `Triangulator` and `Triangulator<T>` now implement `INativeDisposable`.

### Fixed

- Safety handles for `NativeList`s in `UnsafeTriangulator`.

## [3.6.0] – 2025-02-01

### Added

- `ConstrainEdge` extension for `UnsafeTriangulator<T>`. Edges can now be constrained dynamically.
- `NativeInputData<T>` and `NativeOutputData<T>`, which are essentially `LowLevel.Unsafe.InputData<T>` and `LowLevel.Unsafe.OutputData<T>`. This change resolves ambiguity between managed and unmanaged input/output data.
- (Internal) `NativeQueueList<T>`, an alternative implementation to `Unity.Collections.NativeQueue<T>`. This wrapper is more stable and faster, though it consumes more memory.

### Changed

- Improved the performance of the refinement step. Depending on the input, speedups of up to 50% can be observed!
- Various documentation (summary/manual/scripting API) tweaks.

### Deprecated

- `LowLevel.Unsafe.InputData<T>` and `LowLevel.Unsafe.OutputData<T>` have been deprecated. Use `NativeInputData<T>` and `NativeOutputData<T>` instead.

### Fixed

- A rare issue causing an editor crash when reloading the domain, apparently caused by `NativeQueue`. This has been resolved by replacing `NativeQueue` with the internal `NativeQueueList<T>` implementation.

## [3.5.0] – 2024-12-06

### Added

- `DynamicSplitHalfedge` extension for `UnsafeTriangulator`, enabling point insertion by splitting a selected halfedge.
- `DynamicRemoveBulkPoint` extension for `UnsafeTriangulator`, facilitating the removal of points from the mesh bulk.

### Changed

- Refactor Bower-Watson point insertion algorithm to achieve approximately a 25% performance improvement during the refinement step and dynamic triangulation routines.
- Optimized planting seeds job for slightly increased performance and reduced memory allocation.
- (Internal) Various refactors to improve code clarity and maintainability.

## [3.4.0] – 2024-10-25

### Added

- Dynamic triangulation support. Introduced `DynamicInsertPoint` extension for `UnsafeTriangulator` to support dynamic point insertion.
- A new demo scene to better illustrate the functionality in [the documentation](https://andywiecko.github.io/BurstTriangulator/demo/).

### Changed

- Improved support for `TriangulatorSettings` in `UnityEditor` by adding a missing backing field, a setter, and editor-related attributes for properties.
- Refinement step optimization. Refactored the refinement step to enhance performance and simplify the code.

### Fixed

- Refinement with constraints without holes. Corrected an issue where refinement with constraints would ignore boundaries when holes were absent.
- Invalid refinement results. Fixed a rare issue where refinement could produce invalid results, especially when input data included subsegment clusters, leading to points appearing outside the triangulation domain.

## [3.3.0] – 2024-09-23

### Added

- Ignored constraints for seed planting. Users can now ignore specific constraints during the seed planting process. This is especially useful when constraining edges without creating hole boundaries. This option can be set using `Input.IgnoreConstraintForPlantingSeeds`. Additionally, post-triangulation verification can be done with `Output.IgnoredHalfedgesForPlantingSeeds`, which provides a list of booleans indicating whether a given halfedge was ignored during seed planting.
- Status error codes. New flags have been added to the `Status` enum for enhanced error handling during triangulation. Users can now catch errors during validation more effectively. Note: The `Status` enum is now decorated with the `[Flags]`. To check if no errors occurred, use `status == Status.OK`.

### Changed

- Faster hole planting. The complexity has improved from 𝒪(n²) to 𝒪(n), making hole planting almost free compared to the Delaunay step.
- Improved validation. All input data buffers are now validated. Additionally, some unconfigured settings can trigger log warnings.

### Fixed

- Integer overflow for `int2` coordinates. Resolved an overflow issue for large coordinates with differences around ~2²⁰.

## [3.2.1] – 2024-09-03

### Changed

- Significant updates to the API documentation.
- (Internal) Miscellaneous changes.

### Deprecated

- The `OutputData(Triangulator<T2>)` constructor is now obsolete. It will be made internal in future versions.

### Fixed

- Resolved a potential issue causing an infinite loop during the `PlantingSeedStep` with `AutoHolesAndBoundary`.

## [3.2.0] – 2024-08-28

### Added

- Support for additional types: `Vector2`, `int2`, and `fp2` (fixed-point in Q31.32 format). Note: `fp2` requires an optional dependency. Refer to the manual for more details.
- (Internal) Introduced `TrianglesComparer` to simplify triangle assertions in tests.
- `Args` is now blittable and can be used in Burst-compiled static methods.
- Enhanced validation logs to include position information.

### Changed

- (Internal) Various simplifications, minor performance improvements, refactoring, and additional code comments.

### Deprecated

- `AsNativeArray()` and `ManagedInput` have been deprecated for safety reasons. Use `AsNativeArray(out Handle handle)` instead. Refer to the manual for more information.

### Fixed

- Corrected the refinement of concentric shells segment splitting factor alpha.
- Fixed safety issues with `AsNativeArray`.
- Fully collinear input is now handled correctly.

## [3.1.0] – 2024-08-01

### Added

- Native support with a low-level API for triangulation via `UnsafeTriangulator<T>`. This allows for customization of steps and calling triangulation directly from jobs.
- Extensions for `UnsafeTriangulator<T>`: `Triangulate`, `PlantHoleSeeds`, and `RefineMesh`.
- Support for managed input.
- Public `ConstrainedHalfedges` in triangulation output.

### Fixed

- Edge-edge intersection for collinear non-intersecting edges (issue [#173]).

## [3.0.0] – 2024-06-30

### Added

- **New** online documentation (including manual and scripting API): <https://andywiecko.github.io/BurstTriangulator/>
- `Verbose` option in triangulation settings.
- `Halfedges` for triangulation output.
- `AutoHolesAndBoundary` option in triangulation settings. This option allows for automatic hole detection, eliminating the need for the user to provide hole positions. Holes are calculated using constraint edges.
- Support for generic coordinates. Users can specify the type of coordinates used in the triangulation with `Triangulator<T2>`. The API for this class is the same as `Triangulator`, except the input/output is of type `T2`. Supported coordinate types: `float2`, `double2` (`int2` will be implemented in the future).

### Changed

- Increased performance for `constrainedHalfedges` generation.
- `Circle` based calculations now use `RadiusSq` instead of `Radius`. This results in increased performance in the refinement step, however, the final results may be slightly different.
- **Breaking change:** Moved most of the inner types (e.g., `Status`, `TriangulatorSettings`, etc.) to the global namespace. They can now be accessed directly using `using andywiecko.BurstTriangulator;`.
- **Breaking change:** The default coordinate type for `Triangulator` is now `double2` instead of `float2`.
- (Internal) Greatly simplified job logic replaced with steps. Overall triangulation logic is now easier to follow and read. Some internal structs were removed or hidden inside steps.

### Removed

- Obsolete settings: `BatchCount`, `MinimumAngle`, `MinimumArea`, `MaximumArea`, and `ConstrainEdges`.

### Fixed

- Run triangulator on the main thread using the `Run()` method.

## [2.5.0] – 2024-04-03

### Changed

- Simplified `PlantingSeedJob` by removing generics and introducing an algorithm based on `constraintEdges`. This resulted in improved performance.
- Changed the triangulator to schedule a single job instead of multiple smaller ones.
- Greatly simplified the preprocessor transformations code. All transformations are now represented by the `AffineTransform2D` struct, and several jobs have been removed.

### Deprecated

- Deprecated the `Triangulator.Settings.ConstrainEdges` property. To enable constrained edges, pass the corresponding array into input.
- Deprecated the `Triangulator.Settings.BatchCount` property. This property is no longer used, setting it has no effect.

### Fixed

- Fixed constructing `pointToHalfedges` during constraint resolution. This resolves GitHub issue [#111].

## [2.4.0] – 2023-12-23

### Added

- Introduce `ConcentricShellParameter` in `TriangulationSettings`, serving as a constant for *concentric shells* segment splitting.
- Add `RefinementThresholds` in `TriangulationSettings`, including `.Area` and `.Angle`. Previous corresponding parameters are marked with obsolete.

### Changed

- Enhance triangulation refinement for improved quality and performance. Replace the previous algorithm with a new one, similar to Shewchuk's *terminator* algorithm. The refined mesh now exhibits non-uniform triangle density, with increased density near boundaries and decreased density in the mesh bulk.
- Update `README.md` to include a comparison between different refinement settings.
- Remove the super-triangle approach (resulting in a slight performance boost). Perform refinement after removing holes and boundaries for better refinement quality.

### Deprecated

- Mark `MinimumArea`, `MaximumArea`, and `MinimumAngle` as obsolete. Replace these parameters with the more versatile `RefinementThresholds`.

## [2.3.0] – 2023-10-25

### Changed

- Improved performance by adapting triangulation with mesh refinement to a `half-edges` approach.
- Simplified the refinement job contract.
- Merged several internal jobs for better efficiency.
- General project simplification for enhanced maintainability.

### Removed

- Eliminated `edgeToTriangles` and `triangleToEdges` mappings.
- Removed the internal `Triangle` struct.

## [2.2.0] ⁠– 2023-10-03

### Changed

- Simplified constrained triangulation scheduling jobs pipeline.
- Adapted constrained triangulation for `half-edges` approach, which significantly improved performance. The algorithm no longer relies on triangulation mappings, such as edge-to-triangle and triangle-to-edge relationships (or circles). The complexity of the intersection searching algorithm has been reduced from a naive `O(n^2)` solution to `O(n log n)`.

### Fixed

- Resolved Sloan algorithm corner cases. In previous releases, constrained triangulation may get stuck. Constrained triangulation is now more robust.

### Added

- Added constrained triangulation benchmark test. The results may be found at [`README.md`](https://github.com/andywiecko/BurstTriangulator/blob/main/README.md#benchmark).

## [2.1.0] ⁠– 2023-09-17

### Changed

- Replaced the *classic* Delaunay algorithm (without refinement/constraints) with an implementation based on `half-edges` (see [`delaunator`](https://github.com/mapbox/delaunator) and [`delaunator-sharp`](https://github.com/nol1fe/delaunator-sharp/) for more details). This change has led to a significant performance boost in unconstrained triangulation. See [`README.md`](https://github.com/andywiecko/BurstTriangulator/blob/main/README.md#benchmark) for more details.
- Refactored some internal math utilities.

## [2.0.0] ⁠– 2023-09-09

### Added

- Introduced the `Preprocessor` enum with the following options: `None`, `COM`, and `PCA`. This enum replaces the previous transformation settings (`UseLocalTransformation`/`UsePCATransformation`).
- Introduced the `Status` (with values `OK`, `ERR`) enum along with corresponding native data. This enum is now utilized for input validation, with functionality extending beyond the Unity editor to encompass validation in builds as well.
- Added a benchmark test for mesh refinement, which will be used for future performance measurement.

### Changed

- Default values for `TriangulationSettings`.
- Updated Unity Editor to version `2022.2.1f1`.
- Bumped dependencies: Burst to `1.8.7`, Collections to `2.2.0`.

### Removed

- Removed the following deprecated methods: `Schedule(NativeArray<float2>, ...)`.
- Removed the following deprecated properties: `Positions`, `Triangles`, `PositionsDeferred`, `PositionsDeferred`.
- Removed the internal `TriangulatorNativeData` as part of a significant refactor to simplify the code structure. Internal implementations were cleaned up, and code structure was simplified.

## [1.5.0] ⁠– 2023-04-12

### Added

- Added PCA transformation for input positions and holes.

### Fixed

- Editor hangs during Sloan algorithm for specific input data (issues [#30] and [#31]).

## [1.4.0] ⁠– 2022-11-01

### Added

- Added option for transforming input positions (as well as holes) into normalized local space, i.e. [-1, 1] box. Converting points into normalized local space could increase numerical accuracy.

### Fixed

- Fix deferred array support in triangulator input.
- Add missing constraint position range validation.
- Fix whitespaces in code and `README.md`.

## [1.3.0] ⁠– 2022-04-09

### Added

- Restoring input boundaries. The feature allows for restoring a given boundary from triangulation input.
It is necessary to provide constraints, as well as enable corresponding
options in the triangulation settings, aka `RestoreBoundary`.
- Support for holes in the mesh.
- Upload project's logo generated using the above features.

### Changed

- More verbose warnings during input validation.

## [1.2.0] ⁠– 2022-04-02

### Added

- Add support for the Constraint Delaunay Triangulation with mesh refinement.

### Changed

- Performance: Bower-Watson point insertion algorithm has been optimised and is based on the breadth-first search.
- Refactor: moved a few methods from jobs into `TriangulatorNativeData`.
- Refactor: structures have more compact layout.

## [1.1.0] ⁠– 2022-03-27

### Added

- Add support for Constraint Delaunay Triangulation. Selected edges can be constrained e.g. for restoring the boundary. The feature currently does not support mesh refinement.
- Basic validation of the input positions as well as input constraint edges.

### Deprecated

- Refactor of input/output data buffers, some of them are marked as obsoletes.

## [1.0.1] ⁠– 2021-11-24

### Changed

- Util function `GetCircumcenter` has been optimized. It is faster and more stable.
- Unity packages have been updated (Note: there was API changed in `FixedList<T>`).

## [1.0.0] ⁠– 2021-10-26

### Added

- Initial release version

[Github Releases]: https://github.com/andywiecko/BurstTriangulator/releases

[3.8.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v3.8.0
[3.7.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v3.7.0
[3.6.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v3.6.0
[3.5.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v3.5.0
[3.4.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v3.4.0
[3.3.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v3.3.0
[3.2.1]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v3.2.1
[3.2.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v3.2.0
[3.1.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v3.1.0
[3.0.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v3.0.0
[2.5.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v2.5.0
[2.4.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v2.4.0
[2.3.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v2.3.0
[2.2.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v2.2.0
[2.1.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v2.1.0
[2.0.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v2.0.0
[1.5.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v1.5.0
[1.4.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v1.4.0
[1.3.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v1.3.0
[1.2.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v1.2.0
[1.1.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v1.1.0
[1.0.1]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v1.0.1
[1.0.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v1.0.0

[#173]: https://github.com/andywiecko/BurstTriangulator/issues/173
[#111]: https://github.com/andywiecko/BurstTriangulator/issues/111
[#31]: https://github.com/andywiecko/BurstTriangulator/issues/31
[#30]: https://github.com/andywiecko/BurstTriangulator/issues/30
