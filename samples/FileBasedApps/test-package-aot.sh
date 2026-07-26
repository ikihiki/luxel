#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -P -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)"
WORK="${LUXEL_AOT_FIXTURE_DIR:-/tmp/luxel-package-aot-fixture}"
BASE_WORK="$WORK/base"
RID="${LUXEL_AOT_RID:-linux-x64}"
PACKAGES="$WORK/packages"
PUBLISH="$WORK/publish"
LOG="$WORK/publish.log"

if [[ "$(uname -s)-$(uname -m)" != "Linux-x86_64" || "$RID" != "linux-x64" ]]; then
  echo "Luxel Native AOT v1 currently supports Linux x64 with RID linux-x64 only." >&2
  exit 1
fi
for command_name in clang file readelf ldd python3; do
  command -v "$command_name" >/dev/null 2>&1 || { echo "missing Native AOT prerequisite: $command_name" >&2; exit 1; }
done

rm -rf "$WORK"
LUXEL_PACKAGE_FIXTURE_DIR="$BASE_WORK" LUXEL_SKIP_PACKAGE_RUN=1 "$ROOT/samples/FileBasedApps/test-package.sh"
mkdir -p "$WORK/app" "$PACKAGES"
cp "$ROOT/samples/FileBasedApps/package/HelloLuxel.Package.Aot.cs" "$WORK/app/app.cs"
cat > "$WORK/app/NuGet.Config" <<CONFIG
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/><add key="luxel-local" value="$BASE_WORK/feed"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>
CONFIG

cd "$WORK/app"
NUGET_PACKAGES="$PACKAGES" dotnet restore app.cs --configfile NuGet.Config --no-cache -r "$RID"
set +e
NUGET_PACKAGES="$PACKAGES" dotnet publish app.cs -c Release -r "$RID" --no-restore -o "$PUBLISH" -p:TrimmerSingleWarn=false 2>&1 | tee "$LOG"
status=${PIPESTATUS[0]}
set -e
[[ $status -eq 0 ]] || exit "$status"

# Silk.NET 2.23 emits known loader/platform-discovery warnings but the explicit GLFW path is runtime-smoked below.
unexpected="$(grep -E 'IL[0-9]{4}' "$LOG" | grep -Ev 'Silk\.NET\.(Windowing\.Window\.TryAdd|Core\.Loader\.DefaultPathResolver)|Microsoft\.Extensions\.DependencyModel\.DependencyContext' || true)"
if [[ -n "$unexpected" ]]; then
  echo "unexpected Native AOT warning(s):" >&2
  echo "$unexpected" >&2
  exit 1
fi

exe="$PUBLISH/app"
file "$exe" | grep -Eq 'ELF 64-bit.*(pie executable|shared object)'
readelf -h "$exe" | grep -q 'Machine:.*X86-64'
if ldd "$exe" | grep -q 'not found'; then
  ldd "$exe" >&2
  exit 1
fi
for asset in shaders/raster2d_bounds.spv shaders/raster2d_bin.spv shaders/raster2d_fine.spv assets/fonts/BIZUDGothic-Regular.ttf assets/fonts/OFL.txt libglfw.so.3 libglfw.so.3.3 libHarfBuzzSharp.so; do
  test -f "$PUBLISH/$asset" || { echo "missing AOT published asset: $asset" >&2; exit 1; }
done

if [[ "${LUXEL_SKIP_AOT_RUN:-0}" != 1 ]]; then
  : "${DISPLAY:?DISPLAY must point to the Linux test desktop}"
  (cd /tmp && LUXEL_RUN_FRAMES=1 "$exe")
fi

echo "Native AOT package fixture passed: $WORK ($RID)"
