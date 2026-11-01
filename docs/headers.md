# Headers

A [`FitsHeader`](api/Eleris.FitsNet.FitsHeader.md) is an ordered list of [`FitsCard`](api/Eleris.FitsNet.FitsCard.md). Every HDU has one, in its `Header` property. You can also create one alone.

## Read and write values

`Set` replaces the value of a keyword, or appends a card if the keyword is not there. It returns the header, so calls can be chained. The type of the value decides how it is written: string, integer, floating point, logical, complex, or no value.

<!-- example: Headers.Values -->
```csharp
var header = new FitsHeader();

// Set replaces the value of a keyword or appends a new card; the calls can be chained.
header
    .Set("OBJECT", "M42", "name of the target")   // string
    .Set("NAXIS3", 3)                            // integer
    .Set("EXPTIME", 30.5, "[s] exposure")        // floating point
    .Set("FLATCORR", true)                       // logical
    .Set("GAIN", new Complex(1.5, -0.5))         // complex
    .Set("NODATA", null);                     // a card with no value

// Get converts the value and returns the default of the type when the keyword is missing or cannot be converted.
string? target = header.Get<string>("OBJECT");
double exposure = header.Get<double>("EXPTIME");    // 30.5
int axes = header.Get<int>("NAXIS3");               // 3
int missing = header.Get<int>("NOPE");              // 0

// TryGetValue tells a missing keyword from a zero.
if (header.TryGetValue("EXPTIME", out double seconds))
    Console.WriteLine($"{target}: {seconds} s, {axes} axes, {missing}");

// Find returns the card itself, with its comment.
FitsCard? card = header.Find("EXPTIME");
Console.WriteLine($"{card!.Keyword} = {card.Value} / {card.Comment}");

Console.WriteLine(header.Contains("OBJECT"));       // True
header.Remove("NODATA");
```
<!-- /example -->

Three ways to read, depending on what you want to know:

| Method | Missing keyword | Value of another type |
| --- | --- | --- |
| `Get<T>(keyword)` | The default of `T` | Converted when possible, the default of `T` otherwise |
| `TryGetValue(keyword, out T)` | Returns `false` | Returns `false` |
| `Find(keyword)` | `null` | The card, whatever its value |

Keywords are upper-case ASCII letters, digits, `-` and `_`, 8 characters at most. Anything else throws an `ArgumentException`, for the longer names see [`HIERARCH`](#hierarch-keywords) below.

## Comments

The comment is the text after the `/` of a card.

<!-- example: Headers.Comments -->
```csharp
var header = new FitsHeader();
header.Set("EXPTIME", 30.0, "[s] first comment");

// A comment given to Set replaces the old one; no comment keeps the old one.
header.Set("EXPTIME", 60.0);                         // the comment is still "[s] first comment"
header.Set("EXPTIME", 60.0, "[s] exposure time");    // replaced
header.SetComment("EXPTIME", null);                  // removed
```
<!-- /example -->

## COMMENT and HISTORY

Commentary cards have no value; they carry free text.

<!-- example: Headers.Commentary -->
```csharp
var header = new FitsHeader();

// COMMENT and HISTORY cards have no value. A long text is written over several consecutive cards.
header.AddComment("Calibrated with the master flat of 2026-01-10.");
header.AddHistory("Dark subtracted");
header.AddHistory("Flat fielded");

foreach (FitsCard card in header.Where(c => c.Kind == FitsCardKind.Commentary))
    Console.WriteLine($"{card.Keyword}: {card.Value}");
```
<!-- /example -->

A text longer than one card is split over several consecutive cards when the header is written.

## Cards and their order

Cards are kept in the order you give them. `header[i]` reads a card by position, `Insert` places a card anywhere, and the header can be enumerated, so LINQ works on it.

<!-- example: Headers.Cards -->
```csharp
var header = new FitsHeader()
    .Set("SIMPLE", true)
    .Set("BITPIX", 16)
    .Set("NAXIS", 0);

// Cards are kept in order and can be inserted anywhere.
header.Insert(1, new FitsCard("TELESCOP", "Vaonis Vespera Pro 2", "instrument"));

for (int i = 0; i < header.Count; i++)
    Console.WriteLine($"{i}: {header[i]}");

// Header is enumerable, so LINQ works on the cards.
string[] keywords = header.Select(c => c.Keyword).ToArray();
Console.WriteLine(string.Join(" ", keywords));
```
<!-- /example -->

In the header of an HDU, the mandatory structural keywords (`SIMPLE`, `XTENSION`, `BITPIX`, `NAXIS`, `NAXISn`, `PCOUNT`, `GCOUNT`, and for tables the `TFORMn` family) are rewritten from the data when the file is saved, and the other cards are kept in order. Change the data through the typed members (`SetPixels`, the table methods), not through these cards.

## Remove cards

<!-- example: Headers.Remove -->
```csharp
var header = new FitsHeader().Set("A", 1).Set("B", 2).AddComment("note");

int removed = header.Remove("A");   // 1: the number of cards removed
header.RemoveAt(0);                 // removes the card that is now first ("B")
header.Clear();                     // removes everything
Console.WriteLine($"{removed} {header.Count}");
```
<!-- /example -->

## HIERARCH keywords

The standard limits keywords to 8 characters. The `HIERARCH` convention, used by several observatories, writes longer names as `HIERARCH ESO DET CHIP1 ID = ...`. It is not part of the standard, so it is never chosen silently: `Set` refuses a long keyword, and `SetHierarch` writes one on purpose.

<!-- example: Headers.Hierarch -->
```csharp
var header = new FitsHeader();

// Standard keywords have at most 8 characters. The HIERARCH convention (widely used, but not part of the standard)
// allows longer ones; it is never chosen silently.
header.SetHierarch("ESO DET CHIP1 ID", "CCD-42", "detector identifier");

Console.WriteLine(header.Find("ESO DET CHIP1 ID")!.IsHierarch);   // True
```
<!-- /example -->

Files that contain `HIERARCH` cards are read as they are, and `IsHierarch` tells them apart.

## Long strings

A string value is limited to what fits in a card (68 characters). Longer strings use the long string convention: the text is continued on `CONTINUE` cards. The library writes and reads this by itself.

<!-- example: Headers.LongStrings -->
```csharp
var header = new FitsHeader();

// A string longer than a card is written with CONTINUE cards (the long string convention) and read back whole.
string notes = string.Join(" ", Enumerable.Repeat("a long observing note", 12));
header.Set("NOTES", notes);

byte[] bytes = header.ToBytes();
FitsHeader back = FitsHeader.Read(new MemoryStream(bytes));
Console.WriteLine(back.Get<string>("NOTES") == notes);            // True
```
<!-- /example -->

## Serialization

`ToBytes` and `Write` produce the bytes of the header: a whole number of 2880-byte blocks, ending with the `END` card. `FitsHeader.Read` parses a header from a stream. Reading uses [`FitsReadOptions`](api/Eleris.FitsNet.FitsReadOptions.md), see [Files and HDUs](files.md#reading-options).

<!-- example: Headers.Serialization -->
```csharp
var header = new FitsHeader().Set("SIMPLE", true).Set("BITPIX", 8).Set("NAXIS", 0);

// A header is written as a whole number of 2880-byte blocks that ends with the END card.
byte[] bytes = header.ToBytes();
Console.WriteLine($"{bytes.Length} bytes = {bytes.Length / FitsHeader.BlockSize} block");

// Write puts the same bytes on a stream; Read parses a header from a stream.
using var stream = new MemoryStream();
header.Write(stream);
stream.Position = 0;
FitsHeader parsed = FitsHeader.Read(stream);
Console.WriteLine(parsed.Count);
```
<!-- /example -->
