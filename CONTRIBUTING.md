# Contributing to Eleris.FitsNet

Thank you for looking at the code. Bug reports, questions, and pull requests are welcome. By taking part you agree to the
[code of conduct](CODE_OF_CONDUCT.md), and your contribution is released under the [MIT license](LICENSE).

## Before you start

* A **bug**: open an issue with the smallest FITS file (or the code that builds it) that shows it. Use the bug template.
* A **security problem**: do not open an issue, see [SECURITY.md](SECURITY.md).
* A **new feature** or a change of behaviour: open an issue first, so that the design is agreed before the work is done. The
  [standard coverage](docs/standard-coverage.md) lists what is supported and what is not yet.

## Build and test

You need the .NET 10 SDK, and the .NET 8 runtime to run the tests for net8.0 (the library targets net8.0 and net10.0).

```shell
dotnet build
dotnet test tests/Eleris.FitsNet.Tests -c Release
```

Some tests compare the library with astropy: they need Python with `numpy` and `astropy` (`pip install numpy astropy`). Without them,
run the others with `FITSNET_SKIP_INTEROP=1`.

## What a pull request needs

* **Tests.** A change is finished when automated tests for it pass. A bug fix comes with a test that fails without it. For a
  codec or a parser, compare with a simple reference implementation, byte for byte or bit for bit, as the tests for the Rice codec do
  (`ReferenceRice*.cs`).
* **Hostile input.** A damaged or forged file must fail with a `FitsException`, never with an index, overflow or out-of-memory error.
* **Documentation.** Public members have XML documentation. After a change of the public API, regenerate the reference pages:

  ```shell
  dotnet run --project tools/DocGen
  ```

  Update the guide in `docs/` when the behaviour changes. The documentation is checked by the continuous integration
  (`pip install -r docs/requirements.txt`, then `python tools/check_docs.py` and `mkdocs build --strict`).
* **Compatibility.** A minor version only adds to the API: package validation compares the package with the previous version. Say in
  the pull request when a change of behaviour is intended.
* **Speed.** A change made to go faster comes with measures. The benchmarks are in `benchmarks/` ([how to run them](benchmarks/README.md));
  compare the old and the new code in the same session, because the clock of a laptop drifts.
* **Changelog.** Add a line to the "Unreleased" section of [CHANGELOG.md](CHANGELOG.md).

## Style

The code follows the style of the file it is in (see `.editorconfig`). Text files use LF line endings (see `.gitattributes`). Commit
messages are in English: a short summary line, then a paragraph that says why.

## Releases

Maintainers only: [RELEASING.md](RELEASING.md).
