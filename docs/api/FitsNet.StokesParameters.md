# StokesParameters Class

**Namespace:** `FitsNet`

**Inheritance:** `object` → `StokesParameters`

The conventional Stokes values of the `STOKES` axis (Table 29 of the standard).

```csharp
public static class StokesParameters
```

## Methods

| Member | Description |
| --- | --- |
| [`Code(string)`](#fitsnet-stokesparameters-code-system-string) | The coordinate value of a symbol such as `Q` or `RR`, or null if unknown. |
| [`Description(string)`](#fitsnet-stokesparameters-description-system-string) | The conventional meaning of a symbol, from Table 29. |
| [`Symbol(double)`](#fitsnet-stokesparameters-symbol-system-double) | The symbol of a coordinate value of a STOKES axis, or null when the value is not a conventional one. |

## Method details

<a id="fitsnet-stokesparameters-code-system-string"></a>
### `Code(string)`

The coordinate value of a symbol such as `Q` or `RR`, or null if unknown.

```csharp
public static int? Code(string symbol)
```

**Parameters**

- `symbol` — The symbol, for example `I`, `Q` or `RR`.

<a id="fitsnet-stokesparameters-description-system-string"></a>
### `Description(string)`

The conventional meaning of a symbol, from Table 29.

```csharp
public static string Description(string symbol)
```

**Parameters**

- `symbol` — The symbol, for example `I`, `Q` or `RR`.

<a id="fitsnet-stokesparameters-symbol-system-double"></a>
### `Symbol(double)`

The symbol of a coordinate value of a STOKES axis, or null when the value is not a conventional one.

```csharp
public static string? Symbol(double coordinate)
```

**Parameters**

- `coordinate` — A coordinate value of a `STOKES` axis.
