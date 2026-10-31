# FitsReadOptions Record

**Namespace:** `FitsNet`

**Inheritance:** `object` → `FitsReadOptions`

Options controlling how strictly files are parsed.

```csharp
public sealed record FitsReadOptions : IEquatable<FitsReadOptions>
```

## Constructors

| Member | Description |
| --- | --- |
| [`FitsReadOptions(bool, int, int, long)`](#fitsnet-fitsreadoptions-ctor-system-boolean-system-int32-system-int32-system-int64) | Options controlling how strictly files are parsed. |

## Properties

| Member | Description |
| --- | --- |
| [`Default`](#fitsnet-fitsreadoptions-default) | The default options: tolerant reading with the default limits. |
| [`MaxHeaderBlocks`](#fitsnet-fitsreadoptions-maxheaderblocks) | Upper bound on 2880-byte blocks read for one header, protecting against files without END. |
| [`MaxInputBytes`](#fitsnet-fitsreadoptions-maxinputbytes) | Upper bound on the bytes [`FitsFile.Read`](FitsNet.FitsFile.md#fitsnet-fitsfile-read-system-io-stream-fitsnet-fitsreadoptions) takes from a stream into memory (never more than the 2 GB of a .NET array, which is the default); [`FitsFile.Open`](FitsNet.FitsFile.md#fitsnet-fitsfile-open-system-string-fitsnet-fitsreadoptions) reads on demand and ignores it. |
| [`MaxTotalHeaderBlocks`](#fitsnet-fitsreadoptions-maxtotalheaderblocks) | Upper bound on the 2880-byte header blocks of a whole file, all its HDUs together (it also bounds the number of extensions of a file whose headers are one block each); it applies to [`FitsFile.Open`](FitsNet.FitsFile.md#fitsnet-fitsfile-open-system-string-fitsnet-fitsreadoptions) as well as to [`FitsFile.Read`](FitsNet.FitsFile.md#fitsnet-fitsfile-read-system-io-stream-fitsnet-fitsreadoptions). |
| [`Strict`](#fitsnet-fitsreadoptions-strict) | When true, any deviation from the standard throws a [`FitsException`](FitsNet.FitsException.md). |

## Constructor details

<a id="fitsnet-fitsreadoptions-ctor-system-boolean-system-int32-system-int32-system-int64"></a>
### `FitsReadOptions(bool, int, int, long)`

Options controlling how strictly files are parsed.

```csharp
public FitsReadOptions(bool Strict = false, int MaxHeaderBlocks = 10000, int MaxTotalHeaderBlocks = 50000, long MaxInputBytes = 2147483591L)
```

**Parameters**

- `Strict` — When true, any deviation from the standard throws a [`FitsException`](FitsNet.FitsException.md).
- `MaxHeaderBlocks` — Upper bound on 2880-byte blocks read for one header, protecting against files without END.
- `MaxTotalHeaderBlocks` — Upper bound on the 2880-byte header blocks of a whole file, all its HDUs together (it also bounds the number of extensions of a file whose headers are one block each); it applies to [`FitsFile.Open`](FitsNet.FitsFile.md#fitsnet-fitsfile-open-system-string-fitsnet-fitsreadoptions) as well as to [`FitsFile.Read`](FitsNet.FitsFile.md#fitsnet-fitsfile-read-system-io-stream-fitsnet-fitsreadoptions).
- `MaxInputBytes` — Upper bound on the bytes [`FitsFile.Read`](FitsNet.FitsFile.md#fitsnet-fitsfile-read-system-io-stream-fitsnet-fitsreadoptions) takes from a stream into memory (never more than the 2 GB of a .NET array, which is the default); [`FitsFile.Open`](FitsNet.FitsFile.md#fitsnet-fitsfile-open-system-string-fitsnet-fitsreadoptions) reads on demand and ignores it.

## Property details

<a id="fitsnet-fitsreadoptions-default"></a>
### `Default`

The default options: tolerant reading with the default limits.

```csharp
public static FitsReadOptions Default { get; }
```

<a id="fitsnet-fitsreadoptions-maxheaderblocks"></a>
### `MaxHeaderBlocks`

Upper bound on 2880-byte blocks read for one header, protecting against files without END.

```csharp
public int MaxHeaderBlocks { get; init; }
```

<a id="fitsnet-fitsreadoptions-maxinputbytes"></a>
### `MaxInputBytes`

Upper bound on the bytes [`FitsFile.Read`](FitsNet.FitsFile.md#fitsnet-fitsfile-read-system-io-stream-fitsnet-fitsreadoptions) takes from a stream into memory (never more than the 2 GB of a .NET array, which is the default); [`FitsFile.Open`](FitsNet.FitsFile.md#fitsnet-fitsfile-open-system-string-fitsnet-fitsreadoptions) reads on demand and ignores it.

```csharp
public long MaxInputBytes { get; init; }
```

<a id="fitsnet-fitsreadoptions-maxtotalheaderblocks"></a>
### `MaxTotalHeaderBlocks`

Upper bound on the 2880-byte header blocks of a whole file, all its HDUs together (it also bounds the number of extensions of a file whose headers are one block each); it applies to [`FitsFile.Open`](FitsNet.FitsFile.md#fitsnet-fitsfile-open-system-string-fitsnet-fitsreadoptions) as well as to [`FitsFile.Read`](FitsNet.FitsFile.md#fitsnet-fitsfile-read-system-io-stream-fitsnet-fitsreadoptions).

```csharp
public int MaxTotalHeaderBlocks { get; init; }
```

<a id="fitsnet-fitsreadoptions-strict"></a>
### `Strict`

When true, any deviation from the standard throws a [`FitsException`](FitsNet.FitsException.md).

```csharp
public bool Strict { get; init; }
```
