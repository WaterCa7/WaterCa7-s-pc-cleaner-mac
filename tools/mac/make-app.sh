#!/bin/bash
# Builds "WaterCa7s PC Cleaner.app" and WaterCa7sPcCleaner-osx-arm64.zip in <out-dir> (macOS only):
#   tools/mac/make-app.sh out
# Signs with the certificate from MAC_SIGNING_P12 / MAC_SIGNING_PASSWORD when they are set, ad-hoc otherwise (sign.sh).
set -euo pipefail

out="${1:?usage: make-app.sh <out-dir>}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
project="$root/src/PcCleaner.Mac/PcCleaner.Mac.csproj"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$project" | head -n 1)"
if [[ -z "$version" ]]; then
  echo "No <Version> in $project" >&2
  exit 1
fi

mkdir -p "$out"
out="$(cd "$out" && pwd)"
publish="$out/publish"
app="$out/WaterCa7s PC Cleaner.app"
zip="$out/WaterCa7sPcCleaner-osx-arm64.zip"
rm -rf "$publish" "$app" "$zip"

# Single file: the managed assemblies live inside the executable, so Contents/MacOS holds only Mach-O files
# (the program and the native Avalonia/Skia/HarfBuzz libraries) — codesign treats every file there as code.
dotnet publish "$project" -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none -o "$publish"
for file in "$publish"/*; do
  if ! file -b "$file" | grep -q 'Mach-O'; then
    echo "Not a Mach-O file in the publish folder (it would break the signature): $file" >&2
    exit 1
  fi
done

mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$publish/." "$app/Contents/MacOS/"
cp "$root/src/PcCleaner.Mac/Assets/AppIcon.icns" "$app/Contents/Resources/AppIcon.icns"
sed "s/@VERSION@/$version/g" "$root/tools/mac/Info.plist" > "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist"
chmod +x "$app/Contents/MacOS/WaterCa7sPcCleaner"

"$root/tools/mac/sign.sh" "$app"

(cd "$out" && ditto -c -k --keepParent "WaterCa7s PC Cleaner.app" "$(basename "$zip")")
echo "$app ($version)"
echo "$zip ($(du -h "$zip" | cut -f1))"
