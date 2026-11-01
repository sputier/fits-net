# FitsCardKind Enum

**Namespace:** `Eleris.FitsNet`

Kind of a header card.

```csharp
public enum FitsCardKind : int
```

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="eleris-fitsnet-fitscardkind-value"></a>`Value` | 0 | `KEYWORD = value / comment`. |
| <a id="eleris-fitsnet-fitscardkind-commentary"></a>`Commentary` | 1 | COMMENT, HISTORY, blank keyword, or any card without a value indicator. |
| <a id="eleris-fitsnet-fitscardkind-end"></a>`End` | 2 | The END card. |
