using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace ThixLauncher.Core.Infrastructure;

/// <summary>
/// A plain PNG for GitHub in memory (<see cref="SampleGitHub"/>): a window of an app drawn in two colours – a title bar, a menu
/// at the left and a page – so the tests, the self test and the pictures without the real screenshots show something in the
/// size of one. Never used by the real app.
/// </summary>
internal static class SamplePicture
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>A picture of <paramref name="width"/> × <paramref name="height"/> in the tint of <paramref name="seed"/> (the same seed, the same picture).</summary>
    public static byte[] Png(string seed, int width = 320, int height = 200)
    {
        var hash = (uint)seed.Aggregate(17, (sum, letter) => unchecked(sum * 31 + letter));
        var (red, green, blue) = ((byte)(80 + hash % 140), (byte)(80 + (hash >> 8) % 140), (byte)(80 + (hash >> 16) % 140));
        var rows = new byte[height * (1 + width * 3)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (1 + width * 3);
            rows[row] = 0; // no filter
            for (var x = 0; x < width; x++)
            {
                var isTitleBar = y < height / 10;
                var isMenu = x < width / 5;
                var (r, g, b) = isTitleBar ? (red, green, blue)
                    : isMenu ? ((byte)(235 - red / 8), (byte)(235 - green / 8), (byte)(235 - blue / 8))
                    : ((byte)250, (byte)250, (byte)250);
                var at = row + 1 + x * 3;
                rows[at] = r;
                rows[at + 1] = g;
                rows[at + 2] = b;
            }
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bits per channel
        header[9] = 2; // RGB
        Chunk(png, "IHDR", header);
        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write(rows);
            }

            Chunk(png, "IDAT", compressed.ToArray());
        }

        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);
        var typeAndData = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        png.Write(typeAndData);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc(typeAndData));
        png.Write(number);
    }

    private static uint Crc(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
