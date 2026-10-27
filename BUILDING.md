# Building RolltheSpire2

## Requirements

- Windows
- .NET 9 SDK
- Slay the Spire 2 Beta111 / 0.111.0 runtime assemblies
- `sts2.dll` and `0Harmony.dll`

The project uses `Godot.NET.Sdk/4.5.1` and targets `net9.0`. The NuGet SDK builds
managed assemblies; it does not install a native Godot executable. A clean worktree
restores NuGet dependencies on its first build (or uses the machine's existing cache).

## Canonical build

```powershell
dotnet build .\RolltheSpire2.csproj -c Release
```

Runtime assemblies are discovered through `Sts2PathDiscovery.props`. Override when necessary:

```powershell
dotnet build .\RolltheSpire2.csproj -c Release `
  -p:Sts2ReferenceDir="D:\path\to\data_sts2_windows_x86_64"
```

Normal builds still deploy when `Rt2ModsDir` resolves. For independent worktrees and
validation, explicitly disable deployment and limit MSBuild concurrency:

```powershell
dotnet build -c Release -p:Rt2ModsDir= -m:1
```

The output DLL is under `.godot/mono/temp/bin/Release`. To stage an install locally,
pass `-p:Rt2ModsDir="<absolute staging directory>"`; its DLL goes under `RolltheSpire2/`.
Build target evidence defaults to `.build-evidence`; `Rt2BuildEvidenceDir` remains an
override. Historical `.rewrite-build-evidence` artifacts are not relocated.

For a machine-specific path, use an absolute `Sts2ReferenceDir` in the command above,
`STS2_REFERENCE_DIR`, or root `local.props`; `Sts2Path` in `local.props` may instead
name the game root. Both runtime DLLs must be present. These settings are local to
each worktree and are not included in source packages. No machine configuration or
registry edits are required. Quote paths containing spaces or Chinese characters.

`Sts2PathDiscovery.props` is the build resolver; on Windows it checks the game
uninstall registry, Steam's registered default library, and the standard Steam path.
The existing PowerShell resolver additionally reads `libraryfolders.vdf`. To inspect
its result without building or launching anything:

```powershell
. ./tools/build/Resolve-Sts2Runtime.ps1
$runtime = Resolve-Rt2Sts2Runtime -ProjectRoot $PWD.Path
$runtime | Format-List
# If build discovery differs, pass the inspected path explicitly:
dotnet build -c Release "-p:Sts2ReferenceDir=$($runtime.ReferenceDir)" -p:Rt2ModsDir= -m:1
```

The PowerShell wrapper reads literal `local.props` paths, resolving relative ones
against `ProjectRoot`; it does not evaluate MSBuild expressions or conditions. Use
absolute literal paths for settings shared by the root and nested test projects.
Unlike the build resolver, the wrapper intentionally continues discovery after an
invalid preferred path, reporting the chosen `Source` and `CheckedReferenceDirs`.

## Validation

Current focused checks run independently of the retired R1 aggregate:

```powershell
dotnet build tests/functional/Rt2FunctionalChecks -c Release -p:Rt2ModsDir= -m:1
dotnet run --project tests/functional/Rt2FunctionalChecks -c Release --no-build -- --compatibility-reference
```

`--compatibility-reference` is a small fixed managed smoke; it needs the game
assemblies but no native Godot process, GPU or full captured Corpus. Many other
functional modes read full Corpus contexts/workloads that are intentionally absent
from PublicSource; their source remains available, but running them needs the
matching private dataset. Inspect each mode's actual input reads before running.
Other relevant modes include
`--predictor-closure`, `--exact-boundary`, `--shop-ordered` and the `--family-*` flags
in `Program.cs`. Select checks for the change; the complete entry point is not a
single default smoke. Use normal [Production Host](tools/ProductionSearchHost/README.md)
replay for execution changes.

The existing path-only fixture creates temporary empty DLL placeholders and does
not build or start a host:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./tests/build/verify_sts2_runtime_autodiscovery.ps1
```

### Native host requirements

`ProductionSearchHost` is a Godot `Node` project, including in `-CpuOnly -Headless`
mode. Its scripts require a compatible Godot **.NET editor executable** supplied
through `-Godot`; `Run.ps1` and `RunCorpus.ps1` build Release with deployment disabled.
`-SkipBuild` requires a previous host Release build. Release binaries are copied to
the editor's Debug lookup directory. `-UiSurfaceChecks` uses real rendering for page
screenshots; it is not a managed or GPU-free smoke.

`--workbench-contract`, `--object-picker-context`, and `--authored-morphic` require
`tools/AuthoredEventChecks` to initialize Godot before calling the managed test
entry. Merely setting `RT2_AUTHORED_GODOT_RUNNER=1` does not supply that initialization.
After a compatible editor and the reference localization files are available:

```powershell
$env:RT2_NATIVE_CHECK = '--workbench-contract'
$env:RT2_NATIVE_GAME_PCK = '<absolute path to matching SlayTheSpire2.pck>'
./tools/AuthoredEventChecks/Run.ps1 `
  -GodotExe '<absolute path to Godot .NET console executable>' `
  -LocalizationSource '<directory containing eng/ and completion.json>'
```

This native command builds Debug without deployment and runs the selected checks;
it is not part of the small managed smoke. The optional PCK supplies matching game
resources; the current wrapper still requires the localization directory.

The installed game executable supports ordinary in-game acceptance, but has not
been validated as a substitute for these editor hosts. Upstream [Godot 4.5.1 assembly
path resolution](https://github.com/godotengine/godot/blob/4.5.1-stable/modules/mono/godotsharp_dirs.cpp)
uses different editor and exported-game assembly layouts. Finding the game's DLLs,
PCK, or the NuGet SDK therefore does not establish that standalone native checks can
run. Do not infer native-host success from a managed build. Owner Beta111 in-game
validation remains separate.

## Packaging

For source responsibilities, contribution guidance and distribution scope, see the
[development guide](DEVELOPMENT.md). Stable releases need matching version metadata,
source and binary identity plus the relevant validation and gameplay acceptance.

The existing SourceCandidate default is an **internal development handoff**, including
the full frozen replay Corpus. From a full repository checkout/tree:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\package_sourcecandidate.ps1 `
  -OutputZip .\artifacts\RolltheSpire2_SourceCandidate.zip
```

For an **external source distribution**, explicitly select PublicSource:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\package_sourcecandidate.ps1 `
  -PackageKind PublicSource `
  -SourceCommit HEAD `
  -OutputZip .\artifacts\RolltheSpire2_PublicSource.zip
```

PublicSource resolves `-SourceCommit` once (default `HEAD`) and exports only that
committed tree. Workspace edits never enter this package. Both the ZIP and the
public repository use `tools/source/Export-PublicSource.ps1` as their single
selection/derivation rule. Export directly to a new or empty directory when needed:

```powershell
./tools/source/Export-PublicSource.ps1 -ProjectRoot $PWD.Path `
  -SourceCommit HEAD -Destination '<new absolute output directory>'
```

The destination itself is the public source root; source ZIPs wrap that same root
in `RolltheSpire2/`. The exporter changes no Git refs and does not publish anything.
Source provenance belongs in the caller's delivery receipt outside the public tree;
`SOURCE_PACKAGE.json` contains only stable schema/package-kind metadata. Exporting
the same selected contents again creates the same file bytes, including when only
internal documents changed.

PublicSource retains product source/assets/Texts, relevant tests and tools,
licenses/notices, numerical resource provenance, and the selected v1 requests,
expected results and build provenance. It excludes internal task/audit/Knowledge
Base/architecture/status records, private synchronization workflows, generated
output and full runtime Corpus contexts. A narrow authored-Morphic JSON is retained
because current functional checks read it. Internal-document assertion scripts are
excluded from the public selection; numerical and source checks remain. The public
Corpus notice describes its missing matching-context dependency explicitly. A fresh
local capture is a separate dataset, not a substitute frozen benchmark.

Internal packaging continues to copy the current workspace, including uncommitted
source edits and the full frozen Corpus, while excluding generated output and
repository-only historical delivery/implementation evidence. It retains canonical
internal development documents. Attached ZIP/7z audit evidence stays in the repository;
unexpected archives outside those historical/generated exclusions remain errors.
Both package kinds retain all embedded `Search/Selectivity/MapEvidence/*.gz` inputs
with their hash records and source explanation. These records are provenance, not
additional game-content permission or per-sample gameplay acceptance.

For the **Mod ZIP**, use the selected already-built output rather than archiving
a game/build directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\package_mod.ps1 `
  -BuildDirectory .\.godot\mono\temp\bin\Release `
  -OutputZip .\artifacts\RolltheSpire2_Mod.zip
```

The current `has_pck=false` package has exactly `INSTALL.txt`, `LICENSE`, `THIRD_PARTY_NOTICES.md`,
`RolltheSpire2/RolltheSpire2.dll` and `RolltheSpire2/RolltheSpire2.json`. It copies
no Corpus, game DLL/PCK, PDB or entire build directory and does not build, install
or upload anything. A future declared resource pack requires an explicit packaging
contract rather than silently dropping it. Match this built DLL to the accepted
source/candidate through the normal release receipt.

[Source and rights notices](THIRD_PARTY_NOTICES.md) accompany both source-package
kinds at their source root and the Mod ZIP beside `LICENSE`. Keep these notices
with external distributions, including separately prepared Workshop materials.

Do not package `.godot/`, `bin/`, `obj/`, `.build-evidence*`, `.rewrite-build-evidence*`, `.vs/`, `TestResults/`,
`__pycache__/`, `.artifacts/`, `.idea/`, PDBs or editor/build caches. Git metadata
(including a worktree's `.git` file), `local.props` and logs are excluded. Relative
output paths follow the PowerShell current location. Generated reference files in
`tools/AuthoredEventChecks/localization/` are excluded; the mod's `Texts/`
resources remain included. Reusing the same output ZIP does not include the old ZIP
in the new package; a staging-copy, validation or compression failure leaves that
old output in place.

Run source-package hygiene **inside the extracted package**, before building it:

```powershell
Expand-Archive -LiteralPath ./artifacts/RolltheSpire2_SourceCandidate.zip -DestinationPath ./artifacts/source-check
python ./artifacts/source-check/RolltheSpire2/tests/safety/verify_source_package_hygiene.py
```

For PublicSource, extract `RolltheSpire2_PublicSource.zip` into a separate directory
and run the same checker with `--package-kind PublicSource`. It checks selected
members and frozen map evidence hashes as well as ordinary hygiene. Never run
both package kinds into the same extraction directory and infer success from a
mixed tree.

Running this checker against a development checkout also sees repository evidence,
Git metadata and generated build files and may fail by design. Package hygiene does
not substitute for compilation, parity or Owner runtime acceptance.
