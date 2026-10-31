# GenericHdu Class

**Namespace:** `FitsNet`

**Inheritance:** `object` → [`Hdu`](FitsNet.Hdu.md) → `GenericHdu`

An extension this version does not interpret (kept verbatim so it survives a round trip).

```csharp
public sealed class GenericHdu : Hdu
```

## Methods

| Member | Description |
| --- | --- |
| [`ReadRawData()`](#fitsnet-generichdu-readrawdata) | A copy of the raw data bytes (unpadded). |

## Method details

<a id="fitsnet-generichdu-readrawdata"></a>
### `ReadRawData()`

A copy of the raw data bytes (unpadded).

```csharp
public byte[] ReadRawData()
```

## Inherited members

- [`Hdu.VerifyChecksum()`](FitsNet.Hdu.md#fitsnet-hdu-verifychecksum) — Checks the CHECKSUM and DATASUM keywords against the HDU as it is now in memory: the current header and data, with the fill bytes found on disk while the data is untouched.
- [`Hdu.Header`](FitsNet.Hdu.md#fitsnet-hdu-header) — The header.
- [`Hdu.DataLength`](FitsNet.Hdu.md#fitsnet-hdu-datalength) — Length in bytes of the data, without block padding.
