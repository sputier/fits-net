using Eleris.FitsNet.Compression;

namespace Eleris.FitsNet.Tests;

/// <summary>The bit writer writes the same bytes as the first one (<see cref="ReferenceBitWriter"/>), whatever the sequence of writes.</summary>
public class BitWriterTests
{
    [Fact]
    public void Random_sequences_of_writes_give_the_bytes_of_the_first_writer()
    {
        var rng = new Random(31);
        for (int round = 0; round < 400; round++)
        {
            var writer = new BitWriter(rng.Next(0, 3) == 0 ? 16 : 1024);
            var reference = new ReferenceBitWriter(rng.Next(0, 3) == 0 ? 16 : 1024);
            int operations = rng.Next(1, 600);
            long bits = 0;   // to know whether the writers are on a byte boundary
            for (int k = 0; k < operations; k++)
            {
                switch (rng.Next(7))
                {
                    case 0:
                    case 1:
                    case 2:
                    {
                        int count = rng.Next(0, 33);
                        uint value = (uint)rng.NextInt64(0, 1L << 32);   // more bits than the count: only the low ones are written
                        writer.WriteBits(value, count);
                        reference.WriteBits(value, count);
                        bits += count;
                        break;
                    }
                    case 3:
                    {
                        int zeros = rng.Next(0, 70);
                        writer.WriteUnary(zeros);
                        reference.WriteUnary(zeros);
                        bits += zeros + 1;
                        break;
                    }
                    case 4:
                    {
                        bool one = rng.Next(2) == 1;
                        writer.WriteBit(one);
                        reference.WriteBit(one);
                        bits++;
                        break;
                    }
                    case 5:
                    {
                        writer.AlignToByte();
                        reference.AlignToByte();
                        bits += (8 - bits % 8) % 8;
                        break;
                    }
                    default:
                        if (bits % 8 != 0) break;
                        if (rng.Next(2) == 0)
                        {
                            byte b = (byte)rng.Next(256);
                            writer.WriteByte(b);
                            reference.WriteByte(b);
                            bits += 8;
                        }
                        else
                        {
                            byte[] bytes = new byte[rng.Next(0, 40)];
                            rng.NextBytes(bytes);
                            writer.WriteBytes(bytes);
                            reference.WriteBytes(bytes);
                            bits += bytes.Length * 8L;
                        }
                        break;
                }
            }
            Assert.Equal(reference.ToArray(), writer.ToArray());
        }
    }

    [Fact]
    public void A_byte_off_a_byte_boundary_is_refused()
    {
        var writer = new BitWriter();
        writer.WriteBits(1, 3);
        Assert.Throws<InvalidOperationException>(() => writer.WriteByte(1));
        Assert.Throws<InvalidOperationException>(() => writer.WriteBytes(new byte[2]));
        writer.AlignToByte();
        writer.WriteByte(0xAB);
        Assert.Equal(new byte[] { 0b0010_0000, 0xAB }, writer.ToArray());
    }
}
