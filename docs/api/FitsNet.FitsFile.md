# FitsFile Class

**Namespace:** `FitsNet`

**Inheritance:** `object` → `FitsFile`

A FITS file: a primary HDU followed by zero or more extensions. Not thread-safe. HDUs belong to one file at a time: do not share an HDU (it shares its [`Hdu.Header`](FitsNet.Hdu.md#fitsnet-hdu-header)) between files.

```csharp
public sealed class FitsFile : IDisposable, IReadOnlyList<Hdu>
```

## Constructors

| Member | Description |
| --- | --- |
| [`FitsFile()`](#fitsnet-fitsfile-ctor) | Creates an empty file; the first HDU added becomes the primary HDU. |

## Properties

| Member | Description |
| --- | --- |
| [`Count`](#fitsnet-fitsfile-count) | The number of HDUs. |
| [`Primary`](#fitsnet-fitsfile-primary) | The primary HDU. |
| [`this[int]`](#fitsnet-fitsfile-item-system-int32) | The HDU at a zero-based index (0 is the primary HDU). |

## Methods

| Member | Description |
| --- | --- |
| [`Add(Hdu)`](#fitsnet-fitsfile-add-fitsnet-hdu) | Appends an HDU. An empty primary image is inserted first if the file is empty and the HDU cannot be primary. |
| [`Dispose()`](#fitsnet-fitsfile-dispose) | Releases the source file of a lazily opened file; HDUs still reading from it become unusable. |
| [`GetEnumerator()`](#fitsnet-fitsfile-getenumerator) | Enumerates the HDUs in file order. |
| [`Insert(int, Hdu)`](#fitsnet-fitsfile-insert-system-int32-fitsnet-hdu) | Inserts an HDU. Inserting at index 0 makes the current primary an extension, and a non-image HDU inserted at index 0 gets an empty primary image in front of it (shifting later indexes by one). |
| [`Open(string, FitsReadOptions)`](#fitsnet-fitsfile-open-system-string-fitsnet-fitsreadoptions) | Opens a file lazily: pixel data is read from disk on demand. |
| [`Read(Stream, FitsReadOptions)`](#fitsnet-fitsfile-read-system-io-stream-fitsnet-fitsreadoptions) | Reads a whole file from a stream into memory. |
| [`Remove(Hdu)`](#fitsnet-fitsfile-remove-fitsnet-hdu) | Removes an HDU; returns false if it is not part of this file. |
| [`RemoveAt(int)`](#fitsnet-fitsfile-removeat-system-int32) | Removes the HDU at an index. |
| [`Save(Stream, ChecksumPolicy)`](#fitsnet-fitsfile-save-system-io-stream-fitsnet-checksumpolicy) | Writes the file to a stream. |
| [`Save(string, ChecksumPolicy)`](#fitsnet-fitsfile-save-system-string-fitsnet-checksumpolicy) | Writes the file to `path`: a temporary file next to the target is written completely, flushed, then moved over it, so a failure leaves the original untouched. |
| [`VerifyChecksums()`](#fitsnet-fitsfile-verifychecksums) | Checks CHECKSUM and DATASUM of every HDU (see [`Hdu.VerifyChecksum`](FitsNet.Hdu.md#fitsnet-hdu-verifychecksum)). |

## Constructor details

<a id="fitsnet-fitsfile-ctor"></a>
### `FitsFile()`

Creates an empty file; the first HDU added becomes the primary HDU.

```csharp
public FitsFile()
```

## Property details

<a id="fitsnet-fitsfile-count"></a>
### `Count`

The number of HDUs.

```csharp
public int Count { get; }
```

<a id="fitsnet-fitsfile-primary"></a>
### `Primary`

The primary HDU.

```csharp
public Hdu Primary { get; }
```

<a id="fitsnet-fitsfile-item-system-int32"></a>
### `this[int]`

The HDU at a zero-based index (0 is the primary HDU).

```csharp
public Hdu this[int index] { get; }
```

**Parameters**

- `index` — The zero-based index of the HDU (0 is the primary HDU).

## Method details

<a id="fitsnet-fitsfile-add-fitsnet-hdu"></a>
### `Add(Hdu)`

Appends an HDU. An empty primary image is inserted first if the file is empty and the HDU cannot be primary.

```csharp
public FitsFile Add(Hdu hdu)
```

**Parameters**

- `hdu` — The HDU to add.

**Exceptions**

- [`ArgumentNullException`](https://learn.microsoft.com/dotnet/api/system.argumentnullexception) — `hdu` is `null`.
- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — The HDU is already part of this file.
- [`ObjectDisposedException`](https://learn.microsoft.com/dotnet/api/system.objectdisposedexception) — The file is disposed.

<a id="fitsnet-fitsfile-dispose"></a>
### `Dispose()`

Releases the source file of a lazily opened file; HDUs still reading from it become unusable.

```csharp
public void Dispose()
```

<a id="fitsnet-fitsfile-getenumerator"></a>
### `GetEnumerator()`

Enumerates the HDUs in file order.

```csharp
public IEnumerator<Hdu> GetEnumerator()
```

<a id="fitsnet-fitsfile-insert-system-int32-fitsnet-hdu"></a>
### `Insert(int, Hdu)`

Inserts an HDU. Inserting at index 0 makes the current primary an extension, and a non-image HDU inserted at index 0 gets an empty primary image in front of it (shifting later indexes by one).

```csharp
public FitsFile Insert(int index, Hdu hdu)
```

**Parameters**

- `index` — The zero-based position of the new HDU.
- `hdu` — The HDU to insert.

**Exceptions**

- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — `index` is negative or greater than the number of HDUs.
- [`ArgumentNullException`](https://learn.microsoft.com/dotnet/api/system.argumentnullexception) — `hdu` is `null`.
- [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) — The HDU is already part of this file, a random groups HDU would not be the primary HDU, or the current primary is a random groups HDU and `index` is 0.
- [`ObjectDisposedException`](https://learn.microsoft.com/dotnet/api/system.objectdisposedexception) — The file is disposed.

<a id="fitsnet-fitsfile-open-system-string-fitsnet-fitsreadoptions"></a>
### `Open(string, FitsReadOptions)`

Opens a file lazily: pixel data is read from disk on demand. Dispose the result to release the file.

```csharp
public static FitsFile Open(string path, FitsReadOptions? options = null)
```

**Parameters**

- `path` — The path of the file.
- `options` — The reading options, or null for the defaults.

<a id="fitsnet-fitsfile-read-system-io-stream-fitsnet-fitsreadoptions"></a>
### `Read(Stream, FitsReadOptions)`

Reads a whole file from a stream into memory. The stream is left open.

```csharp
public static FitsFile Read(Stream stream, FitsReadOptions? options = null)
```

**Parameters**

- `stream` — The stream to read from, positioned at the start of the file; it is left open.
- `options` — The reading options, or null for the defaults.

<a id="fitsnet-fitsfile-remove-fitsnet-hdu"></a>
### `Remove(Hdu)`

Removes an HDU; returns false if it is not part of this file. Same restrictions as [`FitsFile.RemoveAt`](#fitsnet-fitsfile-removeat-system-int32).

```csharp
public bool Remove(Hdu hdu)
```

**Parameters**

- `hdu` — The HDU to remove.

<a id="fitsnet-fitsfile-removeat-system-int32"></a>
### `RemoveAt(int)`

Removes the HDU at an index.

```csharp
public void RemoveAt(int index)
```

**Parameters**

- `index` — The zero-based index of the HDU to remove.

**Exceptions**

- [`InvalidOperationException`](https://learn.microsoft.com/dotnet/api/system.invalidoperationexception) — The next HDU cannot become the primary HDU.
- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — `index` is not the index of an HDU.
- [`ObjectDisposedException`](https://learn.microsoft.com/dotnet/api/system.objectdisposedexception) — The file is disposed.

<a id="fitsnet-fitsfile-save-system-io-stream-fitsnet-checksumpolicy"></a>
### `Save(Stream, ChecksumPolicy)`

Writes the file to a stream.

```csharp
public void Save(Stream stream, ChecksumPolicy checksums = ChecksumPolicy.Auto)
```

**Parameters**

- `stream` — The destination.
- `checksums` — What to do with the CHECKSUM and DATASUM keywords.

<a id="fitsnet-fitsfile-save-system-string-fitsnet-checksumpolicy"></a>
### `Save(string, ChecksumPolicy)`

Writes the file to `path`: a temporary file next to the target is written completely, flushed, then moved over it, so a failure leaves the original untouched. When overwriting the file this instance was opened from, the lazily read HDUs are re-attached to the new file. HDUs previously removed from this file lose access to their data in that case. An existing target keeps its attributes and permissions (File.Replace), and a symbolic link is written through to its target. A save that fails leaves the headers and the state of the HDUs as they were.

```csharp
public void Save(string path, ChecksumPolicy checksums = ChecksumPolicy.Auto)
```

**Parameters**

- `path` — The path of the file to write.
- `checksums` — What to do with the `CHECKSUM` and `DATASUM` keywords.

**Exceptions**

- [`IOException`](https://learn.microsoft.com/dotnet/api/system.io.ioexception) — The file could not be written or put in place. When the file this instance reads from was being replaced and could not be reopened afterwards (or, in the worst case, after a failed replacement), the instance can no longer read its data: dispose it and open the file again; the message says which case it is.

<a id="fitsnet-fitsfile-verifychecksums"></a>
### `VerifyChecksums()`

Checks CHECKSUM and DATASUM of every HDU (see [`Hdu.VerifyChecksum`](FitsNet.Hdu.md#fitsnet-hdu-verifychecksum)).

```csharp
public IReadOnlyList<ChecksumResult> VerifyChecksums()
```
