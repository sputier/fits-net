# Publishing the NuGet package

The package is `Eleris.FitsNet`. Its version is `<Version>` in `src/Eleris.FitsNet/Eleris.FitsNet.csproj`, the only place to change it
(`0.1.0`, or `0.2.0-preview.1` for a prerelease: nuget.org lists a version with a suffix as a prerelease).

## One-time setup

1. On [nuget.org](https://www.nuget.org), create an API key (Account > API Keys) limited to **Push new packages and package
   versions**, with the glob pattern `Eleris.FitsNet*`. The first push creates the package; for the keys of a later version, the key
   can be limited to that package.
2. On GitHub, in *Settings > Environments*, create the environment `nuget`, add the secret `NUGET_API_KEY` to it, and, if every
   publication must be approved by hand, add yourself as a required reviewer.
3. Optional: request the reservation of the `Eleris.` ID prefix from nuget.org (see "Id prefix reservation" in the nuget.org
   documentation). The packages with a reserved prefix show a verified badge.

## Releasing a version

1. Change `<Version>` in `src/Eleris.FitsNet/Eleris.FitsNet.csproj`, update the documentation if the API changed
   (`dotnet run --project tools/DocGen`) and commit.
2. Check the package locally:

   ```shell
   dotnet pack src/Eleris.FitsNet -c Release -o artifacts
   python tools/verify_package.py artifacts
   ```

   The script checks the content and the metadata of the package, then builds a new project for net8.0 and for net10.0 from the
   package alone, to read and write a file.
3. Tag and push. The tag is `v` followed by the version:

   ```shell
   git tag v0.1.0
   git push origin v0.1.0
   ```

The `Release` workflow then checks that the tag matches the version, builds, runs the tests, checks that the generated documentation
is up to date, packs, runs `tools/verify_package.py`, waits for the approval of the `nuget` environment if there is one, pushes the
`.nupkg` and its symbols (`.snupkg`) to nuget.org and creates the GitHub release.

A published version cannot be replaced. It can only be *unlisted* on nuget.org, so a mistake costs a new version number.

## After the first publication

* The Installation section of `docs/index.md` and of `README.md` says the package is not on NuGet yet: replace it with
  `dotnet add package Eleris.FitsNet`, then run `dotnet run --project tools/DocGen` (the pages are regenerated) and
  `python tools/check_docs.py`.
* To protect a later version against an accidental API break, set `<PackageValidationBaselineVersion>` in the project to the last
  published version.
