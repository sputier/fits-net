# ChecksumPolicy Enum

**Namespace:** `Eleris.FitsNet`

What [`FitsFile.Save`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-save-system-io-stream-eleris-fitsnet-checksumpolicy) does with CHECKSUM and DATASUM.

```csharp
public enum ChecksumPolicy : int
```

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="eleris-fitsnet-checksumpolicy-auto"></a>`Auto` | 0 | Recompute them in HDUs that already carry one of the two keywords; leave other HDUs alone. |
| <a id="eleris-fitsnet-checksumpolicy-always"></a>`Always` | 1 | Write fresh CHECKSUM and DATASUM in every HDU. |
| <a id="eleris-fitsnet-checksumpolicy-never"></a>`Never` | 2 | Remove both keywords from every HDU. |
