#!/bin/bash
# Saves the output of read-only macOS tools for the test fixtures (tests/PcCleaner.Mac.Tests/Fixtures):
#   tools/mac/capture-fixtures.sh <out-dir> [app]
# Nothing on the system is changed. The user name, computer and host names, serial number, hardware UUID, MAC addresses
# and long token-like strings are replaced by placeholders — still look the files through before committing any.
# Every command gets <name>.txt (stdout), <name>.err.txt (stderr, when not empty) and <name>.exit (exit code).
# Written for the bash 3.2 that macOS ships.
set -uo pipefail

out="${1:?usage: capture-fixtures.sh <out-dir> [app]}"
app="${2:-}"
mkdir -p "$out"

export SCRUB_USER="$(id -un)"
export SCRUB_HOST="$(hostname -s 2>/dev/null || true)"
export SCRUB_COMPUTER="$(scutil --get ComputerName 2>/dev/null || true)"
export SCRUB_LOCAL="$(scutil --get LocalHostName 2>/dev/null || true)"
export SCRUB_SERIAL="$(ioreg -rd1 -c IOPlatformExpertDevice 2>/dev/null | awk -F'"' '/IOPlatformSerialNumber/ {print $4}')"
export SCRUB_UUID="$(ioreg -rd1 -c IOPlatformExpertDevice 2>/dev/null | awk -F'"' '/IOPlatformUUID/ {print $4}')"

scrub() {
  perl -pi -e '
    for my $p (["SCRUB_COMPUTER", "Test Mac"], ["SCRUB_LOCAL", "Test-Mac"], ["SCRUB_HOST", "testhost"],
               ["SCRUB_USER", "testuser"], ["SCRUB_SERIAL", "XXXXXXXXXX"], ["SCRUB_UUID", "00000000-0000-0000-0000-000000000000"]) {
      my $v = $ENV{$p->[0]};
      next unless defined $v && length($v) >= 3;
      s/\Q$v\E/$p->[1]/g;
    }
    s/\b([0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}\b/00:00:00:00:00:00/g;
    s/("serial_number"\s*:\s*")[^"]*/${1}XXXXXXXXXX/g;
    s/("(?:platform_UUID|provisioning_UDID)"\s*:\s*")[^"]*/${1}00000000/g;
    s/[A-Za-z0-9+=_-]{40,}/<hidden>/g;
  ' "$1"
}

# run <name> <command...>: stdout and stderr apart. run2: both into <name>.txt (codesign and spctl write to stderr).
run() {
  local name="$1"
  shift
  perl -e 'alarm shift; exec @ARGV' 120 "$@" > "$out/$name.txt" 2> "$out/$name.err.txt"
  echo $? > "$out/$name.exit"
  scrub "$out/$name.txt"
  scrub "$out/$name.err.txt"
  [[ -s "$out/$name.err.txt" ]] || rm -f "$out/$name.err.txt"
}

run2() {
  local name="$1"
  shift
  perl -e 'alarm shift; exec @ARGV' 120 "$@" > "$out/$name.txt" 2>&1
  echo $? > "$out/$name.exit"
  scrub "$out/$name.txt"
}

uid="$(id -u)"

# Stage 1: the facts about this Mac.
run sw_vers /usr/bin/sw_vers
run sysctl-boottime /usr/sbin/sysctl -n kern.boottime
run sysctl-memsize /usr/sbin/sysctl -n hw.memsize
run hardware /usr/sbin/system_profiler SPHardwareDataType -json
run df-data /bin/df -k -P /System/Volumes/Data
run df-root /bin/df -k -P /
run computername /usr/sbin/scutil --get ComputerName

# Stage 2: disks and cleaning (listings only, nothing is read inside the files).
run diskutil-root /usr/sbin/diskutil info -plist /
run tmutil-snapshots /usr/bin/tmutil listlocalsnapshots /
run df-all /bin/df -k -P
run ps-user /bin/ps -xww -o comm=
run ls-caches /bin/ls -la "$HOME/Library/Caches"
run ls-logs /bin/ls -la "$HOME/Library/Logs"
run ls-trash /bin/ls -la "$HOME/.Trash"
run du-caches /usr/bin/du -sk "$HOME/Library/Caches" "$HOME/Library/Logs"

# Stage 3: processes, startup, signatures.
run ps-comm /bin/ps -axww -o pid=,ppid=,uid=,pcpu=,rss=,etime=,comm=
run ps-args /bin/ps -axww -o pid=,args=
run launchctl-list /bin/launchctl list
run launchctl-print-gui /bin/launchctl print "gui/$uid"
run launchctl-disabled-gui /bin/launchctl print-disabled "gui/$uid"
run launchctl-disabled-system /bin/launchctl print-disabled system
run ls-launch /bin/ls -la "$HOME/Library/LaunchAgents" /Library/LaunchAgents /Library/LaunchDaemons
run launchctl-print-sshd /bin/launchctl print system/com.openssh.sshd
run launchctl-print-finder /bin/launchctl print "gui/$uid/com.apple.Finder"
run launchctl-print-missing /bin/launchctl print "gui/$uid/io.github.waterca7.no-such-service"
for plist in /Library/LaunchDaemons/*.plist /Library/LaunchAgents/*.plist "$HOME"/Library/LaunchAgents/*.plist; do
  [[ -f "$plist" ]] || continue
  name="$(basename "$plist" .plist)"
  run "launchd-plist-$name" /usr/bin/plutil -convert xml1 -o - "$plist"
  label="$(/usr/libexec/PlistBuddy -c 'Print :Label' "$plist" 2>/dev/null || true)"
  if [[ -n "$label" ]]; then
    run "launchctl-print-system-$name" /bin/launchctl print "system/$label"
    run "launchctl-print-gui-$name" /bin/launchctl print "gui/$uid/$label"
  fi
done
run sshd-plist /usr/bin/plutil -convert xml1 -o - /System/Library/LaunchDaemons/com.openssh.sshd.plist
run2 codesign-ls /usr/bin/codesign -dv --verbose=2 /bin/ls
run2 codesign-calculator /usr/bin/codesign -dv --verbose=2 /System/Applications/Calculator.app
dotnet_path="$(command -v dotnet || true)"
if [[ -n "$dotnet_path" ]]; then
  run2 codesign-dotnet /usr/bin/codesign -dv --verbose=2 "$(perl -MCwd -e 'print Cwd::abs_path(shift)' "$dotnet_path")"
fi
run2 codesign-verify-ls /usr/bin/codesign --verify --strict /bin/ls
run2 spctl-calculator /usr/sbin/spctl --assess --type execute -vv /System/Applications/Calculator.app
if [[ -n "$app" && -d "$app" ]]; then
  run2 codesign-app /usr/bin/codesign -dv --verbose=2 "$app"
  run2 codesign-verify-app /usr/bin/codesign --verify --deep --strict "$app"
  run2 codesign-requirement-app /usr/bin/codesign -d -r- "$app"
  run2 spctl-app /usr/sbin/spctl --assess --type execute -vv "$app"
fi

# Stage 4: programs (bundle facts and folder listings; nothing inside the users' data is read).
run mdls-calculator /usr/bin/mdls -name kMDItemLastUsedDate -raw /System/Applications/Calculator.app
run ls-applications /bin/ls -la /Applications /Applications/Utilities "$HOME/Applications"
run ls-system-applications /bin/ls -1 /System/Applications /System/Applications/Utilities
run ls-app-support /bin/ls -la "$HOME/Library/Application Support"
run ls-preferences /bin/ls -la "$HOME/Library/Preferences" "$HOME/Library/Preferences/ByHost"
run ls-containers /bin/ls -la "$HOME/Library/Containers"
run ls-group-containers /bin/ls -la "$HOME/Library/Group Containers"
run ls-saved-state /bin/ls -la "$HOME/Library/Saved Application State"
run ls-httpstorages /bin/ls -la "$HOME/Library/HTTPStorages" "$HOME/Library/WebKit"
for bundle in /Applications/*.app /Applications/*/*.app "$HOME"/Applications/*.app; do
  [[ -d "$bundle" ]] || continue
  name="$(basename "$bundle" .app | LC_ALL=C tr -c 'A-Za-z0-9._\n-' '_')"
  run "app-plist-$name" /usr/bin/plutil -convert xml1 -o - "$bundle/Contents/Info.plist"
  exe="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$bundle/Contents/Info.plist" 2>/dev/null || true)"
  if [[ -n "$exe" ]]; then
    run "app-lipo-$name" /usr/bin/lipo -archs "$bundle/Contents/MacOS/$exe"
  fi
  run "app-lastused-$name" /usr/bin/mdls -name kMDItemLastUsedDate -raw "$bundle"
  run "app-contents-$name" /bin/ls -la "$bundle/Contents"
done

# Stage 5: security and network.
run spctl-status /usr/sbin/spctl --status
run csrutil /usr/bin/csrutil status
run fdesetup /usr/bin/fdesetup status
run firewall-global /usr/libexec/ApplicationFirewall/socketfilterfw --getglobalstate
run firewall-stealth /usr/libexec/ApplicationFirewall/socketfilterfw --getstealthmode
run scutil-proxy /usr/sbin/scutil --proxy
run scutil-dns /usr/sbin/scutil --dns
run networksetup-services /usr/sbin/networksetup -listallnetworkservices
run profiles /usr/sbin/system_profiler SPConfigurationProfileDataType -json
run softwareupdate-prefs /usr/bin/plutil -convert xml1 -o - /Library/Preferences/com.apple.SoftwareUpdate.plist
run loginwindow-prefs /usr/bin/plutil -convert xml1 -o - /Library/Preferences/com.apple.loginwindow.plist
run xprotect /usr/bin/plutil -convert xml1 -o - /Library/Apple/System/Library/CoreServices/XProtect.bundle/Contents/Info.plist
run hosts /bin/cat /etc/hosts
run softwareupdate-list /usr/sbin/softwareupdate -l

echo "Saved to $out:"
ls -1 "$out"
