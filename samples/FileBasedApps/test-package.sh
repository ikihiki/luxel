#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -P -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)"
WORK="${LUXEL_PACKAGE_FIXTURE_DIR:-/tmp/luxel-package-fixture}"
FEED="$WORK/feed"; PACKAGES="$WORK/packages"; PUBLISH="$WORK/publish"
rm -rf "$WORK"; mkdir -p "$FEED" "$WORK/app"
dotnet restore "$ROOT/src/Framework/Luxel.Framework.UI/Luxel.Framework.UI.csproj"
dotnet pack "$ROOT/src/Framework/Luxel.Framework.UI/Luxel.Framework.UI.csproj" -c Debug -o "$FEED" --no-restore
python3 - "$ROOT/src/Framework/Luxel.Framework.UI/obj/project.assets.json" "$FEED" <<'PY'
import json, pathlib, sys, urllib.request
assets=json.load(open(sys.argv[1])); feed=pathlib.Path(sys.argv[2])
for key, metadata in assets['libraries'].items():
    if metadata.get('type') != 'package': continue
    name, version=key.rsplit('/', 1); lower=name.lower()
    destination=feed/f'{name}.{version}.nupkg'
    if not destination.exists():
        urllib.request.urlretrieve(f'https://api.nuget.org/v3-flatcontainer/{lower}/{version.lower()}/{lower}.{version.lower()}.nupkg', destination)
PY
cp "$ROOT/samples/FileBasedApps/package/HelloLuxel.Package.cs" "$WORK/app/app.cs"
cat > "$WORK/app/NuGet.Config" <<CONFIG
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/><add key="local" value="$FEED"/></packageSources></configuration>
CONFIG
cd "$WORK/app"
NUGET_PACKAGES="$PACKAGES" dotnet restore app.cs --configfile NuGet.Config --no-cache
NUGET_PACKAGES="$PACKAGES" dotnet build app.cs --no-restore
NUGET_PACKAGES="$PACKAGES" dotnet publish app.cs --no-restore -o "$PUBLISH"
if [[ "${LUXEL_SKIP_PACKAGE_RUN:-0}" != 1 ]]; then
  : "${DISPLAY:?DISPLAY must point to the Linux test desktop}"
  LUXEL_RUN_FRAMES=1 NUGET_PACKAGES="$PACKAGES" dotnet run --file "$WORK/app/app.cs" --no-restore
  (cd /tmp && LUXEL_RUN_FRAMES=1 "$PUBLISH/app")
fi
for asset in shaders/raster2d_bounds.spv shaders/raster2d_bin.spv shaders/raster2d_fine.spv assets/fonts/BIZUDGothic-Regular.ttf assets/fonts/OFL.txt libglfw.so.3.3; do
  test -f "$PUBLISH/$asset" || { echo "missing published asset: $asset" >&2; exit 1; }
done
echo "Package fixture passed: $WORK"
