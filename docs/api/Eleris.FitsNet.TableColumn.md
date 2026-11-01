# TableColumn Class

**Namespace:** `Eleris.FitsNet`

**Inheritance:** `object` → `TableColumn`

A table column: its header metadata (TTYPE, TFORM, TUNIT, TSCAL, TZERO, TNULL, TDISP, TDIM) and, optionally, its data. Data is stored in the native representation of the column type: `B`=byte\[\], `I`=short\[\], `J`=int\[\], `K`=long\[\], `E`=float\[\], `D`=double\[\], `C`/`M`=Complex\[\], `L`=bool?\[\], `X`=bool\[\] (one entry per bit), `A`=string\[\]; variable-length columns (`P`/`Q`) hold one array per row (string\[\] for character data). Fixed-length numeric data is flat: row-major, `repeat` values per row.

```csharp
public sealed class TableColumn
```

## Constructors

| Member | Description |
| --- | --- |
| [`TableColumn(string, string)`](#eleris-fitsnet-tablecolumn-ctor-system-string-system-string) | Creates a column description without data. |

## Properties

| Member | Description |
| --- | --- |
| [`Data`](#eleris-fitsnet-tablecolumn-data) | The column data, or null when only the metadata was read. |
| [`Dimensions`](#eleris-fitsnet-tablecolumn-dimensions) | TDIMn (e.g. 4, 3 for a 4x3 array per cell). |
| [`Display`](#eleris-fitsnet-tablecolumn-display) | The display format (`TDISPn`), or null. |
| [`Extras`](#eleris-fitsnet-tablecolumn-extras) | Other per-column keywords of the registered conventions (TCTYPn, TCUNIn, TCRPXn, TCRVLn, TCDLTn, TCROTn, TRPOSn, TDMINn, TDMAXn, TLMINn, TLMAXn, TUCDn, TUTYPn), keyed by the keyword without its column number (e.g. |
| [`Format`](#eleris-fitsnet-tablecolumn-format) | TFORMn, e.g. |
| [`Name`](#eleris-fitsnet-tablecolumn-name) | TTYPEn. |
| [`Null`](#eleris-fitsnet-tablecolumn-null) | TNULLn: an integer (stored as [`Int64`](https://learn.microsoft.com/dotnet/api/system.int64)) for binary integer columns, a [`String`](https://learn.microsoft.com/dotnet/api/system.string) for ASCII tables. |
| [`Scale`](#eleris-fitsnet-tablecolumn-scale) | TSCALn (null = 1). |
| [`Unit`](#eleris-fitsnet-tablecolumn-unit) | The physical unit (`TUNITn`), or null. |
| [`Zero`](#eleris-fitsnet-tablecolumn-zero) | TZEROn (null = 0). |

## Methods

| Member | Description |
| --- | --- |
| [`Create<T>(string, T[], int, string, long[])`](#eleris-fitsnet-tablecolumn-create-1-system-string-0-system-int32-system-string-system-int64) | Numeric column. |
| [`CreateAsciiFloat(string, double[], string)`](#eleris-fitsnet-tablecolumn-createasciifloat-system-string-system-double-system-string) | ASCII table floating point column; `format` is `Fw.d`, `Ew.d` or `Dw.d`. |
| [`CreateAsciiInteger(string, long?[], int?)`](#eleris-fitsnet-tablecolumn-createasciiinteger-system-string-system-nullable-system-int64-system-nullable-system-int32) | ASCII table integer column (`Iw`); width defaults to the widest value. |
| [`CreateAsciiString(string, string[], int?)`](#eleris-fitsnet-tablecolumn-createasciistring-system-string-system-string-system-nullable-system-int32) | ASCII table character column (`Aw`); width defaults to the longest value. |
| [`CreateBits(string, bool[], int)`](#eleris-fitsnet-tablecolumn-createbits-system-string-system-boolean-system-int32) | Bit column (`X`): `bitsPerRow` bits per row, one bool per bit. |
| [`CreateComplex(string, Complex[], bool, int, string)`](#eleris-fitsnet-tablecolumn-createcomplex-system-string-system-numerics-complex-system-boolean-system-int32-system-string) | Complex column (`C` single or `M` double precision storage). |
| [`CreateLogical(string, bool?[], int)`](#eleris-fitsnet-tablecolumn-createlogical-system-string-system-nullable-system-boolean-system-int32) | Logical column (`L`): true, false or undefined (null). |
| [`CreateString(string, string[], int, int)`](#eleris-fitsnet-tablecolumn-createstring-system-string-system-string-system-int32-system-int32) | Fixed-width character column: `stringsPerRow` strings of `width` characters per row. |
| [`CreateVariable<T>(string, T[][], bool, string)`](#eleris-fitsnet-tablecolumn-createvariable-1-system-string-0-system-boolean-system-string) | Variable-length numeric column (`P`, or `Q` with 64-bit descriptors): one array per row. |
| [`CreateVariableString(string, string[], bool)`](#eleris-fitsnet-tablecolumn-createvariablestring-system-string-system-string-system-boolean) | Variable-length character column: one string per row. |

## Constructor details

<a id="eleris-fitsnet-tablecolumn-ctor-system-string-system-string"></a>
### `TableColumn(string, string)`

Creates a column description without data.

```csharp
public TableColumn(string? name, string format)
```

**Parameters**

- `name` — The column name (`TTYPEn`).
- `format` — The format (`TFORMn`), such as `1J` or `8A` for a binary table, `F8.3` for an ASCII table.

## Property details

<a id="eleris-fitsnet-tablecolumn-data"></a>
### `Data`

The column data, or null when only the metadata was read.

```csharp
public Array? Data { get; set; }
```

<a id="eleris-fitsnet-tablecolumn-dimensions"></a>
### `Dimensions`

TDIMn (e.g. 4, 3 for a 4x3 array per cell).

```csharp
public long[]? Dimensions { get; set; }
```

<a id="eleris-fitsnet-tablecolumn-display"></a>
### `Display`

The display format (`TDISPn`), or null.

```csharp
public string? Display { get; set; }
```

<a id="eleris-fitsnet-tablecolumn-extras"></a>
### `Extras`

Other per-column keywords of the registered conventions (TCTYPn, TCUNIn, TCRPXn, TCRVLn, TCDLTn, TCROTn, TRPOSn, TDMINn, TDMAXn, TLMINn, TLMAXn, TUCDn, TUTYPn), keyed by the keyword without its column number (e.g. `"TDMIN"`). They follow the column when columns are added or removed.

```csharp
public Dictionary<string, object?> Extras { get; }
```

<a id="eleris-fitsnet-tablecolumn-format"></a>
### `Format`

TFORMn, e.g. `1J`, `10A`, `PD(100)` (binary) or `F8.3` (ASCII).

```csharp
public string Format { get; set; }
```

<a id="eleris-fitsnet-tablecolumn-name"></a>
### `Name`

TTYPEn.

```csharp
public string? Name { get; set; }
```

<a id="eleris-fitsnet-tablecolumn-null"></a>
### `Null`

TNULLn: an integer (stored as [`Int64`](https://learn.microsoft.com/dotnet/api/system.int64)) for binary integer columns, a [`String`](https://learn.microsoft.com/dotnet/api/system.string) for ASCII tables.

```csharp
public object? Null { get; set; }
```

<a id="eleris-fitsnet-tablecolumn-scale"></a>
### `Scale`

TSCALn (null = 1).

```csharp
public double? Scale { get; set; }
```

<a id="eleris-fitsnet-tablecolumn-unit"></a>
### `Unit`

The physical unit (`TUNITn`), or null.

```csharp
public string? Unit { get; set; }
```

<a id="eleris-fitsnet-tablecolumn-zero"></a>
### `Zero`

TZEROn (null = 0).

```csharp
public double? Zero { get; set; }
```

## Method details

<a id="eleris-fitsnet-tablecolumn-create-1-system-string-0-system-int32-system-string-system-int64"></a>
### `Create<T>(string, T[], int, string, long[])`

Numeric column. Supported: byte, sbyte, short, ushort, int, uint, long, ulong, float, double. Unsigned types and sbyte use the standard TZERO conventions. `repeat` values per row.

```csharp
public static TableColumn Create<T>(string name, T[] values, int repeat = 1, string? unit = null, long[]? dimensions = null) where T : unmanaged
```

**Type parameters**

- `T` — The numeric type; it chooses the format (`short` gives `I`, `float` gives `E`, and so on). `sbyte`, `ushort`, `uint` and `ulong` use the `TZERO` convention.

**Parameters**

- `name` — The name of the column (`TTYPEn`).
- `values` — The values of all the rows, row after row, `repeat` values for each row.
- `repeat` — The number of values in each row (the repeat count of `TFORMn`).
- `unit` — The physical unit (`TUNITn`), or null.
- `dimensions` — The dimensions of the array in each row (`TDIMn`), or null.

<a id="eleris-fitsnet-tablecolumn-createasciifloat-system-string-system-double-system-string"></a>
### `CreateAsciiFloat(string, double[], string)`

ASCII table floating point column; `format` is `Fw.d`, `Ew.d` or `Dw.d`.

```csharp
public static TableColumn CreateAsciiFloat(string name, double[] values, string format = "E15.7")
```

**Parameters**

- `name` — The name of the column (`TTYPEn`).
- `values` — One value for each row.
- `format` — The Fortran format of the field, for example `E15.7` or `F12.5`.

<a id="eleris-fitsnet-tablecolumn-createasciiinteger-system-string-system-nullable-system-int64-system-nullable-system-int32"></a>
### `CreateAsciiInteger(string, long?[], int?)`

ASCII table integer column (`Iw`); width defaults to the widest value.

```csharp
public static TableColumn CreateAsciiInteger(string name, long?[] values, int? width = null)
```

**Parameters**

- `name` — The name of the column (`TTYPEn`).
- `values` — One value for each row; null for an undefined value.
- `width` — The width of the field, or null for the smallest that holds every value.

<a id="eleris-fitsnet-tablecolumn-createasciistring-system-string-system-string-system-nullable-system-int32"></a>
### `CreateAsciiString(string, string[], int?)`

ASCII table character column (`Aw`); width defaults to the longest value.

```csharp
public static TableColumn CreateAsciiString(string name, string[] values, int? width = null)
```

**Parameters**

- `name` — The name of the column (`TTYPEn`).
- `values` — One string for each row.
- `width` — The width of the field, or null for the smallest that holds every string.

<a id="eleris-fitsnet-tablecolumn-createbits-system-string-system-boolean-system-int32"></a>
### `CreateBits(string, bool[], int)`

Bit column (`X`): `bitsPerRow` bits per row, one bool per bit.

```csharp
public static TableColumn CreateBits(string name, bool[] bits, int bitsPerRow)
```

**Parameters**

- `name` — The name of the column (`TTYPEn`).
- `bits` — All the bits, `bitsPerRow` for each row.
- `bitsPerRow` — The number of bits in each row (the repeat count of the `X` format).

<a id="eleris-fitsnet-tablecolumn-createcomplex-system-string-system-numerics-complex-system-boolean-system-int32-system-string"></a>
### `CreateComplex(string, Complex[], bool, int, string)`

Complex column (`C` single or `M` double precision storage).

```csharp
public static TableColumn CreateComplex(string name, Complex[] values, bool singlePrecision = false, int repeat = 1, string? unit = null)
```

**Parameters**

- `name` — The name of the column (`TTYPEn`).
- `values` — The values of all the rows, `repeat` for each row.
- `singlePrecision` — True for single precision (`C`), false for double precision (`M`).
- `repeat` — The number of values in each row.
- `unit` — The physical unit (`TUNITn`), or null.

<a id="eleris-fitsnet-tablecolumn-createlogical-system-string-system-nullable-system-boolean-system-int32"></a>
### `CreateLogical(string, bool?[], int)`

Logical column (`L`): true, false or undefined (null).

```csharp
public static TableColumn CreateLogical(string name, bool?[] values, int repeat = 1)
```

**Parameters**

- `name` — The name of the column (`TTYPEn`).
- `values` — The values of all the rows, `repeat` for each row; null for an undefined value.
- `repeat` — The number of values in each row.

<a id="eleris-fitsnet-tablecolumn-createstring-system-string-system-string-system-int32-system-int32"></a>
### `CreateString(string, string[], int, int)`

Fixed-width character column: `stringsPerRow` strings of `width` characters per row.

```csharp
public static TableColumn CreateString(string name, string[] values, int width, int stringsPerRow = 1)
```

**Parameters**

- `name` — The name of the column (`TTYPEn`).
- `values` — The strings of all the rows, `stringsPerRow` for each row.
- `width` — The width of each string in characters; longer strings are refused.
- `stringsPerRow` — The number of strings in each row.

<a id="eleris-fitsnet-tablecolumn-createvariable-1-system-string-0-system-boolean-system-string"></a>
### `CreateVariable<T>(string, T[][], bool, string)`

Variable-length numeric column (`P`, or `Q` with 64-bit descriptors): one array per row.

```csharp
public static TableColumn CreateVariable<T>(string name, T[][] rows, bool use64BitDescriptors = false, string? unit = null) where T : unmanaged
```

**Type parameters**

- `T` — The element type of the arrays.

**Parameters**

- `name` — The name of the column (`TTYPEn`).
- `rows` — One array for each row.
- `use64BitDescriptors` — True to use 64-bit descriptors (`Q`) instead of 32-bit ones (`P`).
- `unit` — The physical unit (`TUNITn`), or null.

<a id="eleris-fitsnet-tablecolumn-createvariablestring-system-string-system-string-system-boolean"></a>
### `CreateVariableString(string, string[], bool)`

Variable-length character column: one string per row.

```csharp
public static TableColumn CreateVariableString(string name, string[] rows, bool use64BitDescriptors = false)
```

**Parameters**

- `name` — The name of the column (`TTYPEn`).
- `rows` — One string for each row.
- `use64BitDescriptors` — True to use 64-bit descriptors (`Q`) instead of 32-bit ones (`P`).
