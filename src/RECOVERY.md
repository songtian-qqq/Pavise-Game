# Source provenance

The source baseline was reconstructed from the published Pavise 3.1.0.6 executable using ILSpy CLI 8.2.0.7535, then maintained on `dev`.

Original executable SHA-256: `85A9B13542553BCDD1E785F8A681C4A2FD128A2D9645FA5FF5385A3F65EA26FD`.

Copyright remains 2026 bdth, GPL-3.0-only; see [LICENSE](../LICENSE) and [NOTICE](../NOTICE). Reconstruction did not preserve all comments, local variable names, conditional compilation or partial-file organization. The former string-switch dictionary repair has been replaced by a normal C# string switch. Session and UI methods are now grouped into partial files by responsibility, and the core artwork uses descriptive layer and animation names.

These files are the maintained build inputs. Source reconstruction alone does not prove behavioral equivalence to every historical binary. See the [source guide](../docs/SOURCE-GUIDE.md) for the current module map and [tests](../tests/README.md) for runnable checks and their scope.
