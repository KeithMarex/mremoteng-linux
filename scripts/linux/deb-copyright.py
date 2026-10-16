#!/usr/bin/env python3
"""Writes the Debian copyright file (DEP-5) of the mRemoteNG for Linux package.

Every NuGet package that ends up in the package is listed with its own licence, read from its nuspec. A licence
that cannot be combined with mRemoteNG's GPL-2.0-only (Apache-2.0, GPL-3.0, MS-PL, ...) or that is not known
stops the build, so a new dependency cannot slip one in.

Usage: deb-copyright.py <project.assets.json> <runtime identifier> <NuGet packages directory> <install directory>
"""

import html
import json
import os
import re
import sys

# SPDX expressions that may be shipped with GPL-2.0-only code, and their DEP-5 names.
COMPATIBLE = {"MIT": "Expat", "BSD-2-Clause": "BSD-2-clause", "BSD-3-Clause": "BSD-3-clause", "Zlib": "Zlib"}

# Packages whose nuspec points to a licence file instead of an SPDX expression, checked by hand.
LICENSE_FILES = {"Unicode.net": "MIT"}

EXPAT = """\
 Permission is hereby granted, free of charge, to any person obtaining a copy
 of this software and associated documentation files (the "Software"), to deal
 in the Software without restriction, including without limitation the rights
 to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 copies of the Software, and to permit persons to whom the Software is
 furnished to do so, subject to the following conditions:
 .
 The above copyright notice and this permission notice shall be included in all
 copies or substantial portions of the Software.
 .
 THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 SOFTWARE."""


def nuspec_field(text, name):
    match = re.search(rf"<{name}(?: [^>]*)?>([^<]*)</{name}>", text)
    return html.unescape(match.group(1).strip()) if match else None


def package_licence(package_id, nuspec):
    expression = re.search(r'<license type="expression">([^<]+)</license>', nuspec)
    if expression:
        return expression.group(1).strip()
    if re.search(r'<license type="file">', nuspec) and package_id in LICENSE_FILES:
        return LICENSE_FILES[package_id]
    return None


def main(assets_file, rid, packages_dir, install_dir):
    assets = json.load(open(assets_file, encoding="utf-8"))
    target = next((value for key, value in assets["targets"].items() if key.endswith("/" + rid)), None)
    if target is None:
        sys.exit(f"{assets_file} has no restore for {rid}; publish for {rid} first.")

    stanzas = []
    problems = []
    for name, info in sorted(target.items(), key=lambda item: item[0].lower()):
        if info.get("type") != "package":
            continue
        package_id, version = name.split("/")
        files = sorted({
            (asset.split("/")[-2] + "/" if section == "resource" else "") + os.path.basename(asset)
            for section in ("runtime", "native", "resource")
            for asset in info.get(section, {})
            if not asset.endswith("_._")
        })
        if not files:
            continue

        nuspec_path = os.path.join(packages_dir, package_id.lower(), version.lower(), package_id.lower() + ".nuspec")
        nuspec = open(nuspec_path, encoding="utf-8-sig").read()
        licence = package_licence(package_id, nuspec)
        if licence not in COMPATIBLE:
            problems.append(f"{package_id} {version}: {licence or 'unknown licence'}")
            continue

        copyright_line = nuspec_field(nuspec, "copyright") or nuspec_field(nuspec, "authors") or package_id
        url = nuspec_field(nuspec, "projectUrl") or f"https://www.nuget.org/packages/{package_id}"
        stanzas.append(
            "Files: " + " ".join(f"{install_dir}/{file}" for file in files) + "\n"
            f"Copyright: {copyright_line}\n"
            f"License: {COMPATIBLE[licence]}\n"
            f"Comment: {package_id} {version}, {url}\n")

    if problems:
        sys.exit("Dependencies whose licence is not known to be compatible with GPL-2.0-only:\n  " + "\n  ".join(problems))

    print("Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/")
    print("Upstream-Name: mRemoteNG for Linux")
    print("Source: https://github.com/KeithMarex/mremoteng-linux")
    print()
    print("Files: *")
    print("Copyright: 2007-2026 The mRemoteNG project and contributors")
    print("License: GPL-2")
    print()
    print(f"Files: {install_dir}/System.*.dll {install_dir}/Microsoft.*.dll {install_dir}/mscorlib.dll {install_dir}/netstandard.dll")
    print(f" {install_dir}/WindowsBase.dll {install_dir}/lib*.so {install_dir}/createdump")
    print("Copyright: .NET Foundation and Contributors")
    print("License: Expat")
    print("Comment: The .NET runtime, which includes third-party components listed in")
    print(" https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT")
    print()
    for stanza in stanzas:
        print(stanza)
    print("License: GPL-2")
    print(" On Debian systems, the complete text of the GNU General Public License")
    print(" version 2 can be found in /usr/share/common-licenses/GPL-2.")
    print()
    print("License: Expat")
    print(EXPAT)


if __name__ == "__main__":
    if len(sys.argv) != 5:
        sys.exit(__doc__)
    main(*sys.argv[1:])
