"""Checks the NuGet package before it is published: its content, its metadata, and that a project can really use it.

    python tools/verify_package.py <folder holding the .nupkg and .snupkg> [expected version]

The consumer project is restored from that folder only, so it uses the package and not the sources.
"""
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

PACKAGE_ID = "Eleris.FitsNet"
FRAMEWORKS = ["net8.0", "net10.0"]
problems: list[str] = []


def check(condition: bool, message: str) -> None:
    if not condition:
        problems.append(message)


def inspect(nupkg: Path, snupkg: Path, version: str) -> None:
    with zipfile.ZipFile(nupkg) as z:
        names = set(z.namelist())
        for tfm in FRAMEWORKS:
            for ext in ("dll", "xml"):
                check(f"lib/{tfm}/FitsNet.{ext}" in names, f"missing lib/{tfm}/FitsNet.{ext}")
        check("PACKAGE.md" in names, "missing PACKAGE.md (the readme shown on nuget.org)")
        check(not any(n.endswith(".pdb") for n in names), "the symbols must be in the .snupkg, not in the .nupkg")
        nuspec = ET.fromstring(z.read(f"{PACKAGE_ID}.nuspec"))
    ns = {"n": nuspec.tag.split("}")[0].strip("{")}
    meta = nuspec.find("n:metadata", ns)
    text = lambda name: (meta.findtext(f"n:{name}", namespaces=ns) or "").strip()
    check(text("id") == PACKAGE_ID, f"id is {text('id')!r}")
    check(text("version") == version, f"version is {text('version')!r}, expected {version!r}")
    for field in ("authors", "description", "copyright", "projectUrl", "tags", "readme"):
        check(text(field) != "", f"no {field}")
    license_node = meta.find("n:license", ns)
    check(license_node is not None and license_node.get("type") == "expression" and license_node.text == "MIT", "the license must be the SPDX expression MIT")
    repository = meta.find("n:repository", ns)
    check(repository is not None and repository.get("type") == "git" and (repository.get("commit") or "") != "", "the repository (url and commit) is missing: SourceLink needs it")
    dependencies = [d for d in nuspec.iter() if d.tag.endswith("}dependency")]
    check(not dependencies, f"the library must have no dependency, found {[d.get('id') for d in dependencies]}")
    with zipfile.ZipFile(snupkg) as z:
        for tfm in FRAMEWORKS:
            check(f"lib/{tfm}/FitsNet.pdb" in z.namelist(), f"the symbol package has no lib/{tfm}/FitsNet.pdb")


CONSUMER = """using FitsNet;
using FitsNet.Wcs;

var path = Path.Combine(AppContext.BaseDirectory, "consumer.fits");
var image = ImageHdu.Create<short>([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12], 4, 3);
image.Header.Set("OBJECT", "M42");
new FitsFile().Add(image).Save(path);

using var file = FitsFile.Open(path);
var read = (ImageHdu)file.Primary;
if (read.ReadPixels<short>()[11] != 12 || read.Header.Get<string>("OBJECT") != "M42") return 1;
Console.WriteLine("package OK on " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
return 0;
"""


def consume(folder: Path, version: str) -> None:
    work = Path(tempfile.mkdtemp(prefix="fitsnet-consumer-"))
    try:
        # The package comes from the folder only: a private cache keeps an older restore of the same version out.
        packages = work / "packages"
        for tfm in FRAMEWORKS:
            project = work / tfm
            project.mkdir()
            (project / "Consumer.csproj").write_text(f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>{tfm}</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RestoreSources>{folder.resolve()}</RestoreSources>
    <RestorePackagesPath>{packages}</RestorePackagesPath>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="{PACKAGE_ID}" Version="{version}" /></ItemGroup>
</Project>
""", encoding="utf-8")
            (project / "Program.cs").write_text(CONSUMER, encoding="utf-8")
            run = subprocess.run(["dotnet", "run", "-c", "Release"], cwd=project, capture_output=True, text=True)
            check(run.returncode == 0 and "package OK" in run.stdout, f"the consumer project fails on {tfm}:\n{run.stdout}{run.stderr}")
            if run.returncode == 0:
                print(run.stdout.strip())
    finally:
        shutil.rmtree(work, ignore_errors=True)


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    folder = Path(sys.argv[1])
    nupkgs = sorted(folder.glob(f"{PACKAGE_ID}.*.nupkg"))
    if len(nupkgs) != 1:
        print(f"expected exactly one {PACKAGE_ID} .nupkg in {folder}, found {len(nupkgs)}")
        return 1
    nupkg = nupkgs[0]
    version = sys.argv[2] if len(sys.argv) > 2 else re.fullmatch(rf"{re.escape(PACKAGE_ID)}\.(.+)\.nupkg", nupkg.name).group(1)
    snupkg = nupkg.with_suffix(".snupkg")
    check(snupkg.exists(), f"missing {snupkg.name}")
    if snupkg.exists():
        inspect(nupkg, snupkg, version)
    consume(folder, version)
    for p in problems:
        print("error:", p)
    print(f"{nupkg.name}: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
