# Repository engineering

`eng/` holds repository tooling. Use the pinned Node.js and .NET SDK from the
repository root.

| Tooling                                                   | Responsibility                                                               | Entry points                                                                                                            |
| --------------------------------------------------------- | ---------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| [Repository validation](repository/README.md)             | Check source references, Git hooks and CI selection.                         | `npm ci --prefix eng/repository --ignore-scripts`, `npm --prefix eng/repository run check`                              |
| [Package tooling](release/README.md)                      | Build local NuGet snapshots and verify adoption from the resulting archives. | `npm --prefix eng/release run pack`, `npm --prefix eng/release run verify`, `npm --prefix eng/release run verify:kafka` |
| [Nx](repository/README.md#nx-task-graph-and-ci-selection) | Discover test projects, select affected work and run repository targets.     | `npm ci --ignore-scripts`, `npm run check:nx`                                                                           |

Library and application scenarios run through their native test projects. The C#
consumers under `eng/release/consumers/` verify the package boundary by building
outside the checkout against exact NuGet references. The TypeScript commands
handle packing, isolated restore and artifact verification. See the
[test map](../tests/README.md) and [development guide](../docs/development.md).
