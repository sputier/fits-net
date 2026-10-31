# Conventions

The appendices of the standard describe conventions that many files use. FitsNet supports the ones that act on data or headers; the others need nothing from a library.

## Header inheritance

An extension can carry `INHERIT = T` to say that it shares the keywords of the primary header (the telescope, the observer, the dates...). `GetEffectiveHeader` on a [`FitsFile`](api/FitsNet.FitsFile.md) returns the header as an application should see it: the cards of the extension, then the cards of the primary header that it does not define itself. The mandatory keywords and the commentary cards of the primary header are not inherited.

The HDU is not modified. The result is a separate header, so saving the file does not copy the shared keywords into the extension.

<!-- example: Conventions.Inherit -->
```csharp
// The primary header holds keywords shared by the extensions; an extension with INHERIT = T inherits them.
var primary = ImageHdu.Create<byte>(new byte[1], 1);
primary.Header.Set("TELESCOP", "Vespera Pro 2").Set("OBSERVER", "A. Astronomer");

var table = BinaryTableHdu.Create([TableColumn.Create<int>("ID", [1, 2, 3])]);
table.Header.Set("INHERIT", true).Set("OBSERVER", "B. Observer");   // its own value wins

using var stream = new MemoryStream();
new FitsFile().Add(primary).Add(table).Save(stream);
stream.Position = 0;
using var file = FitsFile.Read(stream);

// The HDU itself is not modified: the merged view is a separate header.
FitsHeader merged = file.GetEffectiveHeader(1);
Console.WriteLine(merged.Get<string>("TELESCOP"));     // Vespera Pro 2
Console.WriteLine(merged.Get<string>("OBSERVER"));     // B. Observer
Console.WriteLine(file[1].Header.Contains("TELESCOP")); // False
```
<!-- /example -->

## Green Bank convention

In a binary table, a value that is the same in every row can be stored once, as a keyword, instead of in a column. The Green Bank convention lets software treat both forms the same way. The extension methods of `GreenBankConvention` apply to a [`BinaryTableHdu`](api/FitsNet.BinaryTableHdu.md):

| Method | Does |
| --- | --- |
| `GetValue(name, row)` | The value of a column for a row or, if no column has that name, the keyword. `null` if there is neither. |
| `GetValues(name)` | The same for every row, reading the column once. Use it rather than `GetValue` in a loop. |
| `CollapseConstantColumns()` | Turns the columns that hold one value in all rows into keywords, and returns their names. The last column is kept, and columns that are scaled or have `TNULL` are left alone. |
| `ExpandKeyword(name)` | Turns a keyword into a column, so that the rows can differ. |
| `IsExpandable(keyword)` | Whether a keyword can be expanded: the keywords that describe the structure of the table cannot. |

<!-- example: Conventions.GreenBank -->
```csharp
// The Green Bank convention moves values between header keywords and table columns.
var table = BinaryTableHdu.Create(
[
    TableColumn.Create<double>("FLUX", [1.0, 2.0, 3.0]),
    TableColumn.Create<double>("TSYS", [50.5, 50.5, 50.5]),         // the same in every row
    TableColumn.CreateString("OBJECT", ["M31", "M31", "M31"], 8),   // the same in every row
]);

// A column that holds one value in all rows can become a keyword...
IReadOnlyList<string> collapsed = table.CollapseConstantColumns();
Console.WriteLine(string.Join(", ", collapsed));                    // TSYS, OBJECT
Console.WriteLine(table.Header.Get<double>("TSYS"));                // 50.5

// ...GetValue finds a value in the column or, failing that, in the keyword, as the convention asks...
Console.WriteLine(table.GetValue("TSYS", 2));                       // 50.5

// ...and a keyword can become a column again, so that rows can differ.
table.ExpandKeyword("TSYS");
Console.WriteLine(table.ColumnCount);                               // 2
```
<!-- /example -->

## Stokes parameters

The coordinate values of a `STOKES` axis are conventional numbers: 1 is `I`, 2 is `Q`, -1 is `RR`... [`StokesParameters`](api/FitsNet.StokesParameters.md) converts between the numbers and the symbols (`I`, `Q`, `U`, `V`, `RR`, `LL`, `RL`, `LR`, `XX`, `YY`, `XY`, `YX`) and gives the meaning of each.

<!-- example: Conventions.Stokes -->
```csharp
// A STOKES axis holds polarizations; their coordinate values are conventional (section 8.5 of the standard).
Console.WriteLine(StokesParameters.Symbol(1));                      // I
Console.WriteLine(StokesParameters.Symbol(-1));                     // RR
Console.WriteLine(StokesParameters.Code("Q"));                      // 2
Console.WriteLine(StokesParameters.Description("V"));               // Standard Stokes circular
```
<!-- /example -->

`Symbol` returns `null` for a number that is not a conventional value, and `Code` returns `null` for an unknown symbol.

## Media types

[`FitsMimeTypes`](api/FitsNet.FitsMimeTypes.md) holds the media types registered for FITS (RFC 4047) and the usual file extensions, for a web server or an upload form: `application/fits` for any FITS file, `image/fits` for a file whose purpose is the image of the primary HDU.

<!-- example: Conventions.Mime -->
```csharp
// The media types registered for FITS (RFC 4047), for a web server or an upload.
Console.WriteLine(FitsMimeTypes.Application);                        // application/fits
Console.WriteLine(FitsMimeTypes.Image);                              // image/fits
Console.WriteLine(string.Join(" ", FitsMimeTypes.FileExtensions));   // .fits .fit .fts
```
<!-- /example -->

## Other conventions that are handled

| Convention | What the library does |
| --- | --- |
| `HIERARCH` keywords | Read and written, see [Headers](headers.md#hierarch-keywords) |
| Long strings (`CONTINUE`) | Read and written, see [Headers](headers.md#long-strings) |
| Unsigned integers in images and tables (`BZERO`, `TZERO`) | Applied on read and write, see [Images](images.md#unsigned-integers) |
| `CHECKSUM` and `DATASUM` | See [Checksums](checksums.md) |
| Tile compression | See [Compression](compression.md) |
| `IUEIMAGE` and `A3DTABLE` (prototype extensions of Appendix F) | Read as an image and a binary table, and written back with their own name |
