# API reference

Every public type of the library, by namespace. The pages are generated from the XML documentation comments of the code.

## Namespace `Eleris.FitsNet`

| Type | Kind | Description |
| --- | --- | --- |
| [`AsciiTableHdu`](Eleris.FitsNet.AsciiTableHdu.md) | class | An ASCII table extension (`XTENSION = 'TABLE'`). |
| [`BinaryTableHdu`](Eleris.FitsNet.BinaryTableHdu.md) | class | A binary table extension (`XTENSION = 'BINTABLE'`), including variable-length arrays stored in the heap. |
| [`BitPix`](Eleris.FitsNet.BitPix.md) | enum | The BITPIX data representations defined by the standard. |
| [`ChecksumPolicy`](Eleris.FitsNet.ChecksumPolicy.md) | enum | What [`FitsFile.Save`](Eleris.FitsNet.FitsFile.md#eleris-fitsnet-fitsfile-save-system-io-stream-eleris-fitsnet-checksumpolicy) does with CHECKSUM and DATASUM. |
| [`ChecksumResult`](Eleris.FitsNet.ChecksumResult.md) | struct | Result of [`Hdu.VerifyChecksum`](Eleris.FitsNet.Hdu.md#eleris-fitsnet-hdu-verifychecksum). |
| [`ChecksumStatus`](Eleris.FitsNet.ChecksumStatus.md) | enum | Outcome of checking one of the CHECKSUM / DATASUM keywords. |
| [`CompressedImageHdu`](Eleris.FitsNet.CompressedImageHdu.md) | class | A tile-compressed image (standard section 10): a binary table with `ZIMAGE = T` whose rows hold the compressed tiles. |
| [`CompressedTableHdu`](Eleris.FitsNet.CompressedTableHdu.md) | class | A tile-compressed binary table (standard section 10.3): a binary table with `ZTABLE = T` whose rows hold, for each tile of rows of the original table, one compressed byte stream per column. |
| [`FitsCard`](Eleris.FitsNet.FitsCard.md) | class | One 80-character header card. |
| [`FitsCardKind`](Eleris.FitsNet.FitsCardKind.md) | enum | Kind of a header card. |
| [`FitsException`](Eleris.FitsNet.FitsException.md) | class | Thrown when data does not conform to the FITS standard. |
| [`FitsFile`](Eleris.FitsNet.FitsFile.md) | class | A FITS file: a primary HDU followed by zero or more extensions. |
| [`FitsHeader`](Eleris.FitsNet.FitsHeader.md) | class | An ordered collection of header cards (without the END card, which is implicit). |
| [`FitsMimeTypes`](Eleris.FitsNet.FitsMimeTypes.md) | class | The MIME types of Appendix G of the standard (RFC 4047). |
| [`FitsReadOptions`](Eleris.FitsNet.FitsReadOptions.md) | record | Options controlling how strictly files are parsed. |
| [`GenericHdu`](Eleris.FitsNet.GenericHdu.md) | class | An extension this version does not interpret (kept verbatim so it survives a round trip). |
| [`GreenBankConvention`](Eleris.FitsNet.GreenBankConvention.md) | class | The Green Bank convention (Appendix L): header keywords expanded into binary-table columns, and constant columns collapsed into keywords. |
| [`Hdu`](Eleris.FitsNet.Hdu.md) | class | A Header Data Unit: a header plus its data. |
| [`HeaderInheritance`](Eleris.FitsNet.HeaderInheritance.md) | class | The header inheritance convention (Appendix K): extensions that share the keywords of the primary HDU. |
| [`ImageHdu`](Eleris.FitsNet.ImageHdu.md) | class | An image: the primary array or an `XTENSION = 'IMAGE'` extension. |
| [`RandomGroupsHdu`](Eleris.FitsNet.RandomGroupsHdu.md) | class | The random groups structure (primary HDU with `GROUPS = T` and `NAXIS1 = 0`): a sequence of groups, each made of PCOUNT parameters followed by a data array. |
| [`StokesParameters`](Eleris.FitsNet.StokesParameters.md) | class | The conventional Stokes values of the `STOKES` axis (Table 29 of the standard). |
| [`TableColumn`](Eleris.FitsNet.TableColumn.md) | class | A table column: its header metadata (TTYPE, TFORM, TUNIT, TSCAL, TZERO, TNULL, TDISP, TDIM) and, optionally, its data. |
| [`TableHdu`](Eleris.FitsNet.TableHdu.md) | class | Common behaviour of ASCII (`TABLE`) and binary (`BINTABLE`) tables. |

## Namespace `Eleris.FitsNet.Compression`

| Type | Kind | Description |
| --- | --- | --- |
| [`CompressedTableOptions`](Eleris.FitsNet.Compression.CompressedTableOptions.md) | class | Parameters for creating a tile-compressed table ([`CompressedTableHdu.Compress`](Eleris.FitsNet.CompressedTableHdu.md#eleris-fitsnet-compressedtablehdu-compress-eleris-fitsnet-binarytablehdu-eleris-fitsnet-compression-compressedtableoptions)). |
| [`CompressionAlgorithm`](Eleris.FitsNet.Compression.CompressionAlgorithm.md) | enum | The compression algorithms of the tile compression convention (`ZCMPTYPE`). |
| [`CompressionOptions`](Eleris.FitsNet.Compression.CompressionOptions.md) | class | Parameters for creating a tile-compressed image. |
| [`QuantizationMethod`](Eleris.FitsNet.Compression.QuantizationMethod.md) | enum | How floating point pixels are mapped to integers before compression (`ZQUANTIZ`). |

## Namespace `Eleris.FitsNet.Wcs`

| Type | Kind | Description |
| --- | --- | --- |
| [`FitsTime`](Eleris.FitsNet.Wcs.FitsTime.md) | struct | An instant on a time scale (standard section 9): a day number and the seconds into the day, which keeps nanoseconds over any span of years. |
| [`TimeFrame`](Eleris.FitsNet.Wcs.TimeFrame.md) | class | The time coordinate frame of an HDU (standard section 9): time scale, unit, reference time and position, errors, the global time keywords, and the means to turn the time values of the data (an image axis, a table column) into [`FitsTime`](Eleris.FitsNet.Wcs.FitsTime.md) instants. |
| [`TimeScale`](Eleris.FitsNet.Wcs.TimeScale.md) | enum | The time scales of Table 30 of the standard (synonyms are folded: TDT and ET into TT, IAT into TAI, GMT into UTC). |
| [`TimeScales`](Eleris.FitsNet.Wcs.TimeScales.md) | class | Names, synonyms and conversions of time scales. |
| [`WorldAxisKind`](Eleris.FitsNet.Wcs.WorldAxisKind.md) | enum | The kind of a world coordinate axis. |
| [`WorldCoordinateSystem`](Eleris.FitsNet.Wcs.WorldCoordinateSystem.md) | class | The world coordinate system of an HDU (standard section 8): the link between pixel coordinates and world coordinates, built from the `CRPIXj`, `CRVALi`, `CDELTi`, `CTYPEi`, `CUNITi`, `PCi_j` / `CDi_j` / `CROTAi`, `PVi_m`, `LONPOLE`, `LATPOLE` keywords of one coordinate description (primary or alternate A to Z). |
| [`WorldCoordinateSystemExtensions`](Eleris.FitsNet.Wcs.WorldCoordinateSystemExtensions.md) | class | Adds the WCS to the HDU classes whose header can carry one. |
