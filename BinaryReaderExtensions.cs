using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PAKLib
{
    public static class BinaryReaderExtensions
    {
        public static byte[] PeekBytes(this BinaryReader reader, int count)
        {
            long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
            if (remaining <= 0)
                return Array.Empty<byte>();

            int readCount = (int)Math.Min(count, remaining);
            byte[] bytes = reader.ReadBytes(readCount);
            reader.BaseStream.Seek(-readCount, SeekOrigin.Current);
            return bytes;
        }

        public static void SkipBytes(this BinaryReader reader, int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be non-negative.");
            }
            if (reader.EndOfStream())
            {
                return;
            }
            reader.BaseStream.Seek(count, System.IO.SeekOrigin.Current);
        }

        public static bool EndOfStream(this BinaryReader reader)
        {
            return reader.BaseStream.Position >= reader.BaseStream.Length;
        }
    }
}
