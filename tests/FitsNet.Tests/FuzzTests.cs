using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using FitsNet.Compression;

namespace FitsNet.Tests;

/// <summary>
/// Mutation fuzzing: damaged files must be rejected with <see cref="FitsException"/> (or an argument exception for caller errors),
/// never with an out-of-range index, a cast error, an overflow, an out-of-memory error or a hang. Deterministic (fixed seed) so that a
/// failure can be reproduced from its message. FITSNET_FUZZ_ROUNDS raises the number of mutations per file for a deeper, slower run.
/// </summary>
public class FuzzTests
{
    private static byte[] Save(params Hdu[] hdus)
    {
        var file = new FitsFile();
        foreach (var h in hdus) file.Add(h);
        var ms = new MemoryStream();
        file.Save(ms, ChecksumPolicy.Always);
        return ms.ToArray();
    }

    private static IEnumerable<(string Name, byte[] Bytes)> Corpus()
    {
        short[] image = Enumerable.Range(0, 24 * 16).Select(i => (short)((i * 37) % 900 - 300 + i % 7)).ToArray();
        float[] floats = Enumerable.Range(0, 24 * 16).Select(i => (float)(200 + Math.Sin(i * 0.37) * 9 + i % 5 * 0.1)).ToArray();
        int[] mask = Enumerable.Range(0, 24 * 16).Select(i => i % 24 < 8 ? 0 : (i / 24) % 3 == 0 ? 7 : 300).ToArray();

        yield return ("image", Save(ImageHdu.Create<short>(image, 24, 16)));
        yield return ("image+ext", Save(ImageHdu.Create<ushort>([1, 2, 3, 65535], 2, 2), ImageHdu.Create<double>([1.5, 2.5], 2)));
        yield return ("groups", Save(RandomGroupsHdu.Create<float>([1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f], 2, 2, 2)));

        var table = BinaryTableHdu.Create(
        [
            TableColumn.Create<int>("ID", [1, 2, 3]),
            TableColumn.Create<double>("X", [1.5, 2.5, 3.5]),
            TableColumn.CreateString("NAME", ["a", "bb", "ccc"], 4),
            TableColumn.CreateLogical("OK", [true, null, false]),
            TableColumn.CreateBits("BITS", [.. Enumerable.Range(0, 24).Select(i => i % 3 == 0)], 8),
            TableColumn.CreateComplex("Z", [new System.Numerics.Complex(1, 2), 0, 3]),
            TableColumn.CreateVariable<short>("V", [[1, 2], [], [3]]),
            TableColumn.CreateVariable<double>("Q", [[1.5], [2.5, 3.5], []], use64BitDescriptors: true),
            TableColumn.CreateVariableString("S", ["x", "", "yz"]),
        ]);
        table.Header.Set("EXTNAME", "CATALOG").Set("LONGTXT", new string('t', 150));
        yield return ("bintable", Save(table));
        yield return ("ascii", Save(AsciiTableHdu.Create(
        [
            TableColumn.CreateAsciiString("S", ["alpha", "beta"]),
            TableColumn.CreateAsciiInteger("I", [1, null]),
            TableColumn.CreateAsciiFloat("F", [1.5, -2.25], "F8.3"),
            TableColumn.CreateAsciiFloat("E", [1e10, 0], "E12.4"),
        ])));
        yield return ("ztable", Save(CompressedTableHdu.Compress(table, new CompressedTableOptions { TileRows = 2, MaxDegreeOfParallelism = 1 })));
        yield return ("ztable-gzip", Save(CompressedTableHdu.Compress(table, new CompressedTableOptions { TileRows = 1, Algorithm = CompressionAlgorithm.Gzip2 })));
        yield return ("two-tables", Save(table, AsciiTableHdu.Create([TableColumn.CreateAsciiInteger("N", [1, 2, 3])])));

        foreach (var algo in new[] { CompressionAlgorithm.Rice, CompressionAlgorithm.Gzip1, CompressionAlgorithm.Gzip2, CompressionAlgorithm.HCompress, CompressionAlgorithm.NoCompress })
            yield return ("comp-" + algo, Save(CompressedImageHdu.Create<short>(image, [24, 16], new CompressionOptions { Algorithm = algo, TileShape = [24, 4], MaxDegreeOfParallelism = 1 })));
        yield return ("comp-plio", Save(CompressedImageHdu.Create<int>(mask, [24, 16], new CompressionOptions { Algorithm = CompressionAlgorithm.Plio, TileShape = [24, 4] })));
        yield return ("comp-float", Save(CompressedImageHdu.Create<float>(floats, [24, 16], new CompressionOptions { TileShape = [24, 4], MaxDegreeOfParallelism = 1 })));
        yield return ("comp-float-hcomp", Save(CompressedImageHdu.Create<float>(floats, [24, 16],
            new CompressionOptions { Algorithm = CompressionAlgorithm.HCompress, TileShape = [24, 8], MaxDegreeOfParallelism = 1 })));
    }

    /// <summary>Every operation a caller can do with a file, on whatever the mutated bytes turned into.</summary>
    private static void Exercise(FitsFile file)
    {
        foreach (var hdu in file)
        {
            _ = hdu.VerifyChecksum();
            switch (hdu)
            {
                case CompressedImageHdu c:
                    _ = c.ReadPixels<double>();
                    _ = c.ReadColumns();
                    break;
                case CompressedTableHdu z:
                    _ = z.ReadColumns();
                    foreach (var column in z.Decompress().ReadColumns()) _ = column.Data;
                    break;
                case ImageHdu i:
                    _ = i.ReadPixels<double>();
                    if (i.Axes.Count > 0 && i.PixelCount > 0) _ = i.Subimage(new long[i.Axes.Count], i.Axes.Select(a => Math.Min(a, 3L)).ToArray());
                    break;
                case RandomGroupsHdu g:
                    if (g.GroupCount > 0)
                    {
                        _ = g.ReadParameters(0);
                        _ = g.ReadData(0);
                        _ = g.ReadNamedParameters(0);
                    }
                    break;
                case TableHdu t:
                    _ = t.ReadColumns();
                    for (int c = 0; c < Math.Min(t.ColumnCount, 12); c++) _ = t.ReadRaw(c);
                    _ = t.IndexOf("ID");
                    if (t.RowCount > 0) t.RemoveRows(0, 1);
                    break;
            }
        }
        file.Save(new MemoryStream(), ChecksumPolicy.Always);
    }

    private static void ExerciseBytes(byte[] bytes, bool strict)
    {
        using var file = FitsFile.Read(new MemoryStream(bytes), new FitsReadOptions(strict));
        Exercise(file);
    }

    /// <summary>The lazy path: the file is opened from disk, exercised, then overwritten by its own save.</summary>
    private static void ExerciseLazy(byte[] bytes, string path)
    {
        File.WriteAllBytes(path, bytes);
        using (var file = FitsFile.Open(path))
        {
            Exercise(file);
            file.Save(path);
        }
        using var again = FitsFile.Open(path);
        _ = again.Count;
    }

    /// <summary>Runs <paramref name="action"/> with a watchdog: a hang becomes a test failure instead of stalling the run.</summary>
    private static void WithWatchdog(Action action)
    {
        var task = Task.Run(action);
        if (!task.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("the operation did not finish within 30 s");
        // Task.Wait wraps the original exception; hand it back untouched
    }

    private static bool Acceptable(Exception e) => e is FitsException or ArgumentException or NotSupportedException;

    /// <summary>Header blocks are the blocks made only of printable ASCII: that is where the structure of every HDU lives.</summary>
    private static List<int> HeaderBlocks(byte[] bytes)
    {
        var blocks = new List<int>();
        for (int b = 0; b + 2880 <= bytes.Length; b += 2880)
        {
            bool text = true;
            for (int k = 0; k < 2880 && text; k++) text = bytes[b + k] is >= 0x20 and <= 0x7E;
            if (text) blocks.Add(b);
        }
        return blocks;
    }

    private static readonly string[] Extremes =
    [
        "-1", "0", "1", "2", "255", "65536", "2147483647", "-2147483648", "4294967296", "9223372036854775807", "-9223372036854775808",
        "99999999999999999999", "1.0E308", "1.0E-308", "NaN", "T", "F", "'X'", "'1J'", "'1PJ(5)'", "'RICE_1'", "'GZIP_2'", "'0B'", "'10A'",
    ];

    [Fact]
    public void Mutated_files_never_escape_with_anything_but_a_clear_exception()
    {
        var rng = new Random(20261030);
        var failures = new List<string>();
        var clock = Stopwatch.StartNew();
        int attempts = 0, rejected = 0;
        int rounds = int.TryParse(Environment.GetEnvironmentVariable("FITSNET_FUZZ_ROUNDS"), out int r) ? r : 700;
        string scratch = Path.Combine(Path.GetTempPath(), "fitsnet-fuzz-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);

        try
        {
            foreach (var (name, original) in Corpus())
            {
                var headerBlocks = HeaderBlocks(original);
                for (int n = 0; n < rounds; n++)
                {
                    byte[] bytes = (byte[])original.Clone();
                    int headerAt() => headerBlocks.Count > 0 ? headerBlocks[rng.Next(headerBlocks.Count)] : 0;
                    switch (rng.Next(6))
                    {
                        case 0: // a few random bytes anywhere
                            for (int k = rng.Next(1, 6); k > 0; k--) bytes[rng.Next(bytes.Length)] = (byte)rng.Next(256);
                            break;
                        case 1: // bytes in any header block, extensions included
                            for (int k = rng.Next(1, 4); k > 0; k--) bytes[headerAt() + rng.Next(2880)] = (byte)rng.Next(256);
                            break;
                        case 2: // truncation
                            Array.Resize(ref bytes, rng.Next(bytes.Length));
                            break;
                        case 3: // digits and signs inside the header cards: sizes, counts and formats
                            for (int k = rng.Next(1, 3); k > 0; k--)
                            {
                                int at = headerAt() + rng.Next(2880);
                                if (bytes[at] is >= (byte)'0' and <= (byte)'9' or (byte)'-') bytes[at] = (byte)"0123456789-+.E"[rng.Next(14)];
                            }
                            break;
                        case 4: // a whole value replaced by an extreme one (huge or negative counts, other formats, other algorithms)
                            for (int k = rng.Next(1, 3); k > 0; k--)
                            {
                                int card = headerAt() + rng.Next(36) * 80;
                                if (bytes[card + 8] != (byte)'=') continue;
                                string value = Extremes[rng.Next(Extremes.Length)];
                                string field = value.Length <= 20 ? value.PadLeft(20) : value;
                                var text = Encoding.ASCII.GetBytes(field.PadRight(70)[..70]);
                                Array.Copy(text, 0, bytes, card + 10, 70);
                            }
                            break;
                        default: // a block of a later HDU zeroed or filled
                            int b = rng.Next(bytes.Length / 2880) * 2880;
                            Array.Fill(bytes, (byte)(rng.Next(2) == 0 ? 0 : 0xFF), b, Math.Min(2880, bytes.Length - b));
                            break;
                    }

                    foreach (bool strict in new[] { false, true })
                    {
                        attempts++;
                        try
                        {
                            WithWatchdog(() => ExerciseBytes(bytes, strict));
                        }
                        catch (Exception e) when (e is AggregateException ae ? Acceptable(ae.GetBaseException()) : Acceptable(e))
                        {
                            rejected++;
                        }
                        catch (Exception e)
                        {
                            var real = e is AggregateException agg ? agg.GetBaseException() : e;
                            failures.Add($"{name} #{n} strict={strict}: {real.GetType().Name}: {real.Message} @ {real.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
                        }
                    }

                    if (n % 25 == 0) // the lazy path touches the disk: a sample is enough
                    {
                        attempts++;
                        string path = Path.Combine(scratch, "f.fits");
                        try
                        {
                            WithWatchdog(() => ExerciseLazy(bytes, path));
                        }
                        catch (Exception e) when (e is AggregateException ae ? Acceptable(ae.GetBaseException()) : Acceptable(e))
                        {
                            rejected++;
                        }
                        catch (Exception e)
                        {
                            var real = e is AggregateException agg ? agg.GetBaseException() : e;
                            failures.Add($"{name} #{n} lazy: {real.GetType().Name}: {real.Message} @ {real.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
                        }
                        if (File.Exists(path)) File.Delete(path);
                    }
                }
            }

            Assert.True(failures.Count == 0, $"{failures.Count} unexpected failures of {attempts} runs:\n" + string.Join("\n", failures.Take(15)));
            Assert.True(rejected > 100, "the mutations should break a good share of the files");
            Assert.Empty(Directory.GetFiles(scratch)); // no temporary file is ever left behind
            Assert.True(clock.Elapsed < TimeSpan.FromMinutes(rounds > 700 ? 40 : 3), $"took {clock.Elapsed}");
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }
}
