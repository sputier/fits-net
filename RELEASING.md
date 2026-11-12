# Publishing the NuGet package

The package is `Eleris.FitsNet`. Its version is `<Version>` in `src/Eleris.FitsNet/Eleris.FitsNet.csproj`, the only place to change it
(`0.1.0`, or `0.2.0-preview.1` for a prerelease: nuget.org lists a version with a suffix as a prerelease).

## One-time setup

The workflow publishes with **trusted publishing**: no API key is stored anywhere. GitHub proves to nuget.org which repository,
workflow and environment is running, and nuget.org answers with an API key that is valid for one hour.

1. On [nuget.org](https://www.nuget.org), click your user name, choose **Trusted Publishing** and add a policy:
   *Repository Owner* `sputier`, *Repository* `fits-net`, *Workflow File* `release.yml` (the file name only), *Environment* `nuget`,
   scopes **Push new packages and package versions** with the glob pattern `Eleris.FitsNet*`. The policy is temporary (7 days) until
   a first publication succeeds, then it is permanent.
2. On GitHub, in *Settings > Environments*, create the environment `nuget`, add the secret `NUGET_USER` to it (your nuget.org profile
   name, not your e-mail address), and, if every publication must be approved by hand, add yourself as a required reviewer.
3. Optional: request the reservation of the `Eleris.` ID prefix from nuget.org (see "Id prefix reservation" in the nuget.org
   documentation). The packages with a reserved prefix show a verified badge.

## Releasing a version

1. Change `<Version>` in `src/Eleris.FitsNet/Eleris.FitsNet.csproj`, add a section `## <version>` to `CHANGELOG.md` (it becomes the
   notes of the GitHub release; the workflow stops if it is missing), update the documentation if the API changed
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

The `Release` workflow then checks that the tag matches the version and that the changelog has notes for it, builds, runs the tests,
checks that the generated documentation is up to date, packs, runs `tools/verify_package.py`, waits for the approval of the `nuget`
environment if there is one, pushes the `.nupkg` and its symbols (`.snupkg`) to nuget.org and creates the GitHub release with the
notes of the changelog.

### Dry run

*Actions > Release > Run workflow* runs the same workflow from a branch without a tag: every check and the packing are done, the
package is kept as an artifact of the run, and nothing is published (the `publish` job only runs for a tag). Do it before the first
tag, and after any change to the workflow.

A published version cannot be replaced. It can only be *unlisted* on nuget.org, so a mistake costs a new version number.

## After a publication

* `<PackageValidationBaselineVersion>` in the project is the last published version (`0.2.0`, on nuget.org since 2026-11-10):
  `dotnet pack` fails on any API break against it. After a publication, set it to the version just published.
