# FitsCard Class

**Namespace:** `Eleris.FitsNet`

**Inheritance:** `object` → `FitsCard`

One 80-character header card. [`FitsCard.Value`](#eleris-fitsnet-fitscard-value) is one of [`Boolean`](https://learn.microsoft.com/dotnet/api/system.boolean), [`Int64`](https://learn.microsoft.com/dotnet/api/system.int64), [`BigInteger`](https://learn.microsoft.com/dotnet/api/system.numerics.biginteger), [`Double`](https://learn.microsoft.com/dotnet/api/system.double), [`Complex`](https://learn.microsoft.com/dotnet/api/system.numerics.complex), [`String`](https://learn.microsoft.com/dotnet/api/system.string) or `null` (undefined).

```csharp
public sealed class FitsCard
```

## Constructors

| Member | Description |
| --- | --- |
| [`FitsCard(string, object, string)`](#eleris-fitsnet-fitscard-ctor-system-string-system-object-system-string) | Creates a value card, validating keyword and value. |

## Fields

| Member | Description |
| --- | --- |
| [`CardLength`](#eleris-fitsnet-fitscard-cardlength) | The length of a card in a file: 80 characters. |

## Properties

| Member | Description |
| --- | --- |
| [`Comment`](#eleris-fitsnet-fitscard-comment) | The comment that follows the value, without the leading slash; null when there is none. |
| [`End`](#eleris-fitsnet-fitscard-end) | The `END` card. |
| [`IsHierarch`](#eleris-fitsnet-fitscard-ishierarch) | True when the keyword cannot be written as a standard 8-character keyword and uses the HIERARCH convention. |
| [`Keyword`](#eleris-fitsnet-fitscard-keyword) | The keyword (upper case); empty for a blank commentary card, `COMMENT` or `HISTORY` for those. |
| [`Kind`](#eleris-fitsnet-fitscard-kind) | What sort of card this is. |
| [`Value`](#eleris-fitsnet-fitscard-value) | The value: a [`Boolean`](https://learn.microsoft.com/dotnet/api/system.boolean), [`Int64`](https://learn.microsoft.com/dotnet/api/system.int64), [`BigInteger`](https://learn.microsoft.com/dotnet/api/system.numerics.biginteger), [`Double`](https://learn.microsoft.com/dotnet/api/system.double), [`Complex`](https://learn.microsoft.com/dotnet/api/system.numerics.complex) or [`String`](https://learn.microsoft.com/dotnet/api/system.string); null when the value is undefined. |

## Methods

| Member | Description |
| --- | --- |
| [`CommentCard(string)`](#eleris-fitsnet-fitscard-commentcard-system-string) | Creates a `COMMENT` card. |
| [`Commentary(string, string)`](#eleris-fitsnet-fitscard-commentary-system-string-system-string) | Creates a commentary card (COMMENT, HISTORY or blank keyword). |
| [`Hierarch(string, object, string)`](#eleris-fitsnet-fitscard-hierarch-system-string-system-object-system-string) | Creates a card with a long or unusual keyword using the HIERARCH convention (which is not part of the FITS standard): printable ASCII, single-blank separated words, up to what fits in 80 characters. |
| [`HistoryCard(string)`](#eleris-fitsnet-fitscard-historycard-system-string) | Creates a `HISTORY` card. |
| [`ToString()`](#eleris-fitsnet-fitscard-tostring) | A readable form of the card, such as `NAXIS1 = 640 / length of the first axis`; it is not the 80-character image of the file, which [`FitsHeader.ToBytes`](Eleris.FitsNet.FitsHeader.md#eleris-fitsnet-fitsheader-tobytes) gives for a whole header. |

## Constructor details

<a id="eleris-fitsnet-fitscard-ctor-system-string-system-object-system-string"></a>
### `FitsCard(string, object, string)`

Creates a value card, validating keyword and value.

```csharp
public FitsCard(string keyword, object? value, string? comment = null)
```

**Parameters**

- `keyword` — The keyword: up to 8 upper case letters, digits, hyphens or underscores.
- `value` — The value: a `bool`, an integer, a `double`, a [`Complex`](https://learn.microsoft.com/dotnet/api/system.numerics.complex) or a `string`; null for an undefined value.
- `comment` — The comment, or null for none.

## Field details

<a id="eleris-fitsnet-fitscard-cardlength"></a>
### `CardLength`

The length of a card in a file: 80 characters.

```csharp
public const int CardLength = 80
```

## Property details

<a id="eleris-fitsnet-fitscard-comment"></a>
### `Comment`

The comment that follows the value, without the leading slash; null when there is none.

```csharp
public string? Comment { get; }
```

<a id="eleris-fitsnet-fitscard-end"></a>
### `End`

The `END` card. Headers do not store it: it is added when a header is written.

```csharp
public static FitsCard End { get; }
```

<a id="eleris-fitsnet-fitscard-ishierarch"></a>
### `IsHierarch`

True when the keyword cannot be written as a standard 8-character keyword and uses the HIERARCH convention.

```csharp
public bool IsHierarch { get; }
```

<a id="eleris-fitsnet-fitscard-keyword"></a>
### `Keyword`

The keyword (upper case); empty for a blank commentary card, `COMMENT` or `HISTORY` for those.

```csharp
public string Keyword { get; }
```

<a id="eleris-fitsnet-fitscard-kind"></a>
### `Kind`

What sort of card this is.

```csharp
public FitsCardKind Kind { get; }
```

<a id="eleris-fitsnet-fitscard-value"></a>
### `Value`

The value: a [`Boolean`](https://learn.microsoft.com/dotnet/api/system.boolean), [`Int64`](https://learn.microsoft.com/dotnet/api/system.int64), [`BigInteger`](https://learn.microsoft.com/dotnet/api/system.numerics.biginteger), [`Double`](https://learn.microsoft.com/dotnet/api/system.double), [`Complex`](https://learn.microsoft.com/dotnet/api/system.numerics.complex) or [`String`](https://learn.microsoft.com/dotnet/api/system.string); null when the value is undefined. For a commentary card, the text.

```csharp
public object? Value { get; }
```

## Method details

<a id="eleris-fitsnet-fitscard-commentcard-system-string"></a>
### `CommentCard(string)`

Creates a `COMMENT` card.

```csharp
public static FitsCard CommentCard(string text)
```

**Parameters**

- `text` — The text of the card.

<a id="eleris-fitsnet-fitscard-commentary-system-string-system-string"></a>
### `Commentary(string, string)`

Creates a commentary card (COMMENT, HISTORY or blank keyword).

```csharp
public static FitsCard Commentary(string keyword, string text)
```

**Parameters**

- `keyword` — `COMMENT`, `HISTORY` or an empty string for a blank keyword.
- `text` — The text of the card.

<a id="eleris-fitsnet-fitscard-hierarch-system-string-system-object-system-string"></a>
### `Hierarch(string, object, string)`

Creates a card with a long or unusual keyword using the HIERARCH convention (which is not part of the FITS standard): printable ASCII, single-blank separated words, up to what fits in 80 characters.

```csharp
public static FitsCard Hierarch(string keyword, object? value, string? comment = null)
```

**Parameters**

- `keyword` — The keyword, which may be longer than 8 characters and contain several words.
- `value` — The value (see the constructor).
- `comment` — The comment, or null for none.

<a id="eleris-fitsnet-fitscard-historycard-system-string"></a>
### `HistoryCard(string)`

Creates a `HISTORY` card.

```csharp
public static FitsCard HistoryCard(string text)
```

**Parameters**

- `text` — The text of the card.

<a id="eleris-fitsnet-fitscard-tostring"></a>
### `ToString()`

A readable form of the card, such as `NAXIS1 = 640 / length of the first axis`; it is not the 80-character image of the file, which [`FitsHeader.ToBytes`](Eleris.FitsNet.FitsHeader.md#eleris-fitsnet-fitsheader-tobytes) gives for a whole header.

```csharp
public override string ToString()
```
