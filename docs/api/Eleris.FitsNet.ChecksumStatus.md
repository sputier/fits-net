# ChecksumStatus Enum

**Namespace:** `Eleris.FitsNet`

Outcome of checking one of the CHECKSUM / DATASUM keywords.

```csharp
public enum ChecksumStatus : int
```

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="eleris-fitsnet-checksumstatus-missing"></a>`Missing` | 0 | The keyword is not present. |
| <a id="eleris-fitsnet-checksumstatus-valid"></a>`Valid` | 1 | The stored value matches the data. |
| <a id="eleris-fitsnet-checksumstatus-invalid"></a>`Invalid` | 2 | The stored value does not match (or cannot be parsed). |
