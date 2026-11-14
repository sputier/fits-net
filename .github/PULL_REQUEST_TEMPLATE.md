Refs #

## What changes, and why

## Checks

- [ ] Tests for the change pass (`dotnet test tests/Eleris.FitsNet.Tests -c Release`), and a bug fix has a test that fails without it
- [ ] A damaged or forged file still fails with a `FitsException`
- [ ] The public API is documented, and the reference pages are regenerated (`dotnet run --project tools/DocGen`)
- [ ] The guide in `docs/` and the "Unreleased" section of `CHANGELOG.md` are updated
- [ ] If it is made to go faster: the old and the new code are measured in the same session (numbers in the description)
- [ ] A change of behaviour is intended, and said here
