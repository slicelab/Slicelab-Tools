#!/usr/bin/env bash
#
# Assembles a universal (rh8_0-any) Yak package for the Rhino package manager.
#
#   ./yak/build-package.sh
#
# Requires, all present at once:
#   - src/Slicelab/bin/Release/net7.0/SlicelabTools.gha  (Release build)
#   - the three macOS .dylib wrappers, produced by the Mac native build and copied
#     next to the .gha by the csproj
#   - the three Windows .dll wrappers, built on the PC and dropped into yak/win-libs/
#
# The package ships both platforms' native libraries in one archive; DllImport picks
# the right one at runtime, so a single package serves Mac and Windows.
#
# This script never pushes. Publishing is a manual step — see yak/README.md.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BIN="$ROOT/src/Slicelab/bin/Release/net7.0"
WIN_LIBS="$ROOT/yak/win-libs"
STAGE="$ROOT/yak/dist"
YAK="${YAK:-/Applications/Rhino 8.app/Contents/Resources/bin/yak}"

MAC_LIBS=(TetgenWrapper.dylib MmgsWrapper.dylib ManifoldWrapper.dylib)
WINDOWS_LIBS=(TetgenWrapper.dll MmgsWrapper.dll ManifoldWrapper.dll)
# Managed dependencies that must travel with the .gha
DEPS=(PdfSharpCore.dll SixLabors.Fonts.dll SixLabors.ImageSharp.dll ICSharpCode.SharpZipLib.dll)

fail() { printf '\033[31mERROR:\033[0m %s\n' "$1" >&2; exit 1; }
ok()   { printf '\033[32m  ok\033[0m  %s\n' "$1"; }

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/src/Slicelab/Slicelab.csproj" | head -1)"
[ -n "$VERSION" ] || fail "could not read <Version> from Slicelab.csproj"
echo "Slicelab Tools $VERSION — building Yak package"

# --- Preflight -------------------------------------------------------------
[ -f "$BIN/SlicelabTools.gha" ] || fail "no .gha at $BIN
  Build it first:  dotnet build src/Slicelab/Slicelab.csproj --configuration Release"

# --- Maintainer verification (runs only when the private dev folder is present) ---
VERIFY="$ROOT/src/Slicelab/Internal/tools/verify-public-build.sh"
if [ -d "$ROOT/src/Slicelab/Internal" ]; then
  [ -x "$VERIFY" ] || fail "private folder present but verification script missing: $VERIFY"
  "$VERIFY" "$BIN" || fail "pre-publish verification failed — do not package this build"
  ok "maintainer verification passed"
else
  echo "  note: maintainer verification skipped (not applicable to public clones)"
fi

# Guard: the csproj copies native libs with Condition=Exists, so a build with no
# natives succeeds silently and produces a .gha that throws DllNotFoundException.
missing=()
for f in "${MAC_LIBS[@]}"; do [ -f "$BIN/$f" ] || missing+=("$BIN/$f"); done
if [ ${#missing[@]} -gt 0 ]; then
  fail "macOS native libraries missing:
$(printf '    %s\n' "${missing[@]}")
  Build them, then rebuild the plugin so the csproj copies them:
    for lib in tetgen mmg manifold; do
      mkdir -p native/\$lib/build && cd native/\$lib/build
      cmake .. -DCMAKE_BUILD_TYPE=Release && make && cd \"\$OLDPWD\"
    done
    dotnet build src/Slicelab/Slicelab.csproj --configuration Release"
fi

# Windows DLLs: prefer yak/win-libs/; otherwise fall back to the private dev
# folder's dist/win/ when it's present (the maintainer sync route).
have_all_win() { local d="$1" f; for f in "${WINDOWS_LIBS[@]}"; do [ -f "$d/$f" ] || return 1; done; }
ALT_WIN="$ROOT/src/Slicelab/Internal/dist/win"
if ! have_all_win "$WIN_LIBS" && have_all_win "$ALT_WIN"; then
  WIN_LIBS="$ALT_WIN"
  echo "  using Windows DLLs from $ALT_WIN"
fi

missing=()
for f in "${WINDOWS_LIBS[@]}"; do [ -f "$WIN_LIBS/$f" ] || missing+=("$f"); done
if [ ${#missing[@]} -gt 0 ]; then
  fail "Windows DLLs missing from yak/win-libs/:
$(printf '    %s\n' "${missing[@]}")
  Build them on the PC (x64 Native Tools Command Prompt for VS 2022):
    cd native\\<lib>\\build
    cmake .. -G \"Visual Studio 17 2022\" -A x64
    cmake --build . --config Release
  Then copy native\\<lib>\\build\\Release\\*.dll into yak/win-libs/ on the Mac."
fi

for f in "${DEPS[@]}"; do [ -f "$BIN/$f" ] || fail "managed dependency missing: $BIN/$f"; done

# --- Stage -----------------------------------------------------------------
rm -rf "$STAGE"
mkdir -p "$STAGE/misc"

cp "$BIN/SlicelabTools.gha" "$STAGE/"; ok "SlicelabTools.gha"
for f in "${DEPS[@]}";        do cp "$BIN/$f"      "$STAGE/"; ok "$f"; done
for f in "${MAC_LIBS[@]}";    do cp "$BIN/$f"      "$STAGE/"; ok "$f"; done
for f in "${WINDOWS_LIBS[@]}"; do cp "$WIN_LIBS/$f" "$STAGE/"; ok "$f"; done

cp "$ROOT/yak/icon.png" "$STAGE/"
cp "$ROOT/README.md"    "$STAGE/misc/"
cp "$ROOT/LICENSE"      "$STAGE/misc/LICENSE.txt"
cp "$ROOT/THIRD_PARTY_LICENSES.txt" "$STAGE/misc/"

sed "s/{{VERSION}}/$VERSION/" "$ROOT/yak/manifest.yml.template" > "$STAGE/manifest.yml"
ok "manifest.yml (version $VERSION)"

# --- Build -----------------------------------------------------------------
[ -x "$YAK" ] || fail "yak CLI not found at: $YAK
  Override with:  YAK=/path/to/yak ./yak/build-package.sh"

cd "$STAGE"
"$YAK" build

# Identify the package we just built by name. Globbing the yak/ folder instead would
# pick up packages from previous releases — and since yak publishes are irreversible,
# the printed commands must name one exact file rather than a pattern.
BUILT="$(ls -t ./*.yak 2>/dev/null | head -1)"
[ -n "$BUILT" ] || fail "yak build produced no .yak file in $STAGE"

PKG_NAME="$(basename "$BUILT")"
mv "$BUILT" "$ROOT/yak/$PKG_NAME"

case "$PKG_NAME" in
  *"$VERSION"*) ;;
  *) fail "built package '$PKG_NAME' does not carry version $VERSION — check manifest.yml" ;;
esac

echo
printf '\033[32mDone.\033[0m Package: %s\n' "$ROOT/yak/$PKG_NAME"

# Unquoted heredoc so the filename expands; \$YAK stays literal for copy-paste.
cat <<EOF

Next — publish (test server first, it resets daily and mistakes are free):

  YAK="/Applications/Rhino 8.app/Contents/Resources/bin/yak"
  "\$YAK" login
  "\$YAK" push --source https://test.yak.rhino3d.com yak/$PKG_NAME

Install it in Rhino from the test server, verify a TetGen, an mmg, and a Manifold
component all compute on BOTH Mac and Windows. Only then:

  "\$YAK" push yak/$PKG_NAME

WARNING: published versions can never be deleted or overwritten — only yanked
(unlisted). And the first push of a package name owns that name forever.
EOF
