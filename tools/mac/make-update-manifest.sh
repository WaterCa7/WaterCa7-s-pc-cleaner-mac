#!/bin/bash
# Writes update.json for a release and signs it (ECDSA P-256 / SHA-256, DER — what the app checks):
#   tools/mac/make-update-manifest.sh <version> <notes.md> <private-key.pem> <out-dir> <zip>...
# Every zip must be named WaterCa7sPcCleaner-<platform>.zip (osx-arm64, osx-x64); it is listed under that platform.
# <out-dir> gets update.json, update.json.sig and a copy of the zips. The signature is verified with the public half of
# the key before the script ends. Written for the bash 3.2 that macOS ships.
set -euo pipefail

usage="usage: make-update-manifest.sh <version> <notes.md> <private-key.pem> <out-dir> <zip>..."
version="${1:?$usage}"
notes="${2:?$usage}"
key="${3:?$usage}"
out="${4:?$usage}"
shift 4
if [[ $# -eq 0 ]]; then
  echo "$usage" >&2
  exit 1
fi

if [[ ! "$version" =~ ^[0-9]{1,4}\.[0-9]{1,4}\.[0-9]{1,5}$ ]]; then
  echo "Version must be like 1.2.3, not '$version'" >&2
  exit 1
fi
published="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
mkdir -p "$out"

# One "platform name url size sha" line per zip for the JSON writer below.
files="$(mktemp)"
for zip in "$@"; do
  name="$(basename "$zip")"
  if [[ ! "$name" =~ ^WaterCa7sPcCleaner-(osx-arm64|osx-x64)\.zip$ ]]; then
    echo "The zip must be WaterCa7sPcCleaner-osx-arm64.zip or WaterCa7sPcCleaner-osx-x64.zip, not '$name'" >&2
    exit 1
  fi
  platform="${BASH_REMATCH[1]}"
  size="$(stat -f %z "$zip")"
  sha="$(shasum -a 256 "$zip" | cut -d' ' -f1)"
  url="https://github.com/WaterCa7/WaterCa7-s-pc-cleaner-mac/releases/download/v$version/$name"
  echo "$platform $name $url $size $sha" >> "$files"
  if [[ "$(cd "$(dirname "$zip")" && pwd)/$name" != "$(cd "$out" && pwd)/$name" ]]; then
    cp "$zip" "$out/$name"
  fi
  echo "update.json for $version: $platform $name, $size bytes, sha256 $sha"
done

# JSON through python3, so the release notes are escaped properly.
/usr/bin/python3 - "$version" "$published" "$notes" "$files" > "$out/update.json" <<'PY'
import json
import sys

version, published, notes_path, files_path = sys.argv[1:]
with open(notes_path, encoding="utf-8") as f:
    notes = f.read().strip()
files = {}
with open(files_path, encoding="utf-8") as f:
    for line in f:
        platform, name, url, size, sha = line.split()
        if platform in files:
            sys.exit(f"Two zips for {platform}")
        files[platform] = {"name": name, "url": url, "size": int(size), "sha256": sha}
manifest = {"version": version, "published": published, "notes": notes, "files": files}
print(json.dumps(manifest, ensure_ascii=False, indent=2))
PY
rm -f "$files"

openssl dgst -sha256 -sign "$key" -out "$out/update.json.sig" "$out/update.json"
public="$(mktemp)"
openssl ec -in "$key" -pubout -out "$public" 2>/dev/null
openssl dgst -sha256 -verify "$public" -signature "$out/update.json.sig" "$out/update.json"
rm -f "$public"
