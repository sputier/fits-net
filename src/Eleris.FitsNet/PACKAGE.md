# Eleris.FitsNet

A pure managed .NET library (net10.0 and net8.0, no dependency) to read, write and edit **FITS** files, the format of astronomical
images and tables. It covers the FITS 4.0 standard: images, ASCII and binary tables, random groups, tile compression of images and
tables, checksums, world coordinate systems and time coordinates.

```csharp
using Eleris.FitsNet;

using var file = FitsFile.Open("frame.fits");               // reads on demand: a large frame opens in milliseconds
var image = (ImageHdu)file.Primary;
ushort[] pixels = image.ReadPixels<ushort>();                // BZERO / BSCALE / unsigned conventions applied
ushort[] cutout = image.ReadRegion<ushort>([1500, 1500], [256, 256]);   // only these bytes are read

string target = image.Header.Get<string>("OBJECT")!;
image.Header.Set("OBSERVER", "me", "who took it");
```

The namespaces are `Eleris.FitsNet`, `Eleris.FitsNet.Compression` and `Eleris.FitsNet.Wcs`.

* [Documentation and guide](https://github.com/sputier/fits-net/blob/main/docs/index.md)
* [Reference of the public API](https://github.com/sputier/fits-net/blob/main/docs/api/index.md)
* [Coverage of the FITS 4.0 standard](https://github.com/sputier/fits-net/blob/main/docs/standard-coverage.md)
* [Source code and issues](https://github.com/sputier/fits-net)

Released under the MIT license.
