# ChecksumResult Struct

**Namespace:** `Eleris.FitsNet`

**Inheritance:** `object` → `ValueType` → `ChecksumResult`

Result of [`Hdu.VerifyChecksum`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-verifychecksum).

```csharp
public readonly struct ChecksumResult : IEquatable<ChecksumResult>
```

## Constructors

| Member | Description |
| --- | --- |
| [`ChecksumResult(ChecksumStatus, ChecksumStatus)`](#eleris-fitsnet-checksumresult-ctor-eleris-fitsnet-checksumstatus-eleris-fitsnet-checksumstatus) | Result of [`Hdu.VerifyChecksum`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-verifychecksum). |

## Properties

| Member | Description |
| --- | --- |
| [`Checksum`](#eleris-fitsnet-checksumresult-checksum) | The CHECKSUM keyword, which covers header and data. |
| [`DataSum`](#eleris-fitsnet-checksumresult-datasum) | The DATASUM keyword, which covers the data unit only. |

## Constructor details

<a id="eleris-fitsnet-checksumresult-ctor-eleris-fitsnet-checksumstatus-eleris-fitsnet-checksumstatus"></a>
### `ChecksumResult(ChecksumStatus, ChecksumStatus)`

Result of [`Hdu.VerifyChecksum`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-verifychecksum).

```csharp
public ChecksumResult(ChecksumStatus Checksum, ChecksumStatus DataSum)
```

**Parameters**

- `Checksum` — The CHECKSUM keyword, which covers header and data.
- `DataSum` — The DATASUM keyword, which covers the data unit only.

## Property details

<a id="eleris-fitsnet-checksumresult-checksum"></a>
### `Checksum`

The CHECKSUM keyword, which covers header and data.

```csharp
public ChecksumStatus Checksum { get; init; }
```

<a id="eleris-fitsnet-checksumresult-datasum"></a>
### `DataSum`

The DATASUM keyword, which covers the data unit only.

```csharp
public ChecksumStatus DataSum { get; init; }
```
