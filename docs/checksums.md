# Checksums

Two optional keywords let a reader detect that an HDU has been damaged:

- `DATASUM` is the checksum of the data unit;
- `CHECKSUM` is the checksum of the whole HDU, header and data. It is chosen so that the sum of the HDU is `-0` in ones' complement arithmetic.

The algorithm is the one of appendix J of the standard, and the values are the same as those of astropy.

## Write and verify

Pass a [`ChecksumPolicy`](api/FitsNet.ChecksumPolicy.md) to `Save`. `VerifyChecksum()` checks one HDU; `VerifyChecksums()` on the file checks all of them. Both return a [`ChecksumResult`](api/FitsNet.ChecksumResult.md), with a [`ChecksumStatus`](api/FitsNet.ChecksumStatus.md) for each keyword.

| `ChecksumStatus` | Meaning |
| --- | --- |
| `Missing` | The keyword is not in the header. |
| `Valid` | The value matches the data. |
| `Invalid` | The value does not match, or cannot be read. |

<!-- example: Checksums.WriteVerify -->
```csharp
using var source = FitsFile.Open("m42.fits");

// Always writes CHECKSUM and DATASUM in every HDU.
source.Save("m42-sums.fits", ChecksumPolicy.Always);

using var file = FitsFile.Open("m42-sums.fits");
foreach (ChecksumResult result in file.VerifyChecksums())
    Console.WriteLine($"CHECKSUM {result.Checksum}, DATASUM {result.DataSum}");

// One HDU at a time:
ChecksumResult primary = file.Primary.VerifyChecksum();
Console.WriteLine(primary.Checksum == ChecksumStatus.Valid);     // True
```
<!-- /example -->

## Policies

| `ChecksumPolicy` | On `Save` |
| --- | --- |
| `Auto` (default) | An HDU that has `CHECKSUM` or `DATASUM` keeps them valid: they are recomputed if the HDU changed. An HDU without them gets none. |
| `Always` | Fresh `CHECKSUM` and `DATASUM` in every HDU. |
| `Never` | Both keywords are removed from every HDU. |

Use `Never` when other programs edit the file afterwards, since they do not update the sums, and `Always` for files you archive or send.

<!-- example: Checksums.Policies -->
```csharp
using var file = FitsFile.Open("m42-sums.fits");

// Auto (the default) keeps the checksums of the HDUs that have them up to date and adds none.
file.Save("auto.fits");

// Never removes them: the right choice for a file that other programs edit afterwards.
file.Save("never.fits", ChecksumPolicy.Never);
using var stripped = FitsFile.Open("never.fits");
Console.WriteLine(stripped.Primary.Header.Contains("CHECKSUM"));    // False
```
<!-- /example -->

## Corruption is never hidden

With `Auto`, an HDU that is neither modified nor re-created keeps the sums it was read with, **even when they are wrong**. Recomputing them would turn a damaged file into one that looks healthy. The same holds for the byte-for-byte copy of a file read and saved without a change.

<!-- example: Checksums.Detect -->
```csharp
byte[] bytes = File.ReadAllBytes("m42-sums.fits");
bytes[bytes.Length / 2] ^= 0xFF;                      // damage one byte of the data

using var file = FitsFile.Read(new MemoryStream(bytes));
ChecksumResult result = file.Primary.VerifyChecksum();
Console.WriteLine($"{result.Checksum} {result.DataSum}");     // Invalid Invalid

// A file read and saved without changes is written back byte for byte, damaged or not:
// the library never hides a corruption by recomputing the sums.
```
<!-- /example -->

A file whose last HDU lacks the final padding is accepted unless `Strict` is set; the sums of that HDU are then reported as `Invalid`, since the file is not what was summed.
