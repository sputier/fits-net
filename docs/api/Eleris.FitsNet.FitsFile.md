# FitsFile Class

**Namespace:** `Eleris.FitsNet`

**Inheritance:** `object` → `FitsFile`

A FITS file: a primary HDU followed by zero or more extensions. Not thread-safe. HDUs belong to one file at a time: do not share an HDU (it shares its [`Hdu.Header`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-header)) between files.

```csharp
public sealed class FitsFile : IDisposable, IReadOnlyList<Hdu>
```

## Constructors

| Member | Description |
| --- | --- |
| [`FitsFile()`](#eleris-fitsnet-fitsfile-ctor) | Creates an empty file; the first HDU added becomes the primary HDU. |

## Properties

| Member | Description |
| --- | --- |
| [`Count`](#eleris-fitsnet-fitsfile-count) | The number of HDUs. |
| [`Primary`](#eleris-fitsnet-fitsfile-primary) | The primary HDU. |
| [`this[int]`](#eleris-fitsnet-fitsfile-item-system-int32) | The HDU at a zero-based index (0 is the primary HDU). |

## Methods

| Member | Description |
| --- | --- |
| [`Add(Hdu)`](#eleris-fitsnet-fitsfile-add-eleris-fitsnet-hdu) | Appends an HDU. An empty primary image is inserted first if the file is empty and the HDU cannot be primary. |
| [`Dispose()`](#eleris-fitsnet-fitsfile-dispose) | Releases the source file of a lazily opened file; HDUs still reading from it become unusable. |
| [`GetEnumerator()`](#eleris-fitsnet-fitsfile-getenumerator) | Enumerates the HDUs in file order. |
| [`Insert(int, Hdu)`](#eleris-fitsnet-fitsfile-insert-system-int32-eleris-fitsnet-hdu) | Inserts an HDU. Inserting at index 0 makes the current primary an extension, and a non-image HDU inserted at index 0 gets an empty primary image in front of it (shifting later indexes by one). |
| [`Open(string, FitsReadOptions)`](#eleris-fitsnet-fitsfile-open-system-string-eleris-fitsnet-fitsreadoptions) | Opens a file lazily: pixel data is read from disk on demand. |
| [`Read(Stream, FitsReadOptions)`](#eleris-fitsnet-fitsfile-read-system-io-stream-eleris-fitsnet-fitsreadoptions) | Reads a whole file from a stream into memory. |
| [`Remove(Hdu)`](#eleris-fitsnet-fitsfile-remove-eleris-fitsnet-hdu) | Removes an HDU; returns false if it is not part of this file. |
| [`RemoveAt(int)`](#eleris-fitsnet-fitsfile-removeat-system-int32) | Removes the HDU at an index. |
| [`Save(Stream, ChecksumPolicy)`](#eleris-fitsnet-fitsfile-save-system-io-stream-eleris-fitsnet-checksumpolicy) | Writes the file to a stream. |
| [`Save(string, ChecksumPolicy)`](#eleris-fitsnet-fitsfile-save-system-string-eleris-fitsnet-checksumpolicy) | Writes the file to `path`: a temporary file next to the target is written completely, flushed, then moved over it, so a failure leaves the original untouched. |
| [`Save(Stream, ChecksumPolicy, CancellationToken)`](#eleris-fitsnet-fitsfile-save-system-io-stream-eleris-fitsnet-checksumpolicy-system-threading-cancellationtoken) | Writes the file to a stream, checking `cancellationToken` between blocks of data. |
| [`Save(string, ChecksumPolicy, CancellationToken)`](#eleris-fitsnet-fitsfile-save-system-string-eleris-fitsnet-checksumpolicy-system-threading-cancellationtoken) | Writes the file to `path` as [`FitsFile.Save`](#eleris-fitsnet-fitsfile-save-system-string-eleris-fitsnet-checksumpolicy) does, checking `cancellationToken` between blocks of data. |
| [`Save(string, ChecksumPolicy, FitsSaveMode, CancellationToken)`](#eleris-fitsnet-fitsfile-save-system-string-eleris-fitsnet-checksumpolicy-eleris-fitsnet-fitssavemode-system-threading-cancellationtoken) | Writes the file to `path`, choosing with `mode` what happens when a file already exists there. |
| [`VerifyChecksums()`](#eleris-fitsnet-fitsfile-verifychecksums) | Checks CHECKSUM and DATASUM of every HDU (see [`Hdu.VerifyChecksum`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-verifychecksum)). |

## Constructor details

<a id="eleris-fitsnet-fitsfile-ctor"></a>
### `FitsFile()`

Creates an empty file; the first HDU added becomes the primary HDU.

```csharp
public FitsFile()
```

## Property details

<a id="eleris-fitsnet-fitsfile-count"></a>
### `Count`

The number of HDUs.

```csharp
public int Count { get; }
```

<a id="eleris-fitsnet-fitsfile-primary"></a>
### `Primary`

The primary HDU.

```csharp
public Hdu Primary { get; }
```

<a id="eleris-fitsnet-fitsfile-item-system-int32"></a>
### `this[int]`

The HDU at a zero-based index (0 is the primary HDU).

```csharp
public Hdu this[int index] { get; }
```

**Parameters**

- `index` — The zero-based index of the HDU (0 is the primary HDU).

## Method details

<a id="eleris-fitsnet-fitsfile-add-eleris-fitsnet-hdu"></a>
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

<a id="eleris-fitsnet-fitsfile-dispose"></a>
### `Dispose()`

Releases the source file of a lazily opened file; HDUs still reading from it become unusable.

```csharp
public void Dispose()
```

<a id="eleris-fitsnet-fitsfile-getenumerator"></a>
### `GetEnumerator()`

Enumerates the HDUs in file order.

```csharp
public IEnumerator<Hdu> GetEnumerator()
```

<a id="eleris-fitsnet-fitsfile-insert-system-int32-eleris-fitsnet-hdu"></a>
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

<a id="eleris-fitsnet-fitsfile-open-system-string-eleris-fitsnet-fitsreadoptions"></a>
### `Open(string, FitsReadOptions)`

Opens a file lazily: pixel data is read from disk on demand. Dispose the result to release the file.

```csharp
public static FitsFile Open(string path, FitsReadOptions? options = null)
```

**Parameters**

- `path` — The path of the file.
- `options` — The reading options, or null for the defaults.

<a id="eleris-fitsnet-fitsfile-read-system-io-stream-eleris-fitsnet-fitsreadoptions"></a>
### `Read(Stream, FitsReadOptions)`

Reads a whole file from a stream into memory. The stream is left open.

```csharp
public static FitsFile Read(Stream stream, FitsReadOptions? options = null)
```

**Parameters**

- `stream` — The stream to read from, positioned at the start of the file; it is left open.
- `options` — The reading options, or null for the defaults.

<a id="eleris-fitsnet-fitsfile-remove-eleris-fitsnet-hdu"></a>
### `Remove(Hdu)`

Removes an HDU; returns false if it is not part of this file. Same restrictions as [`FitsFile.RemoveAt`](#eleris-fitsnet-fitsfile-removeat-system-int32).

```csharp
public bool Remove(Hdu hdu)
```

**Parameters**

- `hdu` — The HDU to remove.

<a id="eleris-fitsnet-fitsfile-removeat-system-int32"></a>
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

<a id="eleris-fitsnet-fitsfile-save-system-io-stream-eleris-fitsnet-checksumpolicy"></a>
### `Save(Stream, ChecksumPolicy)`

Writes the file to a stream.

```csharp
public void Save(Stream stream, ChecksumPolicy checksums = ChecksumPolicy.Auto)
```

**Parameters**

- `stream` — The destination.
- `checksums` — What to do with the CHECKSUM and DATASUM keywords.

<a id="eleris-fitsnet-fitsfile-save-system-string-eleris-fitsnet-checksumpolicy"></a>
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

<a id="eleris-fitsnet-fitsfile-save-system-io-stream-eleris-fitsnet-checksumpolicy-system-threading-cancellationtoken"></a>
### `Save(Stream, ChecksumPolicy, CancellationToken)`

Writes the file to a stream, checking `cancellationToken` between blocks of data. A cancelled save throws an [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) and leaves the headers and the state of the HDUs as they were, but the stream may already hold the beginning of the file: discard it.

```csharp
public void Save(Stream stream, ChecksumPolicy checksums, CancellationToken cancellationToken)
```

**Parameters**

- `stream` — The destination.
- `checksums` — What to do with the CHECKSUM and DATASUM keywords.
- `cancellationToken` — The token that cancels the save.

**Exceptions**

- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled before the file was written completely.

<a id="eleris-fitsnet-fitsfile-save-system-string-eleris-fitsnet-checksumpolicy-system-threading-cancellationtoken"></a>
### `Save(string, ChecksumPolicy, CancellationToken)`

Writes the file to `path` as [`FitsFile.Save`](#eleris-fitsnet-fitsfile-save-system-string-eleris-fitsnet-checksumpolicy) does, checking `cancellationToken` between blocks of data. A cancelled save deletes its temporary file and leaves the target, the headers and the state of the HDUs as they were. Once the temporary file is complete and is being put in place, the save is no longer cancelled.

```csharp
public void Save(string path, ChecksumPolicy checksums, CancellationToken cancellationToken)
```

**Parameters**

- `path` — The path of the file to write.
- `checksums` — What to do with the `CHECKSUM` and `DATASUM` keywords.
- `cancellationToken` — The token that cancels the save.

**Exceptions**

- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled before the file was put in place.
- [`IOException`](https://learn.microsoft.com/dotnet/api/system.io.ioexception) — The file could not be written or put in place (see [`FitsFile.Save`](#eleris-fitsnet-fitsfile-save-system-string-eleris-fitsnet-checksumpolicy)).

<a id="eleris-fitsnet-fitsfile-save-system-string-eleris-fitsnet-checksumpolicy-eleris-fitsnet-fitssavemode-system-threading-cancellationtoken"></a>
### `Save(string, ChecksumPolicy, FitsSaveMode, CancellationToken)`

Writes the file to `path`, choosing with `mode` what happens when a file already exists there. [`FitsSaveMode.Overwrite`](Eleris.FitsNet.FitsSaveMode.md#eleris-fitsnet-fitssavemode-overwrite) is [`FitsFile.Save`](#eleris-fitsnet-fitsfile-save-system-string-eleris-fitsnet-checksumpolicy-system-threading-cancellationtoken). [`FitsSaveMode.CreateNew`](Eleris.FitsNet.FitsSaveMode.md#eleris-fitsnet-fitssavemode-createnew) never replaces anything: the file is written to a temporary file in the same directory, then moved to `path` by an operation that fails if the path exists, so a file that appears while the save runs is not replaced either (no check-then-write race; on Unix, where .NET makes this move with a hard link, on file systems that support hard links). In that mode `path` is taken as it is (a symbolic link there counts as an existing file), and a failure, [`FitsFileExistsException`](Eleris.FitsNet.FitsFileExistsException.md) included, leaves the headers and the state of the HDUs as they were.

```csharp
public void Save(string path, ChecksumPolicy checksums, FitsSaveMode mode, CancellationToken cancellationToken = default)
```

**Parameters**

- `path` — The path of the file to write.
- `checksums` — What to do with the `CHECKSUM` and `DATASUM` keywords.
- `mode` — Whether an existing file may be replaced.
- `cancellationToken` — The token that cancels the save.

**Exceptions**

- [`FitsFileExistsException`](Eleris.FitsNet.FitsFileExistsException.md) — `mode` is [`FitsSaveMode.CreateNew`](Eleris.FitsNet.FitsSaveMode.md#eleris-fitsnet-fitssavemode-createnew) and something exists at `path`, before or after the file was written.
- [`OperationCanceledException`](https://learn.microsoft.com/dotnet/api/system.operationcanceledexception) — `cancellationToken` was cancelled before the file was put in place.
- [`IOException`](https://learn.microsoft.com/dotnet/api/system.io.ioexception) — The file could not be written or put in place (see [`FitsFile.Save`](#eleris-fitsnet-fitsfile-save-system-string-eleris-fitsnet-checksumpolicy)).
- [`ArgumentOutOfRangeException`](https://learn.microsoft.com/dotnet/api/system.argumentoutofrangeexception) — `mode` is not a value of [`FitsSaveMode`](Eleris.FitsNet.FitsSaveMode.md).

<a id="eleris-fitsnet-fitsfile-verifychecksums"></a>
### `VerifyChecksums()`

Checks CHECKSUM and DATASUM of every HDU (see [`Hdu.VerifyChecksum`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-verifychecksum)).

```csharp
public IReadOnlyList<ChecksumResult> VerifyChecksums()
```
