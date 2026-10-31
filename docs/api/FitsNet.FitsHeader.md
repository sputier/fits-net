# FitsHeader Class

**Namespace:** `FitsNet`

**Inheritance:** `object` → `FitsHeader`

An ordered collection of header cards (without the END card, which is implicit).

```csharp
public sealed class FitsHeader : IEnumerable<FitsCard>
```

## Constructors

| Member | Description |
| --- | --- |
| [`FitsHeader()`](#fitsnet-fitsheader-ctor) | Creates an empty header. |

## Fields

| Member | Description |
| --- | --- |
| [`BlockSize`](#fitsnet-fitsheader-blocksize) | The size of a block in bytes: headers and data are padded to a multiple of it. |

## Properties

| Member | Description |
| --- | --- |
| [`Cards`](#fitsnet-fitsheader-cards) | The cards, in order, without the `END` card. |
| [`Count`](#fitsnet-fitsheader-count) | The number of cards. |
| [`SerializedLength`](#fitsnet-fitsheader-serializedlength) | Number of bytes this header occupies on disk (a multiple of 2880, END card included). |
| [`this[int]`](#fitsnet-fitsheader-item-system-int32) | The card at a zero-based index. |

## Methods

| Member | Description |
| --- | --- |
| [`Add(FitsCard)`](#fitsnet-fitsheader-add-fitsnet-fitscard) | Appends a card. |
| [`AddComment(string)`](#fitsnet-fitsheader-addcomment-system-string) | Appends a `COMMENT` card. |
| [`AddHistory(string)`](#fitsnet-fitsheader-addhistory-system-string) | Appends a `HISTORY` card. |
| [`Clear()`](#fitsnet-fitsheader-clear) | Removes every card. |
| [`Contains(string)`](#fitsnet-fitsheader-contains-system-string) | Whether a value card with the keyword exists. |
| [`Find(string)`](#fitsnet-fitsheader-find-system-string) | Returns the first card with the given keyword, or null. |
| [`Get<T>(string)`](#fitsnet-fitsheader-get-1-system-string) | Gets a value or `default` when absent; use [`FitsHeader.TryGetValue`](#fitsnet-fitsheader-trygetvalue-1-system-string-0) to tell absence from a zero/false value. |
| [`GetEnumerator()`](#fitsnet-fitsheader-getenumerator) | Enumerates the cards in order. |
| [`Insert(int, FitsCard)`](#fitsnet-fitsheader-insert-system-int32-fitsnet-fitscard) | Inserts a card at an index. |
| [`Read(Stream, FitsReadOptions)`](#fitsnet-fitsheader-read-system-io-stream-fitsnet-fitsreadoptions) | Reads a header (all 2880-byte blocks up to and including the one holding END) from the stream. |
| [`ReadAsync(Stream, FitsReadOptions, CancellationToken)`](#fitsnet-fitsheader-readasync-system-io-stream-fitsnet-fitsreadoptions-system-threading-cancellationtoken) | Asynchronous version of [`FitsHeader.Read`](#fitsnet-fitsheader-read-system-io-stream-fitsnet-fitsreadoptions). |
| [`Remove(string)`](#fitsnet-fitsheader-remove-system-string) | Removes every value card with the given keyword; returns how many were removed. |
| [`RemoveAt(int)`](#fitsnet-fitsheader-removeat-system-int32) | Removes the card at an index. |
| [`Set(string, object, string)`](#fitsnet-fitsheader-set-system-string-system-object-system-string) | Replaces the value of the first matching card or appends a new card. |
| [`SetComment(string, string)`](#fitsnet-fitsheader-setcomment-system-string-system-string) | Sets or clears (with `null`) the comment of the first card with the given keyword. |
| [`SetHierarch(string, object, string)`](#fitsnet-fitsheader-sethierarch-system-string-system-object-system-string) | Like [`FitsHeader.Set`](#fitsnet-fitsheader-set-system-string-system-object-system-string) for a long or unusual keyword, written with the HIERARCH convention. |
| [`ToBytes()`](#fitsnet-fitsheader-tobytes) | The header as it is written in a file: a whole number of 2880-byte blocks. |
| [`TryGetValue<T>(string, T)`](#fitsnet-fitsheader-trygetvalue-1-system-string-0) | Gets a value converted to `T`; false if absent, undefined or not convertible. |
| [`Write(Stream)`](#fitsnet-fitsheader-write-system-io-stream) | Writes the header to a stream: the cards, the `END` card and the blank padding up to a multiple of 2880 bytes. |
| [`WriteAsync(Stream, CancellationToken)`](#fitsnet-fitsheader-writeasync-system-io-stream-system-threading-cancellationtoken) | Asynchronous version of [`FitsHeader.Write`](#fitsnet-fitsheader-write-system-io-stream). |

## Constructor details

<a id="fitsnet-fitsheader-ctor"></a>
### `FitsHeader()`

Creates an empty header.

```csharp
public FitsHeader()
```

## Field details

<a id="fitsnet-fitsheader-blocksize"></a>
### `BlockSize`

The size of a block in bytes: headers and data are padded to a multiple of it.

```csharp
public const int BlockSize = 2880
```

## Property details

<a id="fitsnet-fitsheader-cards"></a>
### `Cards`

The cards, in order, without the `END` card.

```csharp
public IReadOnlyList<FitsCard> Cards { get; }
```

<a id="fitsnet-fitsheader-count"></a>
### `Count`

The number of cards.

```csharp
public int Count { get; }
```

<a id="fitsnet-fitsheader-serializedlength"></a>
### `SerializedLength`

Number of bytes this header occupies on disk (a multiple of 2880, END card included).

```csharp
public int SerializedLength { get; }
```

<a id="fitsnet-fitsheader-item-system-int32"></a>
### `this[int]`

The card at a zero-based index.

```csharp
public FitsCard this[int index] { get; }
```

**Parameters**

- `index` — The zero-based index of the card.

## Method details

<a id="fitsnet-fitsheader-add-fitsnet-fitscard"></a>
### `Add(FitsCard)`

Appends a card.

```csharp
public FitsHeader Add(FitsCard card)
```

**Parameters**

- `card` — The card to append.

**Returns**

This header, so that calls can be chained.

**Exceptions**

- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — The card is the `END` card, which is implicit.

<a id="fitsnet-fitsheader-addcomment-system-string"></a>
### `AddComment(string)`

Appends a `COMMENT` card. Text longer than a card is split over several cards.

```csharp
public FitsHeader AddComment(string text)
```

**Parameters**

- `text` — The text of the comment.

**Returns**

This header, so that calls can be chained.

<a id="fitsnet-fitsheader-addhistory-system-string"></a>
### `AddHistory(string)`

Appends a `HISTORY` card. Text longer than a card is split over several cards.

```csharp
public FitsHeader AddHistory(string text)
```

**Parameters**

- `text` — The text of the history entry.

**Returns**

This header, so that calls can be chained.

<a id="fitsnet-fitsheader-clear"></a>
### `Clear()`

Removes every card.

```csharp
public void Clear()
```

<a id="fitsnet-fitsheader-contains-system-string"></a>
### `Contains(string)`

Whether a value card with the keyword exists.

```csharp
public bool Contains(string keyword)
```

**Parameters**

- `keyword` — The keyword to look for.

<a id="fitsnet-fitsheader-find-system-string"></a>
### `Find(string)`

Returns the first card with the given keyword, or null.

```csharp
public FitsCard? Find(string keyword)
```

**Parameters**

- `keyword` — The keyword to look for.

<a id="fitsnet-fitsheader-get-1-system-string"></a>
### `Get<T>(string)`

Gets a value or `default` when absent; use [`FitsHeader.TryGetValue`](#fitsnet-fitsheader-trygetvalue-1-system-string-0) to tell absence from a zero/false value.

```csharp
public T? Get<T>(string keyword)
```

**Type parameters**

- `T` — The type to convert the value to, for example `int`, `long`, `double`, `bool` or `string`.

**Parameters**

- `keyword` — The keyword to look for.

<a id="fitsnet-fitsheader-getenumerator"></a>
### `GetEnumerator()`

Enumerates the cards in order.

```csharp
public IEnumerator<FitsCard> GetEnumerator()
```

<a id="fitsnet-fitsheader-insert-system-int32-fitsnet-fitscard"></a>
### `Insert(int, FitsCard)`

Inserts a card at an index.

```csharp
public FitsHeader Insert(int index, FitsCard card)
```

**Parameters**

- `index` — The zero-based position of the new card.
- `card` — The card to insert.

**Returns**

This header, so that calls can be chained.

**Exceptions**

- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — The card is the `END` card, which is implicit.

<a id="fitsnet-fitsheader-read-system-io-stream-fitsnet-fitsreadoptions"></a>
### `Read(Stream, FitsReadOptions)`

Reads a header (all 2880-byte blocks up to and including the one holding END) from the stream.

```csharp
public static FitsHeader Read(Stream stream, FitsReadOptions? options = null)
```

**Parameters**

- `stream` — The stream, positioned at the start of a header.
- `options` — The reading options, or null for the defaults.

<a id="fitsnet-fitsheader-readasync-system-io-stream-fitsnet-fitsreadoptions-system-threading-cancellationtoken"></a>
### `ReadAsync(Stream, FitsReadOptions, CancellationToken)`

Asynchronous version of [`FitsHeader.Read`](#fitsnet-fitsheader-read-system-io-stream-fitsnet-fitsreadoptions).

```csharp
public static Task<FitsHeader> ReadAsync(Stream stream, FitsReadOptions? options = null, CancellationToken cancellationToken = default)
```

**Parameters**

- `stream` — The stream, positioned at the start of a header.
- `options` — The reading options, or null for the defaults.
- `cancellationToken` — A token that cancels the read.

<a id="fitsnet-fitsheader-remove-system-string"></a>
### `Remove(string)`

Removes every value card with the given keyword; returns how many were removed.

```csharp
public int Remove(string keyword)
```

**Parameters**

- `keyword` — The keyword whose cards are removed.

<a id="fitsnet-fitsheader-removeat-system-int32"></a>
### `RemoveAt(int)`

Removes the card at an index.

```csharp
public void RemoveAt(int index)
```

**Parameters**

- `index` — The zero-based index of the card.

<a id="fitsnet-fitsheader-set-system-string-system-object-system-string"></a>
### `Set(string, object, string)`

Replaces the value of the first matching card or appends a new card.

```csharp
public FitsHeader Set(string keyword, object? value, string? comment = null)
```

**Parameters**

- `keyword` — The keyword.
- `value` — The new value: a `bool`, an integer, a `double`, a [`Complex`](https://learn.microsoft.com/dotnet/api/system.numerics.complex) or a `string`; null for an undefined value.
- `comment` — The comment; null keeps the comment the card already has.

<a id="fitsnet-fitsheader-setcomment-system-string-system-string"></a>
### `SetComment(string, string)`

Sets or clears (with `null`) the comment of the first card with the given keyword.

```csharp
public FitsHeader SetComment(string keyword, string? comment)
```

**Parameters**

- `keyword` — The keyword of the card.
- `comment` — The new comment, or null to remove it.

<a id="fitsnet-fitsheader-sethierarch-system-string-system-object-system-string"></a>
### `SetHierarch(string, object, string)`

Like [`FitsHeader.Set`](#fitsnet-fitsheader-set-system-string-system-object-system-string) for a long or unusual keyword, written with the HIERARCH convention.

```csharp
public FitsHeader SetHierarch(string keyword, object? value, string? comment = null)
```

**Parameters**

- `keyword` — The keyword, which may be longer than 8 characters and contain several words.
- `value` — The new value (see [`FitsHeader.Set`](#fitsnet-fitsheader-set-system-string-system-object-system-string)).
- `comment` — The comment; null keeps the comment the card already has.

<a id="fitsnet-fitsheader-tobytes"></a>
### `ToBytes()`

The header as it is written in a file: a whole number of 2880-byte blocks.

```csharp
public byte[] ToBytes()
```

<a id="fitsnet-fitsheader-trygetvalue-1-system-string-0"></a>
### `TryGetValue<T>(string, T)`

Gets a value converted to `T`; false if absent, undefined or not convertible.

```csharp
public bool TryGetValue<T>(string keyword, out T value)
```

**Type parameters**

- `T` — The type to convert the value to, for example `int`, `long`, `double`, `bool` or `string`.

**Parameters**

- `keyword` — The keyword to look for.
- `value` — The converted value, or the default of the type when the method returns false.

<a id="fitsnet-fitsheader-write-system-io-stream"></a>
### `Write(Stream)`

Writes the header to a stream: the cards, the `END` card and the blank padding up to a multiple of 2880 bytes.

```csharp
public void Write(Stream stream)
```

**Parameters**

- `stream` — The stream to write to.

<a id="fitsnet-fitsheader-writeasync-system-io-stream-system-threading-cancellationtoken"></a>
### `WriteAsync(Stream, CancellationToken)`

Asynchronous version of [`FitsHeader.Write`](#fitsnet-fitsheader-write-system-io-stream).

```csharp
public Task WriteAsync(Stream stream, CancellationToken cancellationToken = default)
```

**Parameters**

- `stream` — The stream to write to.
- `cancellationToken` — A token that cancels the write.
