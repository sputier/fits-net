namespace FitsNet.Compression;

/// <summary>
/// IRAF pixel list (<c>PLIO_1</c>): a run-length encoding of non-negative integer masks.
/// A tile is a short array: a 7 word header, then instructions whose top 4 bits are the opcode and low 12 bits the data.
/// The "high" value starts at 1; opcodes: 0 = ZN (n zeros), 1 = SH (set high from this word and the next), 2 = IH, 3 = DH
/// (high += / -= n), 4 = HN (n pixels of the high value), 5 = PN (n-1 zeros then one high pixel), 6 = IS, 7 = DS (change the
/// high value by n and emit one pixel).
/// </summary>
internal static class PlioCodec
{
    private const int HeaderWords = 7;
    private const int MaxRun = 4095;
    public const int MaxValue = (1 << 24) - 1;

    public static int[] Decode(ReadOnlySpan<short> w, int count)
    {
        var result = new int[count];
        if (count == 0) return result;
        if (w.Length < HeaderWords) throw new FitsException("The PLIO line list is too short.");

        int first, length;
        if (w[2] > 0)
        {
            length = w[2]; // older format
            first = 3;
        }
        else
        {
            length = ((ushort)w[4] << 15) | (ushort)w[3];
            first = w[1];
        }
        if (first < 0 || length > w.Length || first > length) throw new FitsException("The PLIO line list header is inconsistent.");

        int pv = 1, op = 0;
        for (int ip = first; ip < length && op < count; ip++)
        {
            int word = (ushort)w[ip];
            int opcode = word >> 12, data = word & 0xFFF;
            switch (opcode)
            {
                case 0: // ZN
                    op = (int)Math.Min((long)count, (long)op + data);
                    break;
                case 4: // HN
                {
                    int end = (int)Math.Min((long)count, (long)op + data);
                    for (; op < end; op++) result[op] = pv;
                    break;
                }
                case 5: // PN
                    if (data > 0)
                    {
                        op = (int)Math.Min((long)count, (long)op + data - 1);
                        if (op < count) result[op++] = pv;
                    }
                    break;
                case 1: // SH
                    if (ip + 1 >= length) throw new FitsException("A PLIO instruction is cut off.");
                    pv = ((ushort)w[ip + 1] << 12) + data;
                    ip++;
                    break;
                case 2:
                    pv += data;
                    break;
                case 3:
                    pv -= data;
                    break;
                case 6:
                    pv += data;
                    result[op++] = pv;
                    break;
                case 7:
                    pv -= data;
                    result[op++] = pv;
                    break;
                default:
                    throw new FitsException($"Unknown PLIO opcode {opcode}.");
            }
        }
        return result;
    }

    public static short[] Encode(ReadOnlySpan<int> pixels)
    {
        var words = new List<short>(HeaderWords + pixels.Length / 8);
        for (int i = 0; i < HeaderWords; i++) words.Add(0);

        void Emit(int opcode, int data) => words.Add(unchecked((short)((opcode << 12) | data)));

        int pv = 1;
        int i0 = 0;
        while (i0 < pixels.Length)
        {
            int v = Math.Max(0, pixels[i0]);
            if (v > MaxValue) throw new FitsException($"PLIO_1 supports pixel values up to {MaxValue}, found {v}.");
            int run = 1;
            while (i0 + run < pixels.Length && Math.Max(0, pixels[i0 + run]) == v) run++;

            if (v == 0)
            {
                for (int left = run; left > 0; left -= MaxRun) Emit(0, Math.Min(left, MaxRun));
            }
            else
            {
                int delta = v - pv;
                if (delta != 0 && run == 1 && Math.Abs(delta) <= MaxRun)
                {
                    Emit(delta > 0 ? 6 : 7, Math.Abs(delta)); // change the value and emit the pixel
                    pv = v;
                }
                else
                {
                    if (delta != 0)
                    {
                        if (Math.Abs(delta) <= MaxRun) Emit(delta > 0 ? 2 : 3, Math.Abs(delta));
                        else
                        {
                            Emit(1, v & 0xFFF);
                            words.Add((short)(v >> 12));
                        }
                        pv = v;
                    }
                    for (int left = run; left > 0; left -= MaxRun) Emit(4, Math.Min(left, MaxRun));
                }
            }
            i0 += run;
        }

        int length = words.Count;
        words[1] = HeaderWords;
        words[2] = -100;
        words[3] = (short)(length & 0x7FFF);
        words[4] = (short)(length >> 15);
        return words.ToArray();
    }
}
