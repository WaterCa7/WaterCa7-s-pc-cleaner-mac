#!/bin/bash
# Signs an .app bundle (macOS only):  tools/mac/sign.sh <app>
# With MAC_SIGNING_P12 (base64 of a .p12) and MAC_SIGNING_PASSWORD it uses the self-signed certificate
# "WaterCa7s PC Cleaner Signing": the designated requirement then stays the same from version to version, so macOS keeps
# Full Disk Access and other permissions after an update. Without them the signature is ad-hoc ("-").
# Every Mach-O file inside Contents/MacOS is signed first, then the bundle itself. No hardened runtime: the app is not
# notarized, and .NET needs no extra entitlements without it.
# Written for the bash 3.2 that macOS ships.
set -euo pipefail

app="${1:?usage: sign.sh <app>}"
identity="-"
keychain=""

if [[ -n "${MAC_SIGNING_P12:-}" ]]; then
  work="$(mktemp -d)"
  keychain="$work/signing.keychain-db"
  password="$(uuidgen)"
  cleanup() {
    security delete-keychain "$keychain" >/dev/null 2>&1 || true
    rm -rf "$work"
  }
  trap cleanup EXIT
  printf '%s' "$MAC_SIGNING_P12" | base64 --decode > "$work/cert.p12"
  echo "cert.p12: $(wc -c < "$work/cert.p12" | tr -d ' ') bytes, password: ${#MAC_SIGNING_PASSWORD} characters"
  # Tells a wrong password from a format the keychain does not accept (prints nothing secret).
  if MAC_SIGNING_PASSWORD="${MAC_SIGNING_PASSWORD:-}" openssl pkcs12 -in "$work/cert.p12" -noout -passin env:MAC_SIGNING_PASSWORD >/dev/null 2>&1; then
    echo "openssl: the password opens cert.p12"
  else
    echo "openssl: cert.p12 does not open with this password" >&2
  fi
  security create-keychain -p "$password" "$keychain"
  security set-keychain-settings -lut 3600 "$keychain"
  security unlock-keychain -p "$password" "$keychain"
  security import "$work/cert.p12" -k "$keychain" -P "${MAC_SIGNING_PASSWORD:-}" -T /usr/bin/codesign
  security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$password" "$keychain" >/dev/null
  # The temporary keychain first in the search list, then the existing ones.
  existing="$(security list-keychains -d user | sed 's/[" ]//g' | tr '\n' ' ')"
  # shellcheck disable=SC2086
  security list-keychains -d user -s "$keychain" $existing
  identity="WaterCa7s PC Cleaner Signing"
  echo "Signing with the certificate \"$identity\""
else
  echo "MAC_SIGNING_P12 is not set: ad-hoc signature"
fi

sign() {
  if [[ -n "$keychain" ]]; then
    codesign --force --timestamp=none --sign "$identity" --keychain "$keychain" "$1"
  else
    codesign --force --timestamp=none --sign "$identity" "$1"
  fi
}

main="$app/Contents/MacOS/WaterCa7sPcCleaner"
while IFS= read -r -d '' file; do
  if [[ "$file" != "$main" ]] && file -b "$file" | grep -q 'Mach-O'; then
    sign "$file"
  fi
done < <(find "$app/Contents/MacOS" -type f -print0)
sign "$app"

codesign --verify --deep --strict --verbose=2 "$app"
echo "Designated requirement:"
codesign -d -r- "$app" 2>&1
