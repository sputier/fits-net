# Security policy

## Supported versions

Only the latest published version of [`Eleris.FitsNet`](https://www.nuget.org/packages/Eleris.FitsNet) receives fixes.

## Reporting a vulnerability

Please **do not open a public issue** for a security problem. Report it privately: on the
[Security tab](https://github.com/sputier/fits-net/security) of the repository, choose **Report a vulnerability**. You will get an answer
as soon as the maintainer can read it, and the report stays private until a fixed version is published.

A FITS file is data from outside, so these are security problems for this library:

* a file that makes the library fail with something other than a `FitsException` (an index, overflow or out-of-memory error, a crash);
* a file that makes it read or write memory or files that it should not;
* a file that makes it use far more memory or time than its size justifies (a decompression bomb, a forged size or count);
* a save that can replace or truncate a file it was not asked to, or leave a partial file in place.

Please include a small file that shows the problem (or the code that builds it), the version of the package, and the .NET version.

## What the library already does

The reading limits (`FitsReadOptions`) and the checks on sizes and counts are described in
[Reliability and limits](docs/reliability.md). The tests include fuzzing of damaged files (`FuzzTests`, `HardeningTests`) and a
differential check of the compression codecs against their first implementations.
