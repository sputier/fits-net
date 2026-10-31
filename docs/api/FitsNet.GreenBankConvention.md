# GreenBankConvention Class

**Namespace:** `FitsNet`

**Inheritance:** `object` → `GreenBankConvention`

The Green Bank convention (Appendix L): header keywords expanded into binary-table columns, and constant columns collapsed into keywords.

```csharp
public static class GreenBankConvention
```

## Methods

| Member | Description |
| --- | --- |
| [`CollapseConstantColumns(BinaryTableHdu)`](#fitsnet-greenbankconvention-collapseconstantcolumns-fitsnet-binarytablehdu) | Collapses into header keywords the columns that hold the same value in every row, when their name is a valid keyword name, and the value is a number, a string or a logical value (unscaled, without TNULL). |
| [`ExpandKeyword(BinaryTableHdu, string)`](#fitsnet-greenbankconvention-expandkeyword-fitsnet-binarytablehdu-system-string) | Replaces the keyword by a column of the same name that holds its value in every row (so that rows can then differ). |
| [`GetValue(BinaryTableHdu, string, long)`](#fitsnet-greenbankconvention-getvalue-fitsnet-binarytablehdu-system-string-system-int64) | The value of a "keyword" in a row of a table: the cell of the column of that name if there is one (it takes precedence), else the value of the header keyword. |
| [`GetValues(BinaryTableHdu, string)`](#fitsnet-greenbankconvention-getvalues-fitsnet-binarytablehdu-system-string) | The values of a "keyword" for every row (see [`GreenBankConvention.GetValue`](#fitsnet-greenbankconvention-getvalue-fitsnet-binarytablehdu-system-string-system-int64)); reads the column once, unlike calling [`GreenBankConvention.GetValue`](#fitsnet-greenbankconvention-getvalue-fitsnet-binarytablehdu-system-string-system-int64) per row. |
| [`IsExpandable(string)`](#fitsnet-greenbankconvention-isexpandable-system-string) | Whether a keyword may be expanded into a column (the keywords that describe the structure of the table may not). |

## Method details

<a id="fitsnet-greenbankconvention-collapseconstantcolumns-fitsnet-binarytablehdu"></a>
### `CollapseConstantColumns(BinaryTableHdu)`

Collapses into header keywords the columns that hold the same value in every row, when their name is a valid keyword name, and the value is a number, a string or a logical value (unscaled, without TNULL). The last column of a table is kept.

```csharp
public static IReadOnlyList<string> CollapseConstantColumns(this BinaryTableHdu table)
```

**Parameters**

- `table` — The table.

**Returns**

The names of the columns that became keywords.

<a id="fitsnet-greenbankconvention-expandkeyword-fitsnet-binarytablehdu-system-string"></a>
### `ExpandKeyword(BinaryTableHdu, string)`

Replaces the keyword by a column of the same name that holds its value in every row (so that rows can then differ).

```csharp
public static void ExpandKeyword(this BinaryTableHdu table, string keyword)
```

**Parameters**

- `table` — The table.
- `keyword` — The keyword to turn into a column.

**Exceptions**

- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — The keyword is a structural one, is not in the header, has no simple value, or a column of that name exists.

<a id="fitsnet-greenbankconvention-getvalue-fitsnet-binarytablehdu-system-string-system-int64"></a>
### `GetValue(BinaryTableHdu, string, long)`

The value of a "keyword" in a row of a table: the cell of the column of that name if there is one (it takes precedence), else the value of the header keyword. Cells are returned unscaled; a cell holding several values is returned as an array. Each call reads the column: to visit every row use [`GreenBankConvention.GetValues`](#fitsnet-greenbankconvention-getvalues-fitsnet-binarytablehdu-system-string).

```csharp
public static object? GetValue(this BinaryTableHdu table, string name, long row)
```

**Parameters**

- `table` — The table.
- `name` — The name of a column or of a keyword.
- `row` — The zero-based index of the row.

**Returns**

The value, or null when there is neither a column nor a keyword of that name.

<a id="fitsnet-greenbankconvention-getvalues-fitsnet-binarytablehdu-system-string"></a>
### `GetValues(BinaryTableHdu, string)`

The values of a "keyword" for every row (see [`GreenBankConvention.GetValue`](#fitsnet-greenbankconvention-getvalue-fitsnet-binarytablehdu-system-string-system-int64)); reads the column once, unlike calling [`GreenBankConvention.GetValue`](#fitsnet-greenbankconvention-getvalue-fitsnet-binarytablehdu-system-string-system-int64) per row.

```csharp
public static object?[]? GetValues(this BinaryTableHdu table, string name)
```

**Parameters**

- `table` — The table.
- `name` — The name of a column or of a keyword.

**Returns**

One entry per row, or null when there is neither a column nor a keyword of that name.

<a id="fitsnet-greenbankconvention-isexpandable-system-string"></a>
### `IsExpandable(string)`

Whether a keyword may be expanded into a column (the keywords that describe the structure of the table may not).

```csharp
public static bool IsExpandable(string keyword)
```

**Parameters**

- `keyword` — The keyword.
