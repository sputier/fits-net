# FitsMimeTypes Class

**Namespace:** `Eleris.FitsNet`

**Inheritance:** `object` → `FitsMimeTypes`

The MIME types of Appendix G of the standard (RFC 4047).

```csharp
public static class FitsMimeTypes
```

## Fields

| Member | Description |
| --- | --- |
| [`Application`](#eleris-fitsnet-fitsmimetypes-application) | The general type, for any FITS file. |
| [`FileExtensions`](#eleris-fitsnet-fitsmimetypes-fileextensions) | The usual file name extensions. |
| [`Image`](#eleris-fitsnet-fitsmimetypes-image) | For a FITS file whose purpose is a single image in the primary HDU. |

## Field details

<a id="eleris-fitsnet-fitsmimetypes-application"></a>
### `Application`

The general type, for any FITS file.

```csharp
public const string Application = "application/fits"
```

<a id="eleris-fitsnet-fitsmimetypes-fileextensions"></a>
### `FileExtensions`

The usual file name extensions.

```csharp
public static readonly IReadOnlyList<string> FileExtensions
```

<a id="eleris-fitsnet-fitsmimetypes-image"></a>
### `Image`

For a FITS file whose purpose is a single image in the primary HDU.

```csharp
public const string Image = "image/fits"
```
