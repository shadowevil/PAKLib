using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PAKLib
{
    public enum SpriteImageFormat
    {
        Unknown,
        Bmp,
        Png
    }

    // Decodes and encodes the sprite image formats without external dependencies.
    // BMP: 1, 4, 8, 16, 24 and 32 bpp, uncompressed, bitfields, RLE8 and RLE4; always written as 24bpp.
    // PNG: every color type, bit depth and interlacing; always written as 8-bit RGBA, non-interlaced.
    public static class SpriteImage
    {
        private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        // Used for transparent pixels when a PNG becomes a BMP and the sprite has no color key
        public const uint DefaultColorKey = 0xFFFF00FFu;

        // Largest image width or height accepted (4K); also keeps a damaged header from exhausting memory
        public const int MaxDimension = 4096;

        // Width and height from a BMP or PNG header, or false when the header is missing or too short
        public static bool TryGetSize(byte[] data, out int width, out int height)
        {
            width = height = 0;
            switch (GetFormat(data))
            {
                case SpriteImageFormat.Png when data.Length >= 24:
                    width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16));
                    height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20));
                    return true;
                case SpriteImageFormat.Bmp when data.Length >= 26:
                    if (BitConverter.ToInt32(data, 14) == 12)
                    {
                        width = BitConverter.ToUInt16(data, 18);
                        height = Math.Abs((int)BitConverter.ToInt16(data, 20));
                        return true;
                    }
                    width = BitConverter.ToInt32(data, 18);
                    height = (int)Math.Min(Math.Abs((long)BitConverter.ToInt32(data, 22)), int.MaxValue);
                    return true;
                default:
                    return false;
            }
        }

        // Throws when the image is larger than MaxDimension on either side
        public static void CheckSize(byte[] data)
        {
            if (TryGetSize(data, out int width, out int height) && (width > MaxDimension || height > MaxDimension))
                throw new InvalidDataException($"Image is {width}x{height}; the limit is {MaxDimension}x{MaxDimension}.");
        }

        public static SpriteImageFormat GetFormat(byte[] data)
        {
            if (data == null)
                return SpriteImageFormat.Unknown;
            if (data.Length >= 8 && data.AsSpan(0, 8).SequenceEqual(PngSignature))
                return SpriteImageFormat.Png;
            if (data.Length >= 2 && data[0] == 0x42 && data[1] == 0x4D)
                return SpriteImageFormat.Bmp;
            return SpriteImageFormat.Unknown;
        }

        // Bits per pixel of a BMP, or 0 when the header is too short to say
        public static int GetBmpBitsPerPixel(byte[] data)
        {
            if (GetFormat(data) != SpriteImageFormat.Bmp || data.Length < 30)
                return 0;
            int dibSize = BitConverter.ToInt32(data, 14);
            return dibSize == 12 ? BitConverter.ToUInt16(data, 24) : BitConverter.ToUInt16(data, 28);
        }

        // Converts a BMP or PNG to a 24bpp BMP. Pixels with alpha below 128 are filled with the color key
        // (or DefaultColorKey when there is none); BMP alpha is dropped.
        public static byte[] ToBmp24(byte[] data, uint? colorKey = null)
        {
            Image image = Decode(data);
            uint key = colorKey ?? DefaultColorKey;
            byte keyR = (byte)(key >> 16), keyG = (byte)(key >> 8), keyB = (byte)key;

            int rowSize = (image.Width * 3 + 3) & ~3;
            byte[] bmp = new byte[54 + rowSize * image.Height];
            bmp[0] = 0x42; bmp[1] = 0x4D;
            BitConverter.TryWriteBytes(bmp.AsSpan(2), bmp.Length);
            BitConverter.TryWriteBytes(bmp.AsSpan(10), 54);
            BitConverter.TryWriteBytes(bmp.AsSpan(14), 40);
            BitConverter.TryWriteBytes(bmp.AsSpan(18), image.Width);
            BitConverter.TryWriteBytes(bmp.AsSpan(22), image.Height);
            BitConverter.TryWriteBytes(bmp.AsSpan(26), (short)1);
            BitConverter.TryWriteBytes(bmp.AsSpan(28), (short)24);
            BitConverter.TryWriteBytes(bmp.AsSpan(34), rowSize * image.Height);

            for (int y = 0; y < image.Height; y++)
            {
                // BMP rows are stored bottom-up
                int row = 54 + (image.Height - 1 - y) * rowSize;
                for (int x = 0; x < image.Width; x++)
                {
                    int src = (y * image.Width + x) * 4;
                    int dst = row + x * 3;
                    bool transparent = image.Rgba[src + 3] < 128;
                    bmp[dst] = transparent ? keyB : image.Rgba[src + 2];
                    bmp[dst + 1] = transparent ? keyG : image.Rgba[src + 1];
                    bmp[dst + 2] = transparent ? keyR : image.Rgba[src];
                }
            }
            return bmp;
        }

        // Converts a BMP or PNG to an 8-bit RGBA PNG. For a BMP, pixels matching the color key (or, when there is
        // none, the top-left pixel, as the game client does) become transparent.
        public static byte[] ToPng(byte[] data, uint? colorKey = null)
        {
            return ToPng(data, colorKey, out _);
        }

        // keyUsed is the key applied to a BMP as 0xFFRRGGBB, or null for a PNG
        internal static byte[] ToPng(byte[] data, uint? colorKey, out uint? keyUsed)
        {
            keyUsed = null;
            Image image = Decode(data);
            if (GetFormat(data) == SpriteImageFormat.Bmp && image.Width > 0 && image.Height > 0)
            {
                byte keyR, keyG, keyB;
                if (colorKey.HasValue)
                {
                    keyR = (byte)(colorKey.Value >> 16); keyG = (byte)(colorKey.Value >> 8); keyB = (byte)colorKey.Value;
                }
                else
                {
                    keyR = image.Rgba[0]; keyG = image.Rgba[1]; keyB = image.Rgba[2];
                }
                keyUsed = 0xFF000000u | ((uint)keyR << 16) | ((uint)keyG << 8) | keyB;
                for (int i = 0; i < image.Rgba.Length; i += 4)
                {
                    if (image.Rgba[i] == keyR && image.Rgba[i + 1] == keyG && image.Rgba[i + 2] == keyB)
                        image.Rgba[i + 3] = 0;
                }
            }
            return EncodePng(image);
        }

        internal class Image
        {
            public int Width;
            public int Height;
            public byte[] Rgba = Array.Empty<byte>();   // top-down rows, 4 bytes per pixel
        }

        internal static Image Decode(byte[] data)
        {
            return GetFormat(data) switch
            {
                SpriteImageFormat.Bmp => DecodeBmp(data),
                SpriteImageFormat.Png => DecodePng(data),
                _ => throw new InvalidDataException("Image data is neither BMP nor PNG.")
            };
        }

        private static Image NewImage(long width, long height)
        {
            if (width <= 0 || height <= 0)
                throw new InvalidDataException($"Unsupported image size {width}x{height}.");
            if (width > MaxDimension || height > MaxDimension)
                throw new InvalidDataException($"Image is {width}x{height}; the limit is {MaxDimension}x{MaxDimension}.");
            return new Image { Width = (int)width, Height = (int)height, Rgba = new byte[width * height * 4] };
        }

        private static Image DecodeBmp(byte[] d)
        {
            if (d.Length < 26)
                throw new InvalidDataException("BMP header is truncated.");
            long pixelOffset = BitConverter.ToUInt32(d, 10);
            int dibSize = BitConverter.ToInt32(d, 14);
            long width, height;
            int bitsPerPixel, compression = 0, colorsUsed = 0, paletteEntrySize;
            if (dibSize == 12)
            {
                width = BitConverter.ToUInt16(d, 18);
                height = BitConverter.ToInt16(d, 20);
                bitsPerPixel = BitConverter.ToUInt16(d, 24);
                paletteEntrySize = 3;
            }
            else if (dibSize >= 40 && d.Length >= 14 + 40)
            {
                width = BitConverter.ToInt32(d, 18);
                height = BitConverter.ToInt32(d, 22);
                bitsPerPixel = BitConverter.ToUInt16(d, 28);
                compression = BitConverter.ToInt32(d, 30);
                colorsUsed = BitConverter.ToInt32(d, 46);
                paletteEntrySize = 4;
            }
            else
            {
                throw new InvalidDataException($"Unsupported BMP header size {dibSize}.");
            }

            bool topDown = height < 0;
            if (bitsPerPixel != 1 && bitsPerPixel != 4 && bitsPerPixel != 8 && bitsPerPixel != 16 && bitsPerPixel != 24 && bitsPerPixel != 32)
                throw new InvalidDataException($"Unsupported BMP bit depth {bitsPerPixel}.");
            if (compression != 0 && compression != 1 && compression != 2 && compression != 3 && compression != 6)
                throw new InvalidDataException($"Unsupported BMP compression {compression}.");

            // Check the pixel data is all there before allocating anything
            long stride = ((long)bitsPerPixel * Math.Max(width, 0) + 31) / 32 * 4;
            if ((compression == 0 || compression == 3 || compression == 6) && pixelOffset + stride * Math.Abs(height) > d.Length)
                throw new InvalidDataException("BMP pixel data is truncated.");

            Image image = NewImage(width, Math.Abs(height));
            int w = image.Width, h = image.Height;

            // Palette for 1, 4 and 8 bpp
            byte[][] palette = Array.Empty<byte[]>();
            if (bitsPerPixel <= 8)
            {
                int count = colorsUsed > 0 && colorsUsed <= 256 ? colorsUsed : 1 << bitsPerPixel;
                long paletteOffset = 14 + dibSize + (compression == 3 && dibSize == 40 ? 12 : 0);
                palette = new byte[count][];
                for (int i = 0; i < count; i++)
                {
                    long p = paletteOffset + (long)i * paletteEntrySize;
                    palette[i] = p + 3 <= d.Length ? [d[p + 2], d[p + 1], d[p]] : [0, 0, 0];
                }
            }

            void SetIndexed(int x, int y, int index)
            {
                byte[] color = index < palette.Length ? palette[index] : [0, 0, 0];
                int o = (y * w + x) * 4;
                image.Rgba[o] = color[0]; image.Rgba[o + 1] = color[1]; image.Rgba[o + 2] = color[2]; image.Rgba[o + 3] = 255;
            }

            if (compression == 1 || compression == 2)
            {
                // Pixels the encoding skips (delta, early end of line) take palette index 0, as Windows does
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        SetIndexed(x, y, 0);
                DecodeBmpRle(d, pixelOffset, w, h, compression == 2, (x, rowFromBottom, index) => SetIndexed(x, h - 1 - rowFromBottom, index));
                return image;
            }

            // Channel masks for 16 and 32 bpp
            uint maskR, maskG, maskB;
            if (compression == 3 || compression == 6)
            {
                long maskOffset = 14 + 40;
                if (maskOffset + 12 > d.Length)
                    throw new InvalidDataException("BMP bitfield masks are truncated.");
                maskR = BitConverter.ToUInt32(d, (int)maskOffset);
                maskG = BitConverter.ToUInt32(d, (int)maskOffset + 4);
                maskB = BitConverter.ToUInt32(d, (int)maskOffset + 8);
            }
            else if (bitsPerPixel == 16)
            {
                maskR = 0x7C00; maskG = 0x03E0; maskB = 0x001F;
            }
            else
            {
                maskR = 0x00FF0000; maskG = 0x0000FF00; maskB = 0x000000FF;
            }

            for (int y = 0; y < h; y++)
            {
                long row = pixelOffset + (topDown ? y : h - 1 - y) * stride;
                for (int x = 0; x < w; x++)
                {
                    int o = (y * w + x) * 4;
                    switch (bitsPerPixel)
                    {
                        case 1: SetIndexed(x, y, (d[row + x / 8] >> (7 - x % 8)) & 1); break;
                        case 4: SetIndexed(x, y, (d[row + x / 2] >> (x % 2 == 0 ? 4 : 0)) & 0xF); break;
                        case 8: SetIndexed(x, y, d[row + x]); break;
                        case 24:
                            image.Rgba[o] = d[row + x * 3 + 2];
                            image.Rgba[o + 1] = d[row + x * 3 + 1];
                            image.Rgba[o + 2] = d[row + x * 3];
                            image.Rgba[o + 3] = 255;
                            break;
                        default:
                            uint v = bitsPerPixel == 16 ? BitConverter.ToUInt16(d, (int)(row + x * 2)) : BitConverter.ToUInt32(d, (int)(row + x * 4));
                            image.Rgba[o] = Channel(v, maskR);
                            image.Rgba[o + 1] = Channel(v, maskG);
                            image.Rgba[o + 2] = Channel(v, maskB);
                            image.Rgba[o + 3] = 255;    // BMP alpha is dropped
                            break;
                    }
                }
            }
            return image;
        }

        private static byte Channel(uint value, uint mask)
        {
            if (mask == 0)
                return 0;
            int shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
            ulong max = mask >> shift;
            return (byte)((ulong)((value & mask) >> shift) * 255 / max);
        }

        private static void DecodeBmpRle(byte[] d, long offset, int w, int h, bool rle4, Action<int, int, int> set)
        {
            int x = 0, y = 0;
            long p = offset;
            void Put(int index)
            {
                if (x < w && y < h)
                    set(x, y, index);
                x++;
            }

            while (p + 1 < d.Length && y < h)
            {
                int count = d[p], value = d[p + 1];
                p += 2;
                if (count > 0)
                {
                    for (int i = 0; i < count; i++)
                        Put(rle4 ? (i % 2 == 0 ? value >> 4 : value & 0xF) : value);
                }
                else if (value == 0)
                {
                    x = 0; y++;     // end of line
                }
                else if (value == 1)
                {
                    break;          // end of bitmap
                }
                else if (value == 2)
                {
                    if (p + 1 >= d.Length)
                        break;
                    x += d[p]; y += d[p + 1];
                    p += 2;
                }
                else
                {
                    // Absolute run of `value` pixels, padded to a 16-bit boundary
                    int bytes = rle4 ? (value + 1) / 2 : value;
                    for (int i = 0; i < value && p + (rle4 ? i / 2 : i) < d.Length; i++)
                        Put(rle4 ? (i % 2 == 0 ? d[p + i / 2] >> 4 : d[p + i / 2] & 0xF) : d[p + i]);
                    p += (bytes + 1) & ~1;
                }
            }
        }

        private static Image DecodePng(byte[] d)
        {
            long position = 8;
            int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
            byte[] palette = Array.Empty<byte>();
            byte[] transparency = Array.Empty<byte>();
            using MemoryStream compressed = new MemoryStream();

            while (position + 12 <= d.Length)
            {
                long length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan((int)position, 4));
                string type = Encoding.ASCII.GetString(d, (int)position + 4, 4);
                long dataStart = position + 8;
                if (dataStart + length > d.Length)
                    throw new InvalidDataException($"PNG chunk {type} is truncated.");
                ReadOnlySpan<byte> chunk = d.AsSpan((int)dataStart, (int)length);

                if (type == "IHDR" && length >= 13)
                {
                    width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(chunk);
                    height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(chunk.Slice(4));
                    bitDepth = chunk[8];
                    colorType = chunk[9];
                    interlace = chunk[12];
                }
                else if (type == "PLTE") palette = chunk.ToArray();
                else if (type == "tRNS") transparency = chunk.ToArray();
                else if (type == "IDAT") compressed.Write(chunk);
                else if (type == "IEND") break;

                position = dataStart + length + 4;
            }

            int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new InvalidDataException($"Unsupported PNG color type {colorType}.") };
            if (bitDepth != 1 && bitDepth != 2 && bitDepth != 4 && bitDepth != 8 && bitDepth != 16)
                throw new InvalidDataException($"Unsupported PNG bit depth {bitDepth}.");
            Image image = NewImage(width, height);

            int bitsPerPixel = channels * bitDepth;
            int filterStep = Math.Max(1, bitsPerPixel / 8);
            int[][] passes = interlace == 1
                ? [[0, 0, 8, 8], [4, 0, 8, 8], [0, 4, 4, 8], [2, 0, 4, 4], [0, 2, 2, 4], [1, 0, 2, 2], [0, 1, 1, 2]]
                : [[0, 0, 1, 1]];

            // Inflate only as much as the image needs, so damaged data cannot expand without limit
            long expected = 0;
            foreach (int[] pass in passes)
            {
                long passWidth = (width - pass[0] + pass[2] - 1) / pass[2];
                long passHeight = (height - pass[1] + pass[3] - 1) / pass[3];
                if (passWidth > 0 && passHeight > 0)
                    expected += ((passWidth * bitsPerPixel + 7) / 8 + 1) * passHeight;
            }
            byte[] pixels = new byte[expected];
            compressed.Position = 0;
            try
            {
                using ZLibStream zlib = new ZLibStream(compressed, CompressionMode.Decompress);
                int filled = 0;
                while (filled < pixels.Length)
                {
                    int read = zlib.Read(pixels, filled, pixels.Length - filled);
                    if (read == 0)
                        break;
                    filled += read;
                }
                if (filled < pixels.Length)
                    Array.Resize(ref pixels, filled);   // reported as truncated below
            }
            catch (IOException ex)
            {
                throw new InvalidDataException($"PNG image data cannot be decompressed. {ex.Message}", ex);
            }

            int offset = 0;
            foreach (int[] pass in passes)
            {
                int passWidth = (width - pass[0] + pass[2] - 1) / pass[2];
                int passHeight = (height - pass[1] + pass[3] - 1) / pass[3];
                if (passWidth <= 0 || passHeight <= 0)
                    continue;

                int rowBytes = (passWidth * bitsPerPixel + 7) / 8;
                byte[] previous = new byte[rowBytes];
                byte[] current = new byte[rowBytes];
                for (int row = 0; row < passHeight; row++)
                {
                    if (offset + 1 + rowBytes > pixels.Length)
                        throw new InvalidDataException("PNG image data is truncated.");
                    Unfilter(pixels[offset], pixels.AsSpan(offset + 1, rowBytes), previous, current, filterStep);
                    offset += 1 + rowBytes;

                    int y = pass[1] + row * pass[3];
                    for (int column = 0; column < passWidth; column++)
                    {
                        int x = pass[0] + column * pass[2];
                        WritePngPixel(image, x, y, current, column, colorType, bitDepth, channels, palette, transparency);
                    }
                    (previous, current) = (current, previous);
                }
            }
            return image;
        }

        private static void Unfilter(byte filter, ReadOnlySpan<byte> source, byte[] previous, byte[] current, int step)
        {
            for (int i = 0; i < source.Length; i++)
            {
                int left = i >= step ? current[i - step] : 0;
                int up = previous[i];
                int upLeft = i >= step ? previous[i - step] : 0;
                int predictor = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => throw new InvalidDataException($"Unsupported PNG filter {filter}.")
                };
                current[i] = (byte)(source[i] + predictor);
            }
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        // Reads sample `index` of a row at the given bit depth, unscaled
        private static int Sample(byte[] row, int index, int bitDepth)
        {
            return bitDepth switch
            {
                8 => row[index],
                16 => (row[index * 2] << 8) | row[index * 2 + 1],
                _ => (row[index * bitDepth / 8] >> (8 - bitDepth - (index * bitDepth) % 8)) & ((1 << bitDepth) - 1)
            };
        }

        private static void WritePngPixel(Image image, int x, int y, byte[] row, int column, int colorType, int bitDepth,
            int channels, byte[] palette, byte[] transparency)
        {
            int max = (1 << bitDepth) - 1;
            byte Scale(int value) => (byte)(bitDepth == 16 ? value >> 8 : value * 255 / max);
            int TrnsSample(int i) => transparency.Length >= i * 2 + 2 ? (transparency[i * 2] << 8) | transparency[i * 2 + 1] : -1;

            int o = (y * image.Width + x) * 4;
            int s = column * channels;
            switch (colorType)
            {
                case 0:
                {
                    int g = Sample(row, s, bitDepth);
                    image.Rgba[o] = image.Rgba[o + 1] = image.Rgba[o + 2] = Scale(g);
                    image.Rgba[o + 3] = (byte)(g == TrnsSample(0) ? 0 : 255);
                    break;
                }
                case 2:
                {
                    int r = Sample(row, s, bitDepth), g = Sample(row, s + 1, bitDepth), b = Sample(row, s + 2, bitDepth);
                    image.Rgba[o] = Scale(r); image.Rgba[o + 1] = Scale(g); image.Rgba[o + 2] = Scale(b);
                    image.Rgba[o + 3] = (byte)(r == TrnsSample(0) && g == TrnsSample(1) && b == TrnsSample(2) ? 0 : 255);
                    break;
                }
                case 3:
                {
                    int index = Sample(row, s, bitDepth);
                    if (index * 3 + 2 < palette.Length)
                    {
                        image.Rgba[o] = palette[index * 3]; image.Rgba[o + 1] = palette[index * 3 + 1]; image.Rgba[o + 2] = palette[index * 3 + 2];
                    }
                    image.Rgba[o + 3] = index < transparency.Length ? transparency[index] : (byte)255;
                    break;
                }
                case 4:
                    image.Rgba[o] = image.Rgba[o + 1] = image.Rgba[o + 2] = Scale(Sample(row, s, bitDepth));
                    image.Rgba[o + 3] = Scale(Sample(row, s + 1, bitDepth));
                    break;
                default:
                    image.Rgba[o] = Scale(Sample(row, s, bitDepth));
                    image.Rgba[o + 1] = Scale(Sample(row, s + 1, bitDepth));
                    image.Rgba[o + 2] = Scale(Sample(row, s + 2, bitDepth));
                    image.Rgba[o + 3] = Scale(Sample(row, s + 3, bitDepth));
                    break;
            }
        }

        private static byte[] EncodePng(Image image)
        {
            int rowBytes = image.Width * 4;
            byte[] raw = new byte[(rowBytes + 1) * image.Height];
            for (int y = 0; y < image.Height; y++)
            {
                // Filter type 0 (none) per row
                Buffer.BlockCopy(image.Rgba, y * rowBytes, raw, y * (rowBytes + 1) + 1, rowBytes);
            }

            using MemoryStream compressed = new MemoryStream();
            using (ZLibStream zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
                zlib.Write(raw);

            byte[] header = new byte[13];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
            header[8] = 8;  // bit depth
            header[9] = 6;  // RGBA

            using MemoryStream png = new MemoryStream();
            png.Write(PngSignature);
            WriteChunk(png, "IHDR", header);
            WriteChunk(png, "IDAT", compressed.ToArray());
            WriteChunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        private static void WriteChunk(MemoryStream png, string type, byte[] data)
        {
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            Span<byte> number = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
            png.Write(number);
            png.Write(typeBytes);
            png.Write(data);
            uint crc = Crc32(Crc32(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, crc);
            png.Write(number);
        }

        private static uint[]? crcTable;

        private static uint Crc32(uint crc, byte[] data)
        {
            if (crcTable == null)
            {
                uint[] table = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++)
                        c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    table[n] = c;
                }
                crcTable = table;
            }
            foreach (byte b in data)
                crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}
