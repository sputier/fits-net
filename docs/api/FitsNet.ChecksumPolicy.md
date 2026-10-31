# ChecksumPolicy Enum

**Namespace:** `FitsNet`

What [`FitsFile.Save`](FitsNet.FitsFile.md#fitsnet-fitsfile-save-system-io-stream-fitsnet-checksumpolicy) does with CHECKSUM and DATASUM.

```csharp
public enum ChecksumPolicy : int
```

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="fitsnet-checksumpolicy-auto"></a>`Auto` | 0 | Recompute them in HDUs that already carry one of the two keywords; leave other HDUs alone. |
| <a id="fitsnet-checksumpolicy-always"></a>`Always` | 1 | Write fresh CHECKSUM and DATASUM in every HDU. |
| <a id="fitsnet-checksumpolicy-never"></a>`Never` | 2 | Remove both keywords from every HDU. |
