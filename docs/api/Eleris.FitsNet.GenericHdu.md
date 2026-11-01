# GenericHdu Class

**Namespace:** `Eleris.FitsNet`

**Inheritance:** `object` → [`Hdu`](Eleris.FitsNet.Hdu.md) → `GenericHdu`

An extension this version does not interpret (kept verbatim so it survives a round trip).

```csharp
public sealed class GenericHdu : Hdu
```

## Methods

| Member | Description |
| --- | --- |
| [`ReadRawData()`](#eleris-fitsnet-generichdu-readrawdata) | A copy of the raw data bytes (unpadded). |

## Method details

<a id="eleris-fitsnet-generichdu-readrawdata"></a>
### `ReadRawData()`

A copy of the raw data bytes (unpadded).

```csharp
public byte[] ReadRawData()
```

## Inherited members

- [`Hdu.VerifyChecksum()`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-verifychecksum) — Checks the CHECKSUM and DATASUM keywords against the HDU as it is now in memory: the current header and data, with the fill bytes found on disk while the data is untouched.
- [`Hdu.Header`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-header) — The header.
- [`Hdu.DataLength`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-datalength) — Length in bytes of the data, without block padding.
