# Change log

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

For online version see [Github Releases].

## [1.5.0] - 2023-04-12

### Added

- Added PCA transformation for input positions and holes.

### Fixed

- Editor hangs during Sloan algorithm for specific input data (issues [#30] and [#31]).

## [1.4.0] - 2022-11-01

### Added

- Added option for transforming input positions (as well as holes) into normalized local space, i.e. [-1, 1] box. Converting points into normalized local space could increase numerical accuracy.

### Fixed

- Fix deferred array support in triangulator input.
- Add missing constraint position range validation.
- Fix whitespaces in code and `README.md`.

## [1.3.0] - 2022-04-09

### Added

- Restoring input boundaries. The feature allows for restoring a given boundary from triangulation input.
It is necessary to provide constraints, as well as enable corresponding
options in the triangulation settings, aka `RestoreBoundary`.
- Support for holes in the mesh.
- Upload project's logo generated using the above features.

### Changes

- More verbose warnings during input validation.

## [1.2.0] - 2022-04-02

### Added

- Add support for the Constraint Delaunay Triangulation with mesh refinement.

### Changes

- Performance: Bower-Watson point insertion algorithm has been optimised and is based on the breadth-first search.
- Refactor: moved a few methods from jobs into `TriangulatorNativeData`.
- Refactor: structures have more compact layout.

## [1.1.0] - 2022-03-27

### Added

- Add support for Constraint Delaunay Triangulation. Selected edges can be constrained e.g. for restoring the boundary. The feature currently does not support mesh refinement.
- Basic validation of the input positions as well as input constraint edges.

### Deprecated

- Refactor of input/output data buffers, some of them are marked as obsoletes.

## [1.0.1] - 2021-11-24

### Changed

- Util function `GetCircumcenter` has been optimized. It is faster and more stable.
- Unity packages have been updated (Note: there was API changed in `FixedList<T>`).

## [1.0.0] ⁠– 2021-10-26

### Added

- Initial release version

[Github Releases]: https://github.com/andywiecko/BurstTriangulator/releases
[1.5.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v1.5.0
[1.4.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v1.4.0
[1.3.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v1.3.0
[1.2.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v1.2.0
[1.1.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v1.1.0
[1.0.1]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v1.0.1
[1.0.0]: https://github.com/andywiecko/BurstTriangulator/releases/tag/v1.0.0
[#30]: https://github.com/andywiecko/BurstTriangulator/issues/30
[#31]: https://github.com/andywiecko/BurstTriangulator/issues/31