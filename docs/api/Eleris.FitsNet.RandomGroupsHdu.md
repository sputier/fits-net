# RandomGroupsHdu Class

**Namespace:** `Eleris.FitsNet`

**Inheritance:** `object` → [`Hdu`](Eleris.FitsNet.Hdu.md) → `RandomGroupsHdu`

The random groups structure (primary HDU with `GROUPS = T` and `NAXIS1 = 0`): a sequence of groups, each made of PCOUNT parameters followed by a data array.

```csharp
public sealed class RandomGroupsHdu : Hdu
```

## Properties

| Member | Description |
| --- | --- |
| [`BitPix`](#eleris-fitsnet-randomgroupshdu-bitpix) | The data representation (`BITPIX`). |
| [`DataAxes`](#eleris-fitsnet-randomgroupshdu-dataaxes) | NAXIS2..NAXISn: the shape of the data array of each group. |
| [`ElementsPerGroup`](#eleris-fitsnet-randomgroupshdu-elementspergroup) | Number of data elements in each group (excluding parameters). |
| [`GroupCount`](#eleris-fitsnet-randomgroupshdu-groupcount) | The number of groups (`GCOUNT`). |
| [`ParameterCount`](#eleris-fitsnet-randomgroupshdu-parametercount) | The number of parameters in each group (`PCOUNT`). |

## Methods

| Member | Description |
| --- | --- |
| [`Create<T>(ReadOnlySpan<T>, long, long, long[])`](#eleris-fitsnet-randomgroupshdu-create-1-system-readonlyspan-0-system-int64-system-int64-system-int64) | Creates a random groups HDU. `values` holds, for each group, its parameters followed by its data. |
| [`ParameterName(int)`](#eleris-fitsnet-randomgroupshdu-parametername-system-int32) | The PTYPEn name of parameter `index` (0-based), if declared. |
| [`ReadData(long)`](#eleris-fitsnet-randomgroupshdu-readdata-system-int64) | Physical data values of a group: BZERO + BSCALE x stored value (BLANK becomes NaN). |
| [`ReadNamedParameters(long)`](#eleris-fitsnet-randomgroupshdu-readnamedparameters-system-int64) | The parameters of a group by name (PTYPEn, case-insensitive). |
| [`ReadParameters(long)`](#eleris-fitsnet-randomgroupshdu-readparameters-system-int64) | Physical parameter values of a group: PZEROn + PSCALn x stored value. |
| [`ReadRawGroup<T>(long)`](#eleris-fitsnet-randomgroupshdu-readrawgroup-1-system-int64) | Reads the raw stored values of one group (parameters then data). |

## Property details

<a id="eleris-fitsnet-randomgroupshdu-bitpix"></a>
### `BitPix`

The data representation (`BITPIX`).

```csharp
public BitPix BitPix { get; }
```

<a id="eleris-fitsnet-randomgroupshdu-dataaxes"></a>
### `DataAxes`

NAXIS2..NAXISn: the shape of the data array of each group.

```csharp
public IReadOnlyList<long> DataAxes { get; }
```

<a id="eleris-fitsnet-randomgroupshdu-elementspergroup"></a>
### `ElementsPerGroup`

Number of data elements in each group (excluding parameters).

```csharp
public long ElementsPerGroup { get; }
```

<a id="eleris-fitsnet-randomgroupshdu-groupcount"></a>
### `GroupCount`

The number of groups (`GCOUNT`).

```csharp
public long GroupCount { get; }
```

<a id="eleris-fitsnet-randomgroupshdu-parametercount"></a>
### `ParameterCount`

The number of parameters in each group (`PCOUNT`).

```csharp
public long ParameterCount { get; }
```

## Method details

<a id="eleris-fitsnet-randomgroupshdu-create-1-system-readonlyspan-0-system-int64-system-int64-system-int64"></a>
### `Create<T>(ReadOnlySpan<T>, long, long, long[])`

Creates a random groups HDU. `values` holds, for each group, its parameters followed by its data. Supported types: byte, short, int, long, float, double.

```csharp
public static RandomGroupsHdu Create<T>(ReadOnlySpan<T> values, long groupCount, long parameterCount, params long[] dataAxes) where T : unmanaged
```

**Type parameters**

- `T` — The value type: `byte`, `short`, `int`, `long`, `float` or `double`.

**Parameters**

- `values` — All the values: for each group its parameters, then its data array.
- `groupCount` — The number of groups.
- `parameterCount` — The number of parameters in each group.
- `dataAxes` — The length of each axis of the data array of a group.

<a id="eleris-fitsnet-randomgroupshdu-parametername-system-int32"></a>
### `ParameterName(int)`

The PTYPEn name of parameter `index` (0-based), if declared.

```csharp
public string? ParameterName(int index)
```

**Parameters**

- `index` — The zero-based index of the parameter.

<a id="eleris-fitsnet-randomgroupshdu-readdata-system-int64"></a>
### `ReadData(long)`

Physical data values of a group: BZERO + BSCALE x stored value (BLANK becomes NaN).

```csharp
public double[] ReadData(long group)
```

**Parameters**

- `group` — The zero-based index of the group.

<a id="eleris-fitsnet-randomgroupshdu-readnamedparameters-system-int64"></a>
### `ReadNamedParameters(long)`

The parameters of a group by name (PTYPEn, case-insensitive). The standard says that parameters sharing a name are added together (e.g. a date split in two parts). Unnamed parameters are called `PARAMn`.

```csharp
public IReadOnlyDictionary<string, double> ReadNamedParameters(long group)
```

**Parameters**

- `group` — The zero-based index of the group.

<a id="eleris-fitsnet-randomgroupshdu-readparameters-system-int64"></a>
### `ReadParameters(long)`

Physical parameter values of a group: PZEROn + PSCALn x stored value.

```csharp
public double[] ReadParameters(long group)
```

**Parameters**

- `group` — The zero-based index of the group.

<a id="eleris-fitsnet-randomgroupshdu-readrawgroup-1-system-int64"></a>
### `ReadRawGroup<T>(long)`

Reads the raw stored values of one group (parameters then data). `T` must match BITPIX.

```csharp
public T[] ReadRawGroup<T>(long group) where T : unmanaged
```

**Type parameters**

- `T` — The type the stored values are returned as.

**Parameters**

- `group` — The zero-based index of the group.

## Inherited members

- [`Hdu.VerifyChecksum()`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-verifychecksum) — Checks the CHECKSUM and DATASUM keywords against the HDU as it is now in memory: the current header and data, with the fill bytes found on disk while the data is untouched.
- [`Hdu.Header`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-header) — The header.
- [`Hdu.DataLength`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-datalength) — Length in bytes of the data, without block padding.
