# ChecksumStatus Enum

**Namespace:** `FitsNet`

Outcome of checking one of the CHECKSUM / DATASUM keywords.

```csharp
public enum ChecksumStatus : int
```

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="fitsnet-checksumstatus-missing"></a>`Missing` | 0 | The keyword is not present. |
| <a id="fitsnet-checksumstatus-valid"></a>`Valid` | 1 | The stored value matches the data. |
| <a id="fitsnet-checksumstatus-invalid"></a>`Invalid` | 2 | The stored value does not match (or cannot be parsed). |
