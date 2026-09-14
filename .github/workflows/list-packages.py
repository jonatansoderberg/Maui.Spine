"""Prints the lib/, build/, buildTransitive/ and native/ entries of every .nupkg in a directory, so a
release log shows that each package carries every target framework and its build assets."""
import sys
import zipfile
from pathlib import Path

packages = sorted(Path(sys.argv[1]).glob("*.nupkg"))
if not packages:
    sys.exit(f"no packages in {sys.argv[1]}")

for package in packages:
    print(f"== {package.name}")
    with zipfile.ZipFile(package) as zip_file:
        for name in sorted(zip_file.namelist()):
            if name.startswith(("lib/", "build/", "buildTransitive/", "native/")):
                print(f"   {name}")
