# CompressionAlgorithm Enum

**Namespace:** `FitsNet.Compression`

The compression algorithms of the tile compression convention (`ZCMPTYPE`).

```csharp
public enum CompressionAlgorithm : int
```

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="fitsnet-compression-compressionalgorithm-rice"></a>`Rice` | 0 | `RICE_1`: lossless, fast, best for integer data (8, 16 or 32 bits). |
| <a id="fitsnet-compression-compressionalgorithm-gzip1"></a>`Gzip1` | 1 | `GZIP_1`: gzip of the big-endian pixels (any BITPIX). |
| <a id="fitsnet-compression-compressionalgorithm-gzip2"></a>`Gzip2` | 2 | `GZIP_2`: gzip after grouping the bytes of equal significance (usually smaller than GZIP\_1). |
| <a id="fitsnet-compression-compressionalgorithm-plio"></a>`Plio` | 3 | `PLIO_1`: IRAF pixel list, for masks (integer values 0 to 2^24). |
| <a id="fitsnet-compression-compressionalgorithm-hcompress"></a>`HCompress` | 4 | `HCOMPRESS_1`: H-transform; lossy when the scale is not 0. |
| <a id="fitsnet-compression-compressionalgorithm-nocompress"></a>`NoCompress` | 5 | `NOCOMPRESS`: tiles are stored as they are. |
