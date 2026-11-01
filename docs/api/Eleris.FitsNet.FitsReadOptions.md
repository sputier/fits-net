# FitsReadOptions Record

**Namespace:** `Eleris.FitsNet`

**Inheritance:** `object` → `FitsReadOptions`

Options controlling how strictly files are parsed.

```csharp
public sealed record FitsReadOptions : IEquatable<FitsReadOptions>
```

## Constructors

| Member | Description |
| --- | --- |
| [`FitsReadOptions(bool, int, int, long)`](#eleris-fitsnet-fitsreadoptions-ctor-system-boolean-system-int32-system-int32-system-int64) | Options controlling how strictly files are parsed. |

## Properties

| Member | Description |
| --- | --- |
| [`Default`](#eleris-fitsnet-fitsreadoptions-default) | The default options: tolerant reading with the default limits. |
| [`MaxHeaderBlocks`](#eleris-fitsnet-fitsreadoptions-maxheaderblocks) | Upper bound on 2880-byte blocks read for one header, protecting against files without END. |
| [`MaxInputBytes`](#eleris-fitsnet-fitsreadoptions-maxinputbytes) | Upper bound on the bytes [`FitsFile.Read`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-read-system-io-stream-eleris-fitsnet-fitsreadoptions) takes from a stream into memory (never more than the 2 GB of a .NET array, which is the default); [`FitsFile.Open`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-open-system-string-eleris-fitsnet-fitsreadoptions) reads on demand and ignores it. |
| [`MaxTotalHeaderBlocks`](#eleris-fitsnet-fitsreadoptions-maxtotalheaderblocks) | Upper bound on the 2880-byte header blocks of a whole file, all its HDUs together (it also bounds the number of extensions of a file whose headers are one block each); it applies to [`FitsFile.Open`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-open-system-string-eleris-fitsnet-fitsreadoptions) as well as to [`FitsFile.Read`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-read-system-io-stream-eleris-fitsnet-fitsreadoptions). |
| [`Strict`](#eleris-fitsnet-fitsreadoptions-strict) | When true, any deviation from the standard throws a [`FitsException`](Eleris.FitsNet.FitsException.md). |

## Constructor details

<a id="eleris-fitsnet-fitsreadoptions-ctor-system-boolean-system-int32-system-int32-system-int64"></a>
### `FitsReadOptions(bool, int, int, long)`

Options controlling how strictly files are parsed.

```csharp
public FitsReadOptions(bool Strict = false, int MaxHeaderBlocks = 10000, int MaxTotalHeaderBlocks = 50000, long MaxInputBytes = 2147483591L)
```

**Parameters**

- `Strict` — When true, any deviation from the standard throws a [`FitsException`](Eleris.FitsNet.FitsException.md).
- `MaxHeaderBlocks` — Upper bound on 2880-byte blocks read for one header, protecting against files without END.
- `MaxTotalHeaderBlocks` — Upper bound on the 2880-byte header blocks of a whole file, all its HDUs together (it also bounds the number of extensions of a file whose headers are one block each); it applies to [`FitsFile.Open`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-open-system-string-eleris-fitsnet-fitsreadoptions) as well as to [`FitsFile.Read`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-read-system-io-stream-eleris-fitsnet-fitsreadoptions).
- `MaxInputBytes` — Upper bound on the bytes [`FitsFile.Read`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-read-system-io-stream-eleris-fitsnet-fitsreadoptions) takes from a stream into memory (never more than the 2 GB of a .NET array, which is the default); [`FitsFile.Open`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-open-system-string-eleris-fitsnet-fitsreadoptions) reads on demand and ignores it.

## Property details

<a id="eleris-fitsnet-fitsreadoptions-default"></a>
### `Default`

The default options: tolerant reading with the default limits.

```csharp
public static FitsReadOptions Default { get; }
```

<a id="eleris-fitsnet-fitsreadoptions-maxheaderblocks"></a>
### `MaxHeaderBlocks`

Upper bound on 2880-byte blocks read for one header, protecting against files without END.

```csharp
public int MaxHeaderBlocks { get; init; }
```

<a id="eleris-fitsnet-fitsreadoptions-maxinputbytes"></a>
### `MaxInputBytes`

Upper bound on the bytes [`FitsFile.Read`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-read-system-io-stream-eleris-fitsnet-fitsreadoptions) takes from a stream into memory (never more than the 2 GB of a .NET array, which is the default); [`FitsFile.Open`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-open-system-string-eleris-fitsnet-fitsreadoptions) reads on demand and ignores it.

```csharp
public long MaxInputBytes { get; init; }
```

<a id="eleris-fitsnet-fitsreadoptions-maxtotalheaderblocks"></a>
### `MaxTotalHeaderBlocks`

Upper bound on the 2880-byte header blocks of a whole file, all its HDUs together (it also bounds the number of extensions of a file whose headers are one block each); it applies to [`FitsFile.Open`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-open-system-string-eleris-fitsnet-fitsreadoptions) as well as to [`FitsFile.Read`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-read-system-io-stream-eleris-fitsnet-fitsreadoptions).

```csharp
public int MaxTotalHeaderBlocks { get; init; }
```

<a id="eleris-fitsnet-fitsreadoptions-strict"></a>
### `Strict`

When true, any deviation from the standard throws a [`FitsException`](Eleris.FitsNet.FitsException.md).

```csharp
public bool Strict { get; init; }
```
