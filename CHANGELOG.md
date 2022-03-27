# Change log

## [1.1.0] - 2022-03-27

### Features

- Add support for Constraint Delaunay Triangulation. Selected edges can be constrained e.g. for restoring the boundary. The feature currently does not support mesh refinement. 
- Basic validation of the input positions as well as input constraint edges.

### Changes

- Refactor of input/output data buffers, some of them are marked as obsoletes. 

## [1.0.1] - 2021-11-24

### Changed

- Util function `GetCircumcenter` has been optimized. It is faster and more stable.
- Unity packages have been updated (Note: there was API changed in `FixedList<T>`).

## [1.0.0] ⁠– 2021-10-26

### Features

- Initial release version
