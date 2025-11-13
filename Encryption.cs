using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PAKLib
{
    public static class Encryption
    {
        private static byte RotL8(byte v, int s)
        {
            return (byte)((v << s) | (v >> (8 - s)));
        }

        private static byte RotR8(byte v, int s)
        {
            return (byte)((v >> s) | (v << (8 - s)));
        }

        public static void EncryptBytes(byte[] data, string key, long startOffset = 0)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Encryption key cannot be empty.", nameof(key));
            if (data == null || data.Length == 0)
                return;

            int keyLen = key.Length;
            for (int i = 0; i < data.Length; i++)
            {
                long globalIndex = startOffset + i;
                byte keyByte = (byte)key[(int)(globalIndex % keyLen)];
                data[i] ^= keyByte;
                int rot = keyByte % 8;
                if (rot != 0)
                    data[i] = RotL8(data[i], rot);
            }
        }

        public static void DecryptBytes(byte[] data, string key, long startOffset = 0)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Decryption key cannot be empty.", nameof(key));
            if (data == null || data.Length == 0)
                return;

            int keyLen = key.Length;
            for (int i = 0; i < data.Length; i++)
            {
                long globalIndex = startOffset + i;
                byte keyByte = (byte)key[(int)(globalIndex % keyLen)];
                int rot = keyByte % 8;
                if (rot != 0)
                    data[i] = RotR8(data[i], rot);
                data[i] ^= keyByte;
            }
        }

        public static void EncryptFile(string inputPath, string outputPath, string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Encryption key cannot be empty.", nameof(key));
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Input file not found.", inputPath);

            byte[] data = File.ReadAllBytes(inputPath);
            EncryptBytes(data, key, 0);
            File.WriteAllBytes(outputPath, data);
        }

        public static void DecryptFile(string inputPath, string outputPath, string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Decryption key cannot be empty.", nameof(key));
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Input file not found.", inputPath);

            byte[] data = File.ReadAllBytes(inputPath);
            DecryptBytes(data, key, 0);
            File.WriteAllBytes(outputPath, data);
        }

        private static readonly byte[] sharedBuffer = new byte[1024 * 1024 * 4]; // 4 MB preallocated buffer

        public static byte[] ReadAndDecryptSection(FileStream fs, long offset, int size, string key)
        {
            if (fs == null)
                throw new ArgumentNullException(nameof(fs));
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Decryption key cannot be empty.", nameof(key));

            byte[] buffer = size <= sharedBuffer.Length ? sharedBuffer : new byte[size];

            fs.Seek(offset, SeekOrigin.Begin);
            int read = fs.Read(buffer, 0, size);
            if (read != size)
                throw new IOException("Failed to read encrypted section from file.");

            byte[] section = new byte[size];
            Buffer.BlockCopy(buffer, 0, section, 0, size);
            DecryptBytes(section, key, offset);
            return section;
        }
    }
}