#!/usr/bin/env bash
# Installs the mRemoteNG for Linux package on a clean system (a debian or ubuntu container, as root) and checks
# that it works there: every library resolves, --version answers, the app starts on a virtual X server and
# creates its connection file, and removing the package removes it.
#
# Usage (in the container): scripts/linux/test-deb.sh path/to/mremoteng_<version>_<arch>.deb
# START_TIMEOUT (seconds, default 20) is how long the app must keep running; raise it under emulation.
set -euo pipefail

deb="$(realpath "$1")"
export DEBIAN_FRONTEND=noninteractive

echo "== Installing $(basename "$deb") on $(sed -n 's/^PRETTY_NAME="\(.*\)"$/\1/p' /etc/os-release)"
apt-get update -qq
apt-get install -y -qq "$deb" xvfb xauth > /dev/null

echo "== Libraries"
missing="$(ldd /usr/lib/mremoteng/*.so /usr/lib/mremoteng/mRemoteNG.Avalonia 2>&1 | grep 'not found' | sort -u || true)"
for library in libicuuc.so libssl.so.3 libX11.so.6 libXcursor.so.1 libXext.so.6 libXfixes.so.3 libXi.so.6 \
               libXrandr.so.2 libICE.so.6 libSM.so.6 libfontconfig.so.1; do
    # Loaded at run time (dlopen), so ldd does not see them.
    ldconfig -p | grep -q "$library" || missing="$missing"$'\n'"$library (loaded at run time)"
done
if [ -n "$missing" ]; then
    echo "Missing:$missing" >&2
    exit 1
fi
echo "all found"

echo "== mremoteng --version"
mremoteng --version

echo "== The executable's helper modes (raw TCP relay; the askpass helper is the same executable)"
set +e
relay="$(mremoteng --raw-tcp 127.0.0.1 9 < /dev/null 2>&1)"
status=$?
set -e
if [ "$status" -ne 1 ] || ! grep -q "Could not connect to 127.0.0.1:9" <<< "$relay"; then
    echo "The relay did not answer as expected (exit code $status): $relay" >&2
    exit 1
fi
echo "$relay"

echo "== Starting on a virtual X server"
home="$(mktemp -d)"
set +e
HOME="$home" timeout "${START_TIMEOUT:-20}" xvfb-run -a mremoteng > "$home/output.txt" 2>&1
status=$?
set -e
# Still running when the time is up (timeout's 124) is what a started app does.
if [ "$status" -ne 124 ] || [ ! -f "$home/.local/share/mRemoteNG/confCons.xml" ]; then
    echo "The app did not start (exit code $status):" >&2
    cat "$home/output.txt" "$home/.local/state/mRemoteNG/mRemoteNG.log" 2>/dev/null >&2 || true
    exit 1
fi
echo "started and created $home/.local/share/mRemoteNG/confCons.xml"

echo "== Removing"
apt-get remove -y -qq mremoteng > /dev/null
if [ -e /usr/bin/mremoteng ] || [ -e /usr/lib/mremoteng ]; then
    echo "Files are left after removing the package" >&2
    exit 1
fi
echo "removed"
