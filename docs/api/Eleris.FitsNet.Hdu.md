# Hdu Class

**Namespace:** `Eleris.FitsNet`

**Inheritance:** `object` → `Hdu`

A Header Data Unit: a header plus its data.

```csharp
public abstract class Hdu
```

## Properties

| Member | Description |
| --- | --- |
| [`DataLength`](#eleris-fitsnet-hdu-datalength) | Length in bytes of the data, without block padding. |
| [`Header`](#eleris-fitsnet-hdu-header) | The header. |

## Methods

| Member | Description |
| --- | --- |
| [`VerifyChecksum()`](#eleris-fitsnet-hdu-verifychecksum) | Checks the CHECKSUM and DATASUM keywords against the HDU as it is now in memory: the current header and data, with the fill bytes found on disk while the data is untouched. |

## Property details

<a id="eleris-fitsnet-hdu-datalength"></a>
### `DataLength`

Length in bytes of the data, without block padding.

```csharp
public long DataLength { get; }
```

<a id="eleris-fitsnet-hdu-header"></a>
### `Header`

The header. Mandatory structural keywords are rewritten on save; edit data through the typed members.

```csharp
public FitsHeader Header { get; }
```

## Method details

<a id="eleris-fitsnet-hdu-verifychecksum"></a>
### `VerifyChecksum()`

Checks the CHECKSUM and DATASUM keywords against the HDU as it is now in memory: the current header and data, with the fill bytes found on disk while the data is untouched. For an HDU that has not been modified since it was read, this is exactly a verification of the file on disk; after any edit the stored sums no longer match, so they are reported [`ChecksumStatus.Invalid`](Eleris.FitsNet.ChecksumStatus.md#eleris-fitsnet-checksumstatus-invalid). Only the canonical `0xFFFFFFFF` total (minus zero) is accepted.

```csharp
public ChecksumResult VerifyChecksum()
```
