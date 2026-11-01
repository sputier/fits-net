# WorldCoordinateSystemExtensions Class

**Namespace:** `Eleris.FitsNet.Wcs`

**Inheritance:** `object` → `WorldCoordinateSystemExtensions`

Adds the WCS to the HDU classes whose header can carry one.

```csharp
public static class WorldCoordinateSystemExtensions
```

## Methods

| Member | Description |
| --- | --- |
| [`GetWorldCoordinateSystem(Hdu, char)`](#eleris-fitsnet-wcs-worldcoordinatesystemextensions-getworldcoordinatesystem-eleris-fitsnet-hdu-system-char) | The world coordinate system described by the header of the HDU (see [`WorldCoordinateSystem.FromHeader`](Eleris.FitsNet.Wcs.WorldCoordinateSystem.md#eleris-fitsnet-wcs-worldcoordinatesystem-fromheader-eleris-fitsnet-fitsheader-system-char)). |

## Method details

<a id="eleris-fitsnet-wcs-worldcoordinatesystemextensions-getworldcoordinatesystem-eleris-fitsnet-hdu-system-char"></a>
### `GetWorldCoordinateSystem(Hdu, char)`

The world coordinate system described by the header of the HDU (see [`WorldCoordinateSystem.FromHeader`](Eleris.FitsNet.Wcs.WorldCoordinateSystem.md#eleris-fitsnet-wcs-worldcoordinatesystem-fromheader-eleris-fitsnet-fitsheader-system-char)).

```csharp
public static WorldCoordinateSystem GetWorldCoordinateSystem(this Hdu hdu, char alternate = ' ')
```

**Parameters**

- `hdu` — The HDU.
- `alternate` — The alternate description: a space for the primary one, or a letter from A to Z.
