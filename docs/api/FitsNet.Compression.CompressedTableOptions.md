# CompressedTableOptions Class

**Namespace:** `FitsNet.Compression`

**Inheritance:** `object` → `CompressedTableOptions`

Parameters for creating a tile-compressed table ([`CompressedTableHdu.Compress`](FitsNet.CompressedTableHdu.md#fitsnet-compressedtablehdu-compress-fitsnet-binarytablehdu-fitsnet-compression-compressedtableoptions)).

```csharp
public sealed class CompressedTableOptions
```

## Constructors

| Member | Description |
| --- | --- |
| [`CompressedTableOptions()`](#fitsnet-compression-compressedtableoptions-ctor) | Creates options with the default settings (the algorithm chosen for each column, tiles of about 2 MB). |

## Properties

| Member | Description |
| --- | --- |
| [`Algorithm`](#fitsnet-compression-compressedtableoptions-algorithm) | The algorithm of every column, or null to choose per column: Rice for 8, 16 and 32 bit integers, GZIP\_1 for logical, bit and character data, GZIP\_2 for the rest. |
| [`ColumnAlgorithms`](#fitsnet-compression-compressedtableoptions-columnalgorithms) | Algorithm of single columns (zero-based index); it wins over [`CompressedTableOptions.Algorithm`](#fitsnet-compression-compressedtableoptions-algorithm). |
| [`MaxDegreeOfParallelism`](#fitsnet-compression-compressedtableoptions-maxdegreeofparallelism) | Tiles compressed at the same time (-1: one per processor core). |
| [`TileRows`](#fitsnet-compression-compressedtableoptions-tilerows) | Rows of the table in each tile; 0 chooses tiles of about 2 MB (or the FZTILELN directive of the header). |

## Constructor details

<a id="fitsnet-compression-compressedtableoptions-ctor"></a>
### `CompressedTableOptions()`

Creates options with the default settings (the algorithm chosen for each column, tiles of about 2 MB).

```csharp
public CompressedTableOptions()
```

## Property details

<a id="fitsnet-compression-compressedtableoptions-algorithm"></a>
### `Algorithm`

The algorithm of every column, or null to choose per column: Rice for 8, 16 and 32 bit integers, GZIP\_1 for logical, bit and character data, GZIP\_2 for the rest. Rice requested here falls back to GZIP\_2 for the columns it cannot compress.

```csharp
public CompressionAlgorithm? Algorithm { get; set; }
```

<a id="fitsnet-compression-compressedtableoptions-columnalgorithms"></a>
### `ColumnAlgorithms`

Algorithm of single columns (zero-based index); it wins over [`CompressedTableOptions.Algorithm`](#fitsnet-compression-compressedtableoptions-algorithm). Rice is refused for non-integer columns.

```csharp
public IDictionary<int, CompressionAlgorithm> ColumnAlgorithms { get; }
```

<a id="fitsnet-compression-compressedtableoptions-maxdegreeofparallelism"></a>
### `MaxDegreeOfParallelism`

Tiles compressed at the same time (-1: one per processor core).

```csharp
public int MaxDegreeOfParallelism { get; set; }
```

<a id="fitsnet-compression-compressedtableoptions-tilerows"></a>
### `TileRows`

Rows of the table in each tile; 0 chooses tiles of about 2 MB (or the FZTILELN directive of the header).

```csharp
public long TileRows { get; set; }
```
