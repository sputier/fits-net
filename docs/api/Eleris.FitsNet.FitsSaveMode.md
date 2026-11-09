# FitsSaveMode Enum

**Namespace:** `Eleris.FitsNet`

What [`FitsFile.Save`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-save-system-string-eleris-fitsnet-checksumpolicy-eleris-fitsnet-fitssavemode-system-threading-cancellationtoken) does when a file already exists at the path.

```csharp
public enum FitsSaveMode : int
```

## Members

| Name | Value | Description |
| --- | ---: | --- |
| <a id="eleris-fitsnet-fitssavemode-overwrite"></a>`Overwrite` | 0 | Replace an existing file, which keeps its attributes and permissions; create the file otherwise. |
| <a id="eleris-fitsnet-fitssavemode-createnew"></a>`CreateNew` | 1 | Never replace anything: fail with [`FitsFileExistsException`](Eleris.FitsNet.FitsFileExistsException.md) if something exists at the path, including when it appears while the file is being written. |
