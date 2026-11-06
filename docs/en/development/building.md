# Building and publishing

From the version in `Directory.Build.props` to the installer:

```mermaid
flowchart LR
    V["Version in<br/>Directory.Build.props"] --> B["dotnet build<br/>dotnet test"]
    B --> P["dotnet publish"]
    P --> D[("installer\publish\")]
    D --> I["ISCC.exe"]
    I --> S(["Installer<br/>Setup-x.y.z.exe"])
    class D io
    class S ok
```

## Requirements

- 64-bit Windows 11. The application uses Windows audio APIs and WPF.
- The **.NET 10** SDK.
- Visual Studio with support for .NET 10 and `.slnx` solutions, or any editor with the `dotnet` CLI.

## Solution layout

| Path | Contents |
|---|---|
| `LoopbackTranscriber.slnx` | The solution, with both projects. |
| `Directory.Build.props` | Version and analyzers. Applies to every project. |
| `.editorconfig` | Analysis rule settings. |
| `.config\dotnet-tools.json` | DocFX as a local tool, for this documentation. |
| `LoopbackTranscriber\` | The application: `WinExe`, `net10.0-windows`, WPF. |
| `LoopbackTranscriber.Tests\` | The tests: xUnit, `net10.0-windows`, WPF. |
| `installer\` | The Inno Setup script. `publish\` (the published app) and `Output\` (the installer) are generated inside it. |
| `docs\` | This documentation, in Spanish and English. |
| `LICENSE` | MIT license. |

## Building and testing

```powershell
dotnet build LoopbackTranscriber.slnx -c Release
dotnet test  LoopbackTranscriber.slnx -c Release
```

In Visual Studio: *Build → Build Solution* and *Test → Run All Tests*. More details in [Tests](tests.md).

## Quality rules

`Directory.Build.props` applies to both projects:

| Property | Value | Effect |
|---|---|---|
| `Version` | `1.1.0` | Assembly and executable version. The installer reads it from the `.exe`. |
| `EnableNETAnalyzers` | `true` | .NET analyzers on. |
| `AnalysisLevel` | `latest-recommended` | Recommended rule set of the latest version. |
| `TreatWarningsAsErrors` | `true` | Any compiler or analyzer warning **breaks the build**. |
| `Deterministic` | `true` | Reproducible builds. |

`.editorconfig` adjusts a few rules:

| Scope | Rule | Reason |
|---|---|---|
| All code | `CA1822.api_surface = private, internal` | Only suggests `static` on private and internal members, not on the public API of the ViewModels, which the bindings use. |
| All code | `CA1716` off | Allows namespace and member names that match reserved words of other languages. |
| Tests | `CA1707` off | Test names with underscores: `Method_Situation_Result`. |
| Tests | `CA1861` off | Constant arrays as arguments in tests. |
| Tests | `CA2255` off | Allows the `[ModuleInitializer]` that prepares the test environment. |

In addition, every build runs the [localization check](../architecture/localization.md): an unknown key or a missing translation is an error.

## Project details

- `App.xaml` is compiled as a `Page`, not as an `ApplicationDefinition`, to use the custom `Program.Main` that handles the [worker processes](../architecture/workers.md).
- `InternalsVisibleTo LoopbackTranscriber.Tests`: the tests can reach `internal` types, such as the filters or the worker protocol.
- `THIRD-PARTY-NOTICES.txt` is copied to the output and to the publish folder.
- Runtime settings: non-concurrent GC, `TieredPGO` off and `System.GC.ConserveMemory = 5`. See [Overview](../architecture/overview.md).

## Publishing

The `Properties\PublishProfiles\FolderProfile.pubxml` profile is the one the installer uses:

| Property | Value |
|---|---|
| Configuration | `Release` |
| `RuntimeIdentifier` | `win-x64` |
| `SelfContained` | `true`: includes the .NET runtime, nothing else to install |
| `PublishSingleFile` | `false`: the native Whisper and LLama libraries ship as separate files |
| `PublishReadyToRun` | `false` |
| `PublishDir` | `..\installer\publish\` |

```powershell
dotnet publish LoopbackTranscriber\LoopbackTranscriber.csproj -p:PublishProfile=FolderProfile
```

Or, in Visual Studio: right-click the project → *Publish* → *FolderProfile*.

The next step is building the [installer](installer.md).

## Changing the version

1. Change `<Version>` in `Directory.Build.props`.
2. Publish.
3. Build the installer. It takes the version from the published `LoopbackTranscriber.exe`, so it does not need to be touched.
