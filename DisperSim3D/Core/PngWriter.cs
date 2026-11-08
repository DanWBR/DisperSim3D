#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Encodes raw RGBA pixels as a PNG without any imaging library — used where a
    /// viewport hands back a framebuffer read (OpenGL) rather than a bitmap object.
    /// </summary>
    public static class PngWriter
    {
        private static readonly uint[] CrcTable = BuildCrcTable();

        /// <summary>
        /// PNG of <paramref name="width"/> × <paramref name="height"/> RGBA8 pixels.
        /// OpenGL reads rows bottom-up, hence <paramref name="flipVertically"/>.
        /// </summary>
        public static byte[] EncodeRgba(byte[] rgba, int width, int height, bool flipVertically = false)
        {
            if (rgba == null) throw new ArgumentNullException(nameof(rgba));
            if (width <= 0 || height <= 0 || rgba.Length < width * height * 4)
                throw new ArgumentException("Pixel buffer does not match the size.");

            int stride = width * 4;
            byte[] scanlines = new byte[(stride + 1) * height];
            for (int y = 0; y < height; y++)
            {
                int src = (flipVertically ? height - 1 - y : y) * stride;
                int dst = y * (stride + 1);
                scanlines[dst] = 0; // filter: none
                Buffer.BlockCopy(rgba, src, scanlines, dst + 1, stride);
            }

            byte[] idat;
            using (var ms = new MemoryStream())
            {
                using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true))
                    z.Write(scanlines, 0, scanlines.Length);
                idat = ms.ToArray();
            }

            using var png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

            byte[] ihdr = new byte[13];
            WriteBigEndian(ihdr, 0, (uint)width);
            WriteBigEndian(ihdr, 4, (uint)height);
            ihdr[8] = 8;   // bit depth
            ihdr[9] = 6;   // colour type: RGBA
            ihdr[10] = 0;  // compression
            ihdr[11] = 0;  // filter
            ihdr[12] = 0;  // interlace
            WriteChunk(png, "IHDR", ihdr);
            WriteChunk(png, "IDAT", idat);
            WriteChunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        /// <summary>True when <paramref name="data"/> starts with the PNG signature.</summary>
        public static bool IsPng(byte[]? data) =>
            data != null && data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            byte[] len = new byte[4];
            WriteBigEndian(len, 0, (uint)data.Length);
            s.Write(len, 0, 4);

            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            s.Write(typeBytes, 0, 4);
            s.Write(data, 0, data.Length);

            uint crc = Crc(Crc(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
            byte[] crcBytes = new byte[4];
            WriteBigEndian(crcBytes, 0, crc);
            s.Write(crcBytes, 0, 4);
        }

        private static uint Crc(uint crc, byte[] data)
        {
            foreach (byte b in data)
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        private static void WriteBigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }
    }
}
