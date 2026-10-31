# FitsException Class

**Namespace:** `FitsNet`

**Inheritance:** `object` → `Exception` → `FitsException`

Thrown when data does not conform to the FITS standard.

```csharp
public class FitsException : Exception
```

## Constructors

| Member | Description |
| --- | --- |
| [`FitsException(string)`](#fitsnet-fitsexception-ctor-system-string) | Creates the exception with a message. |
| [`FitsException(string, Exception)`](#fitsnet-fitsexception-ctor-system-string-system-exception) | Creates the exception with a message and the exception that caused it. |

## Constructor details

<a id="fitsnet-fitsexception-ctor-system-string"></a>
### `FitsException(string)`

Creates the exception with a message.

```csharp
public FitsException(string message)
```

**Parameters**

- `message` — The message that describes the error.

<a id="fitsnet-fitsexception-ctor-system-string-system-exception"></a>
### `FitsException(string, Exception)`

Creates the exception with a message and the exception that caused it.

```csharp
public FitsException(string message, Exception innerException)
```

**Parameters**

- `message` — The message that describes the error.
- `innerException` — The exception that caused this one.
