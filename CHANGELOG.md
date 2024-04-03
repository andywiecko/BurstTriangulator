# Change log

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

For online version see [Github Releases].

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

- Added constrained triangulation benchmark test. The results may be found at [`README.md`](README.md#benchmark).

## [2.1.0] ⁠– 2023-09-17

### Changed

- Replaced the *classic* Delaunay algorithm (without refinement/constraints) with an implementation based on `half-edges` (see [`delaunator`](https://github.com/mapbox/delaunator) and [`delaunator-sharp`](https://github.com/nol1fe/delaunator-sharp/) for more details). This change has led to a significant performance boost in unconstrained triangulation. See [`README.md`](README.md#benchmark) for more details.
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

[#30]: https://github.com/andywiecko/BurstTriangulator/issues/30
[#31]: https://github.com/andywiecko/BurstTriangulator/issues/31
[#111]: https://github.com/andywiecko/BurstTriangulator/issues/111