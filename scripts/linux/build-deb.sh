#!/usr/bin/env bash
# Builds the mRemoteNG for Linux Debian package: a self-contained .NET build (Debian ships no .NET) in
# /usr/lib/mremoteng, started as /usr/bin/mremoteng, with desktop entry, icons and AppStream metadata.
#
# Usage: scripts/linux/build-deb.sh [amd64|arm64] [output directory]
#
# The maintainer is DEB_MAINTAINER ("Name <email>"), or else the author of the last commit. Building for arm64
# needs aarch64-linux-gnu-strip (binutils-aarch64-linux-gnu), or another strip for aarch64 in STRIP.
#
# Depends: what the bundled binaries link against, as dpkg-shlibdeps finds it for amd64 (libc6, libgcc-s1,
# libstdc++6, libfontconfig1), plus what .NET and Avalonia load at run time: ICU, OpenSSL and the X11 client
# libraries. The alternatives cover Debian 12 (libicu72, libssl3), Debian 13 (libicu76, libssl3t64) and
# Ubuntu 24.04 (libicu74, libssl3t64). See packaging/debian/control.in.
set -euo pipefail

arch="${1:-amd64}"
case "$arch" in
    amd64) rid=linux-x64; strip_tool="${STRIP:-strip}" ;;
    arm64) rid=linux-arm64; strip_tool="${STRIP:-aarch64-linux-gnu-strip}" ;;
    *) echo "usage: $0 [amd64|arm64] [output directory]" >&2; exit 2 ;;
esac
command -v "$strip_tool" > /dev/null \
    || { echo "$strip_tool not found (for arm64: apt install binutils-aarch64-linux-gnu, or set STRIP)" >&2; exit 1; }

repo="$(cd "$(dirname "$0")/../.." && pwd)"
out="$(mkdir -p "${2:-$repo/artifacts}" && cd "${2:-$repo/artifacts}" && pwd)"
project="$repo/mRemoteNG.Avalonia/mRemoteNG.Avalonia.csproj"
packaging="$repo/packaging"
id=io.github.keithmarex.mremoteng

version="$(dotnet msbuild "$project" -getProperty:Version)"
maintainer="${DEB_MAINTAINER:-$(git -C "$repo" log -1 --format='%an <%ae>')}"
maintainer_sed="$(printf '%s' "$maintainer" | sed 's/[|&\\]/\\&/g')"

# A release is described in the changelog and the AppStream metadata before it is built.
grep -q "^mremoteng ($version)" "$packaging/debian/changelog.in" \
    || { echo "packaging/debian/changelog.in has no entry for $version" >&2; exit 1; }
grep -q "<release version=\"$version\"" "$packaging/linux/$id.metainfo.xml" \
    || { echo "packaging/linux/$id.metainfo.xml has no release $version" >&2; exit 1; }

stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
app="$stage/usr/lib/mremoteng"
doc="$stage/usr/share/doc/mremoteng"

echo "Publishing $version for $rid"
dotnet publish "$project" -c Release -r "$rid" --self-contained -p:Version="$version" -o "$app" -nologo -v:q
# Optional LTTng tracing; it needs liblttng-ust, which nothing else does.
rm -f "$app/libcoreclrtraceptprovider.so"
# Strip as dh_strip does: shared libraries of the symbols they do not need, all binaries of .comment and .note.
find "$app" -name '*.so' -exec "$strip_tool" --strip-unneeded --remove-section=.comment --remove-section=.note {} +
"$strip_tool" --remove-section=.comment --remove-section=.note "$app/mRemoteNG.Avalonia" "$app/createdump"

mkdir -p "$stage/usr/bin" "$doc" "$stage/usr/share/applications" "$stage/usr/share/metainfo" \
         "$stage/usr/share/lintian/overrides"
ln -s ../lib/mremoteng/mRemoteNG.Avalonia "$stage/usr/bin/mremoteng"
cp "$packaging/linux/$id.desktop" "$stage/usr/share/applications/"
cp "$packaging/linux/$id.metainfo.xml" "$stage/usr/share/metainfo/"
for size in 16 24 32 48 64 128 256; do
    install -D -m 0644 "$packaging/linux/icons/$size.png" "$stage/usr/share/icons/hicolor/${size}x$size/apps/$id.png"
done
cp "$packaging/debian/lintian-overrides" "$stage/usr/share/lintian/overrides/mremoteng"

# Right after the publish: the restore it did is the one that lists the bundled packages.
python3 "$repo/scripts/linux/deb-copyright.py" "$repo/mRemoteNG.Avalonia/obj/project.assets.json" "$rid" \
    "${NUGET_PACKAGES:-$HOME/.nuget/packages}" usr/lib/mremoteng > "$doc/copyright"
sed "s|@MAINTAINER@|$maintainer_sed|" "$packaging/debian/changelog.in" | gzip -9n > "$doc/changelog.gz"

# Debian permissions: directories and executables 0755, everything else (shared libraries too) 0644.
find "$stage" -type d -exec chmod 0755 {} +
find "$stage" -type f -exec chmod 0644 {} +
chmod 0755 "$app/mRemoteNG.Avalonia" "$app/createdump"

mkdir -p "$stage/DEBIAN"
installed_size="$(du -sk --exclude=DEBIAN "$stage" | cut -f1)"
sed -e "s/@VERSION@/$version/" -e "s/@ARCH@/$arch/" -e "s|@MAINTAINER@|$maintainer_sed|" \
    -e "s/@INSTALLED_SIZE@/$installed_size/" "$packaging/debian/control.in" > "$stage/DEBIAN/control"
(cd "$stage" && find usr -type f -print0 | sort -z | xargs -0 md5sum) > "$stage/DEBIAN/md5sums"
chmod 0644 "$stage/DEBIAN/control" "$stage/DEBIAN/md5sums"

deb="$out/mremoteng_${version}_${arch}.deb"
dpkg-deb --root-owner-group -Zxz --build "$stage" "$deb" > /dev/null
echo "$deb"
