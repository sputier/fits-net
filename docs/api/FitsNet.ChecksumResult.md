# ChecksumResult Struct

**Namespace:** `FitsNet`

**Inheritance:** `object` → `ValueType` → `ChecksumResult`

Result of [`Hdu.VerifyChecksum`](FitsNet.Hdu.md#fitsnet-hdu-verifychecksum).

```csharp
public readonly struct ChecksumResult : IEquatable<ChecksumResult>
```

## Constructors

| Member | Description |
| --- | --- |
| [`ChecksumResult(ChecksumStatus, ChecksumStatus)`](#fitsnet-checksumresult-ctor-fitsnet-checksumstatus-fitsnet-checksumstatus) | Result of [`Hdu.VerifyChecksum`](FitsNet.Hdu.md#fitsnet-hdu-verifychecksum). |

## Properties

| Member | Description |
| --- | --- |
| [`Checksum`](#fitsnet-checksumresult-checksum) | The CHECKSUM keyword, which covers header and data. |
| [`DataSum`](#fitsnet-checksumresult-datasum) | The DATASUM keyword, which covers the data unit only. |

## Constructor details

<a id="fitsnet-checksumresult-ctor-fitsnet-checksumstatus-fitsnet-checksumstatus"></a>
### `ChecksumResult(ChecksumStatus, ChecksumStatus)`

Result of [`Hdu.VerifyChecksum`](FitsNet.Hdu.md#fitsnet-hdu-verifychecksum).

```csharp
public ChecksumResult(ChecksumStatus Checksum, ChecksumStatus DataSum)
```

**Parameters**

- `Checksum` — The CHECKSUM keyword, which covers header and data.
- `DataSum` — The DATASUM keyword, which covers the data unit only.

## Property details

<a id="fitsnet-checksumresult-checksum"></a>
### `Checksum`

The CHECKSUM keyword, which covers header and data.

```csharp
public ChecksumStatus Checksum { get; init; }
```

<a id="fitsnet-checksumresult-datasum"></a>
### `DataSum`

The DATASUM keyword, which covers the data unit only.

```csharp
public ChecksumStatus DataSum { get; init; }
```
