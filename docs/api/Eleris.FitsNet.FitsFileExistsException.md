# FitsFileExistsException Class

**Namespace:** `Eleris.FitsNet`

**Inheritance:** `object` → `Exception` → `SystemException` → `IOException` → `FitsFileExistsException`

Thrown by a save in [`FitsSaveMode.CreateNew`](Eleris.FitsNet.FitsSaveMode.md#eleris-fitsnet-fitssavemode-createnew) mode when something already exists at the target path. Nothing was replaced and no temporary file is left behind. It is an [`IOException`](https://learn.microsoft.com/dotnet/api/system.io.ioexception); catch it by its type.

```csharp
public sealed class FitsFileExistsException : IOException
```

## Constructors

| Member | Description |
| --- | --- |
| [`FitsFileExistsException(string)`](#eleris-fitsnet-fitsfileexistsexception-ctor-system-string) | Creates the exception for a path. |
| [`FitsFileExistsException(string, Exception)`](#eleris-fitsnet-fitsfileexistsexception-ctor-system-string-system-exception) | Creates the exception for a path, with the exception that reported the problem. |

## Properties

| Member | Description |
| --- | --- |
| [`FileName`](#eleris-fitsnet-fitsfileexistsexception-filename) | The full path of the file that exists. |

## Constructor details

<a id="eleris-fitsnet-fitsfileexistsexception-ctor-system-string"></a>
### `FitsFileExistsException(string)`

Creates the exception for a path.

```csharp
public FitsFileExistsException(string fileName)
```

**Parameters**

- `fileName` — The full path of the file that exists.

<a id="eleris-fitsnet-fitsfileexistsexception-ctor-system-string-system-exception"></a>
### `FitsFileExistsException(string, Exception)`

Creates the exception for a path, with the exception that reported the problem.

```csharp
public FitsFileExistsException(string fileName, Exception? innerException)
```

**Parameters**

- `fileName` — The full path of the file that exists.
- `innerException` — The exception that reported the problem, or null.

## Property details

<a id="eleris-fitsnet-fitsfileexistsexception-filename"></a>
### `FileName`

The full path of the file that exists.

```csharp
public string FileName { get; }
```
