#!/usr/bin/env bash
set -euo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
release="$root/bin/release"
asset=legacy-ipad-charge-linux-x64.tar.gz
mkdir -p -- "$release"
stage=$(mktemp -d "$release/linux-package.XXXXXXXX")
trap 'rm -rf -- "$stage"' EXIT

cd -- "$root"
"${LEGACY_IPAD_DOTNET:-dotnet}" publish src/LegacyIpadCharge -c Release -r linux-x64 --self-contained true \
    -p:Platform=AnyCPU -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=None -o "$stage/publish" -warnaserror

mkdir -- "$stage/content"
install -m 0755 "$stage/publish/legacy-ipad-charge" "$stage/content/legacy-ipad-charge"
install -m 0644 docs/linux.md "$stage/content/README.md"
install -m 0644 LICENSE "$stage/content/LICENSE"

pack=$(python3 - <<'PY'
import json
from pathlib import Path
import re

assets = json.loads(Path('src/LegacyIpadCharge/obj/project.assets.json').read_text(encoding='utf-8'))
runtime = 'Microsoft.NETCore.App.Runtime.linux-x64'
dependencies = assets['project']['frameworks']['net10.0']['downloadDependencies']
matches = [item['version'] for item in dependencies if item['name'] == runtime]
if len(matches) != 1 or not (version := re.fullmatch(r'\[([^,\s]+),\s*\1\]', matches[0])):
    raise SystemExit('Could not identify the exact published .NET runtime pack.')

for folder in assets['packageFolders']:
    package = Path(folder) / runtime.lower() / version[1]
    if (package / 'LICENSE.TXT').is_file() and (package / 'THIRD-PARTY-NOTICES.TXT').is_file():
        print(package)
        break
else:
    raise SystemExit('Missing the published runtime pack and its notices.')
PY
)
install -m 0644 "$pack/LICENSE.TXT" "$stage/content/dotnet-LICENSE.txt"
install -m 0644 "$pack/THIRD-PARTY-NOTICES.TXT" "$stage/content/dotnet-THIRD-PARTY-NOTICES.txt"

tar -czf "$stage/$asset" -C "$stage/content" .
(cd -- "$stage" && sha256sum "$asset" > "$asset.sha256")
mv -- "$stage/$asset" "$stage/$asset.sha256" "$release/"
cat -- "$release/$asset.sha256"
