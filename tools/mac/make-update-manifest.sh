#!/bin/bash
# Writes update.json for a release and signs it (ECDSA P-256 / SHA-256, DER — what the app checks):
#   tools/mac/make-update-manifest.sh <version> <zip> <notes.md> <private-key.pem> <out-dir>
# <out-dir> gets update.json, update.json.sig and a copy of the zip. The signature is verified with the public half of the
# key before the script ends. Written for the bash 3.2 that macOS ships.
set -euo pipefail

version="${1:?usage: make-update-manifest.sh <version> <zip> <notes.md> <private-key.pem> <out-dir>}"
zip="${2:?zip}"
notes="${3:?notes}"
key="${4:?private key}"
out="${5:?out dir}"

if [[ ! "$version" =~ ^[0-9]{1,4}\.[0-9]{1,4}\.[0-9]{1,5}$ ]]; then
  echo "Version must be like 1.2.3, not '$version'" >&2
  exit 1
fi
name="$(basename "$zip")"
size="$(stat -f %z "$zip")"
sha="$(shasum -a 256 "$zip" | cut -d' ' -f1)"
url="https://github.com/WaterCa7/WaterCa7-s-pc-cleaner-mac/releases/download/v$version/$name"
published="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
mkdir -p "$out"

# JSON through python3, so the release notes are escaped properly.
/usr/bin/python3 - "$version" "$published" "$notes" "$name" "$url" "$size" "$sha" > "$out/update.json" <<'PY'
import json
import sys

version, published, notes_path, name, url, size, sha = sys.argv[1:]
with open(notes_path, encoding="utf-8") as f:
    notes = f.read().strip()
manifest = {
    "version": version,
    "published": published,
    "notes": notes,
    "files": {"osx-arm64": {"name": name, "url": url, "size": int(size), "sha256": sha}},
}
print(json.dumps(manifest, ensure_ascii=False, indent=2))
PY

openssl dgst -sha256 -sign "$key" -out "$out/update.json.sig" "$out/update.json"
public="$(mktemp)"
openssl ec -in "$key" -pubout -out "$public" 2>/dev/null
openssl dgst -sha256 -verify "$public" -signature "$out/update.json.sig" "$out/update.json"
rm -f "$public"
if [[ "$(cd "$(dirname "$zip")" && pwd)/$name" != "$(cd "$out" && pwd)/$name" ]]; then
  cp "$zip" "$out/$name"
fi
echo "update.json for $version: $name, $size bytes, sha256 $sha"
