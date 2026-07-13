# Yak packaging

Builds the `.yak` package that publishes Slicelab Tools to the **Rhino package manager**
(Rhino → Tools → Package Manager).

One **universal** package (`rh8_0-any`) serves both platforms: it contains the `.gha`
plus *both* sets of native wrappers (`.dylib` and `.dll`). At runtime `DllImport`
resolves the right one per platform, so there is a single package and a single version
number to keep in sync.

## Contents

| File | Purpose |
|---|---|
| `manifest.yml.template` | Package metadata. `{{VERSION}}` is substituted from the csproj at build time — **never hand-edit a version into it.** |
| `build-package.sh` | Assembles `yak/dist/`, writes the manifest, runs `yak build`. Never pushes. |
| `icon.png` | 256×256 package icon shown in the Rhino Package Manager. |
| `win-libs/` | *(gitignored)* Drop the three Windows `.dll` wrappers here, built on the PC. When this folder is empty the script also looks in the private development folder's `dist/win/`, if present. |
| `dist/` | *(gitignored)* Staging folder, rebuilt on every run. |

## Prerequisites — you need all of this at once

The package is a zip of finished binaries; nothing is compiled here.

**On the Mac:**
```bash
for lib in tetgen mmg manifold; do
  mkdir -p native/$lib/build && cd native/$lib/build
  cmake .. -DCMAKE_BUILD_TYPE=Release && make && cd -
done
dotnet build src/Slicelab/Slicelab.csproj --configuration Release
```
The csproj copies the three `.dylib`s next to the `.gha`.

> ⚠️ The csproj copies native libs with `Condition="Exists(...)"`, so a Release build
> **succeeds even with no native libraries at all** — producing a `.gha` that throws
> `DllNotFoundException` on the first TetGen/mmg/Manifold component. `build-package.sh`
> hard-fails on this rather than let it ship.

**On the Windows PC** (x64 Native Tools Command Prompt for VS 2022):
```
cd native\<lib>\build
cmake .. -G "Visual Studio 17 2022" -A x64
cmake --build . --config Release
```
Copy `native\<lib>\build\Release\*Wrapper.dll` (three files) into `yak/win-libs/` on the Mac.

## Build

```bash
./yak/build-package.sh
```
Produces `yak/SlicelabTools-<version>-rh8_0-any.yak`.

## Publish

```bash
YAK="/Applications/Rhino 8.app/Contents/Resources/bin/yak"

"$YAK" login                                     # Rhino Account; token lasts ~30 days

# Test server first — it resets daily, so mistakes cost nothing
"$YAK" push --source https://test.yak.rhino3d.com yak/SlicelabTools-*.yak
"$YAK" search --source https://test.yak.rhino3d.com --all --prerelease SlicelabTools

# Production — only after installing from test and verifying on BOTH platforms
"$YAK" push yak/SlicelabTools-*.yak
```

## Rules that bite

- **Published versions can never be deleted or overwritten.** `yak yank` only unlists
  them. Always test-server first.
- **The first push of a name owns that name forever.**
- **Maintainer verification:** when the private development folder is present, the
  build script runs an additional pre-publish verification of the build output and
  refuses to package if it fails. Public clones don't have that folder and skip the
  step — it is not needed to build a working package from this repository.
- **Windows users must run Rhino on .NET Core.** The `.gha` targets `net7.0`; Rhino 8 on
  Windows defaults to .NET Framework 4.8 and will silently not load it. Users run
  `SetDotNetRuntime` → NETCore → restart. This is called out in the package description.
