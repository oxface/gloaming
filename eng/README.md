# Repository engineering

`eng/` contains repository validation and package adoption tools. Run the commands
from the repository root with the pinned Node.js and .NET SDK versions.

| Tooling                                                                   | Commands                                                                                      |
| ------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| [Repository validation](repository/README.md)                             | `npm ci --prefix eng/repository --ignore-scripts` and `npm --prefix eng/repository run check` |
| [Package and adoption proofs](release/README.md)                          | `npm ci --prefix eng/release --ignore-scripts` and `npm --prefix eng/release run check`       |
| [Nx selection proof](repository/README.md#nx-task-graph-and-ci-selection) | `npm ci --ignore-scripts` and `npm run check:nx`                                              |

The [development guide](../docs/development.md) covers setup and provider prerequisites.
