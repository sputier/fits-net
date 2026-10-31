# WorldCoordinateSystemExtensions Class

**Namespace:** `FitsNet.Wcs`

**Inheritance:** `object` → `WorldCoordinateSystemExtensions`

Adds the WCS to the HDU classes whose header can carry one.

```csharp
public static class WorldCoordinateSystemExtensions
```

## Methods

| Member | Description |
| --- | --- |
| [`GetWorldCoordinateSystem(Hdu, char)`](#fitsnet-wcs-worldcoordinatesystemextensions-getworldcoordinatesystem-fitsnet-hdu-system-char) | The world coordinate system described by the header of the HDU (see [`WorldCoordinateSystem.FromHeader`](FitsNet.Wcs.WorldCoordinateSystem.md#fitsnet-wcs-worldcoordinatesystem-fromheader-fitsnet-fitsheader-system-char)). |

## Method details

<a id="fitsnet-wcs-worldcoordinatesystemextensions-getworldcoordinatesystem-fitsnet-hdu-system-char"></a>
### `GetWorldCoordinateSystem(Hdu, char)`

The world coordinate system described by the header of the HDU (see [`WorldCoordinateSystem.FromHeader`](FitsNet.Wcs.WorldCoordinateSystem.md#fitsnet-wcs-worldcoordinatesystem-fromheader-fitsnet-fitsheader-system-char)).

```csharp
public static WorldCoordinateSystem GetWorldCoordinateSystem(this Hdu hdu, char alternate = ' ')
```

**Parameters**

- `hdu` — The HDU.
- `alternate` — The alternate description: a space for the primary one, or a letter from A to Z.
