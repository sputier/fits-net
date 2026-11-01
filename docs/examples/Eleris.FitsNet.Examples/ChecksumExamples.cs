namespace Eleris.FitsNet.Examples;

public static class ChecksumExamples
{
    public static void WriteAndVerify()
    {
        #region Example:Checksums.WriteVerify
        using var source = FitsFile.Open("m42.fits");

        // Always writes CHECKSUM and DATASUM in every HDU.
        source.Save("m42-sums.fits", ChecksumPolicy.Always);

        using var file = FitsFile.Open("m42-sums.fits");
        foreach (ChecksumResult result in file.VerifyChecksums())
            Console.WriteLine($"CHECKSUM {result.Checksum}, DATASUM {result.DataSum}");

        // One HDU at a time:
        ChecksumResult primary = file.Primary.VerifyChecksum();
        Console.WriteLine(primary.Checksum == ChecksumStatus.Valid);     // True
        #endregion
    }

    public static void Policies()
    {
        #region Example:Checksums.Policies
        using var file = FitsFile.Open("m42-sums.fits");

        // Auto (the default) keeps the checksums of the HDUs that have them up to date and adds none.
        file.Save("auto.fits");

        // Never removes them: the right choice for a file that other programs edit afterwards.
        file.Save("never.fits", ChecksumPolicy.Never);
        using var stripped = FitsFile.Open("never.fits");
        Console.WriteLine(stripped.Primary.Header.Contains("CHECKSUM"));    // False
        #endregion
    }

    public static void Detect()
    {
        #region Example:Checksums.Detect
        byte[] bytes = File.ReadAllBytes("m42-sums.fits");
        bytes[bytes.Length / 2] ^= 0xFF;                      // damage one byte of the data

        using var file = FitsFile.Read(new MemoryStream(bytes));
        ChecksumResult result = file.Primary.VerifyChecksum();
        Console.WriteLine($"{result.Checksum} {result.DataSum}");     // Invalid Invalid

        // A file read and saved without changes is written back byte for byte, damaged or not:
        // the library never hides a corruption by recomputing the sums.
        #endregion
    }
}
