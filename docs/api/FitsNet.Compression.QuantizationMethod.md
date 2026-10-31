# QuantizationMethod Enum

**Namespace:** `FitsNet.Compression`

How floating point pixels are mapped to integers before compression (`ZQUANTIZ`).

```csharp
public enum QuantizationMethod : int
```

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="fitsnet-compression-quantizationmethod-none"></a>`None` | 0 | No quantization: floating point tiles are stored losslessly (gzip or uncompressed). |
| <a id="fitsnet-compression-quantizationmethod-nodither"></a>`NoDither` | 1 | `NO_DITHER`. |
| <a id="fitsnet-compression-quantizationmethod-subtractivedither1"></a>`SubtractiveDither1` | 2 | `SUBTRACTIVE_DITHER_1`. |
| <a id="fitsnet-compression-quantizationmethod-subtractivedither2"></a>`SubtractiveDither2` | 3 | `SUBTRACTIVE_DITHER_2`: like dither 1 but zero pixels are preserved exactly. |
