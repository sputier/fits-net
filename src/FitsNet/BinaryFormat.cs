using System.Globalization;

namespace FitsNet;

/// <summary>A parsed binary table <c>TFORMn</c> value such as <c>1J</c>, <c>20A</c>, <c>8A4</c>, <c>16X</c> or <c>1PJ(255)</c>.</summary>
internal readonly record struct BinaryFormat(long Repeat, char Code, int CharWidth, char ElementCode, long MaxElements)
{
    private const string TypeCodes = "LXBIJKAEDCMPQ";
    private const string ElementCodes = "LXBIJKAEDCM";

    public bool IsVariable => Code is 'P' or 'Q';

    /// <summary>The code of the stored elements: the element type of a variable-length column, else the type itself.</summary>
    public char DataCode => IsVariable ? ElementCode : Code;

    public static int ElementSize(char code) => code switch
    {
        'L' or 'B' or 'A' => 1,
        'I' => 2,
        'J' or 'E' => 4,
        'K' or 'D' or 'C' => 8,
        'M' => 16,
        _ => throw new FitsException($"Unknown element type '{code}'."),
    };

    /// <summary>Number of bytes <paramref name="count"/> elements occupy (bits are packed for X).</summary>
    public static long ByteLength(char code, long count)
    {
        if (count < 0) throw new FitsException("A negative element count is invalid.");
        try
        {
            return code == 'X' ? count / 8 + (count % 8 == 0 ? 0 : 1) : checked(count * ElementSize(code));
        }
        catch (OverflowException)
        {
            throw new FitsException("The announced element count overflows.");
        }
    }

    /// <summary>Number of bytes the field occupies in each row of the main table.</summary>
    public long FieldBytes
    {
        get
        {
            try
            {
                return Code switch
                {
                    'X' => ByteLength('X', Repeat),
                    'P' => checked(Repeat * 8),
                    'Q' => checked(Repeat * 16),
                    _ => checked(Repeat * ElementSize(Code)),
                };
            }
            catch (OverflowException)
            {
                throw new FitsException("The repeat count of the TFORM overflows.");
            }
        }
    }

    public static BinaryFormat Parse(string tform)
    {
        string s = tform.Trim();
        int i = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
        long repeat = 1;
        if (i > 0 && !long.TryParse(s.AsSpan(0, i), NumberStyles.None, CultureInfo.InvariantCulture, out repeat))
            throw new FitsException($"Invalid repeat count in TFORM '{tform}'.");
        if (i >= s.Length || TypeCodes.IndexOf(char.ToUpperInvariant(s[i])) < 0)
            throw new FitsException($"Invalid binary table TFORM '{tform}'.");
        char code = char.ToUpperInvariant(s[i++]);

        if (code is 'P' or 'Q')
        {
            if (repeat > 1) throw new FitsException($"Variable-length TFORM '{tform}' must have a repeat count of 0 or 1.");
            if (i >= s.Length || ElementCodes.IndexOf(char.ToUpperInvariant(s[i])) < 0)
                throw new FitsException($"Variable-length TFORM '{tform}' needs an element type.");
            char element = char.ToUpperInvariant(s[i++]);
            long max = 0;
            if (i < s.Length && s[i] == '(')
            {
                int close = s.IndexOf(')', i);
                if (close < 0 || !long.TryParse(s.AsSpan(i + 1, close - i - 1), NumberStyles.None, CultureInfo.InvariantCulture, out max))
                    throw new FitsException($"Invalid maximum length in TFORM '{tform}'.");
            }
            return new BinaryFormat(repeat, code, 0, element, max);
        }

        int width = 0;
        if (code == 'A')
        {
            int start = i;
            while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
            if (i > start && !int.TryParse(s.AsSpan(start, i - start), NumberStyles.None, CultureInfo.InvariantCulture, out width))
                throw new FitsException($"Invalid string width in TFORM '{tform}'.");
        }
        return new BinaryFormat(repeat, code, width, code, 0);
    }
}
