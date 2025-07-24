using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using PAKLib.Headers;

namespace PAKLib
{
    public class PAKData
    {
        public List<Sprite> Sprites { get; set; } = new List<Sprite>();
        public int SpriteCount => Sprites.Count;
        public List<Range> SpriteEntryLengthOffsets { get; set; } = new List<Range>();

        public PAKData()
        { }

        public static PAKData Read(byte[] bytes)
        {
            using MemoryStream ms = new MemoryStream(bytes);
            using BinaryReader reader = new BinaryReader(ms);

            // Read the file header
            FileHeader header = new FileHeader
            {
                Magic = Encoding.UTF8.GetString(reader.ReadBytes(FileHeader.Default().Magic.Length)),
                Padding = reader.ReadBytes(3)
            };

            // Check if the magic is correct
            if (header.Magic != FileHeader.Default().Magic)
            {
                throw new InvalidDataException("Invalid PAK file header magic.");
            }

            // Create new PAKData object
            PAKData data = new PAKData();

            // Read the sprite count
            int spriteCount = reader.ReadInt32();

            // Read sprite entry offsets beginning and end
            data.SpriteEntryLengthOffsets = new List<Range>(spriteCount);
            for (int i=0; i < spriteCount; i++)
            {
                data.SpriteEntryLengthOffsets.Add(new Range(
                    reader.ReadInt32(),     // ASDFileLocation
                    reader.ReadInt32()      // ChunkSize
                    ));
            }

            data.Sprites = new List<Sprite>(spriteCount);
            foreach (var (SpriteChunkOffset, ChunkSize) in data.SpriteEntryLengthOffsets.Select(x => (x.Start.Value, x.End.Value)))
            {
                if(reader.BaseStream.Position != SpriteChunkOffset)
                    throw new Exception($"Expected to be at position {SpriteChunkOffset}, but was at {reader.BaseStream.Position}.");

                // Read the header and padding
                var defaultHeader = SpriteHeader.Default();
                SpriteHeader spriteHeader = new SpriteHeader
                {
                    Magic = Encoding.UTF8.GetString(reader.ReadBytes(defaultHeader.Magic.Length)),
                    Padding = reader.ReadBytes(defaultHeader.Padding.Length)
                };
                // Check if the magic is correct
                if (spriteHeader.Magic != SpriteHeader.Default().Magic)
                    throw new InvalidDataException("Invalid sprite header magic.");

                // Create new sprite object instance
                Sprite sprite = new Sprite();

                // Read the number of rectangles and set the capacity of the Rectangles list
                sprite.Rectangles = new List<SpriteRectangle>(reader.ReadInt32());
                for (int j = 0; j < sprite.Rectangles.Capacity; j++)
                {
                    // Read 12 bytes of data (x, y, width, height, pivotX, pivotY) for each rectangle
                    sprite.Rectangles.Add(new SpriteRectangle()
                    {
                        x = reader.ReadInt16(),
                        y = reader.ReadInt16(),
                        width = reader.ReadInt16(),
                        height = reader.ReadInt16(),
                        pivotX = reader.ReadInt16(),
                        pivotY = reader.ReadInt16()
                    });
                }

                // Padding
                reader.SkipBytes(4);

                // Calculate the size (in bytes) of the sprite itself (bmp)
                int sizeOfSpriteHeader = defaultHeader.Magic.Length + defaultHeader.Padding.Length;
                int sizeOfRectangleCount = sizeof(Int32);
                int sizeOfSpriteRectangles = (sprite.Rectangles.Count * SpriteRectangle.Size()) + sizeOfRectangleCount;
                int sizeOfPadding = sizeof(Int32);
                long newLength = ChunkSize - (sizeOfSpriteHeader + sizeOfSpriteRectangles) - sizeOfPadding;
                sprite.data = reader.ReadBytes((int)newLength);
                
                // Only add if valid BMP data
                if (IsValidBmp(sprite.data) || IsValidPng(sprite.data))
                {
                    data.Sprites.Add(sprite);
                } else
                {
                    throw new InvalidDataException("Invalid sprite data format. Expected BMP or PNG.");
                }
            }

            return data;
        }

        private static bool IsValidBmp(byte[] data)
        {
            if (data == null || data.Length < 14)
                return false;

            // Check BMP signature
            byte[] bmpSig = [0x42, 0x4D];
            if (!data.Take(2).SequenceEqual(bmpSig))
                return false;

            int declaredSize = BitConverter.ToInt32(data, 2);
            if (declaredSize > data.Length)
                return false;

            int width = BitConverter.ToInt32(data, 18);
            int height = BitConverter.ToInt32(data, 22);

            if (width <= 0 || height <= 0)
            {
                return false;
            }

            return true;
        }

        private static bool IsValidPng(byte[] data)
        {
            if (data == null || data.Length < 8)
                return false;

            // Check PNG signature
            byte[] pngSig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
            if (!data.Take(8).SequenceEqual(pngSig))
                return false;

            // Check for IHDR chunk after signature
            if (data.Length < 24)
                return false;

            // First chunk length (bytes 8–11), should be 13 for IHDR
            int ihdrLength = BitConverter.ToInt32(data.Skip(8).Take(4).Reverse().ToArray(), 0);
            if (ihdrLength != 13)
                return false;

            // Chunk type (bytes 12–15)
            string chunkType = Encoding.ASCII.GetString(data, 12, 4);
            return chunkType == "IHDR";
        }

        public void Write(in string FilePath)
        {
            using MemoryStream ms = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(ms);

            // Write default file header
            FileHeader header = FileHeader.Default();
            writer.Write(Encoding.UTF8.GetBytes(header.Magic));
            writer.Write(header.Padding);

            // Write Sprite Count
            writer.Write(SpriteCount);
            long[] spriteOffsets = new long[SpriteCount];
            for(int i=0;i<SpriteCount; i++)
            {
                // Write placeholder values (4 bytes (int)) for sprite entry offsets and entry lengths
                spriteOffsets[i] = writer.BaseStream.Position;
                writer.Write(0);    // offset
                writer.Write(0);    // length
            }

            int offsetIndex = 0;

            foreach (var sprite in Sprites)
            {
                // Begin counting how many bytes were written to calculate the correct image size
                int startOffset = (int)writer.BaseStream.Position;

                // Write default header
                SpriteHeader spriteHeader = SpriteHeader.Default();
                writer.Write(Encoding.UTF8.GetBytes(spriteHeader.Magic));
                writer.Write(spriteHeader.Padding);

                // Write Rectangle data
                writer.Write(sprite.Rectangles.Count);
                foreach (var rect in sprite.Rectangles)
                {
                    writer.Write(rect.x);
                    writer.Write(rect.y);
                    writer.Write(rect.width);
                    writer.Write(rect.height);
                    writer.Write(rect.pivotX);
                    writer.Write(rect.pivotY);
                }
                
                // Padding
                writer.Write(new byte[4]);

                // End counting bytes, and calculate the total bytes written for the sprite data (header + rectangles + padding)
                int totalBytesWritten = (int)writer.BaseStream.Position - startOffset;

                // Write the sprite data
                writer.Write(sprite.data);

                // Get the current position after writing image data to return to the end after updating offsets
                int endOffset = (int)writer.BaseStream.Position;

                // Update the sprite entry length offsets
                {
                    // The first entry in spriteOffsets corresponds to the current sprite already written.
                    writer.BaseStream.Position = spriteOffsets[offsetIndex++];

                    // Write the start offset of the sprite data
                    writer.Write(startOffset);

                    // Write the length of the sprite data, which is the total bytes of the header, rectangles, padding, and the sprite data itself
                    writer.Write(sprite.data.Length + totalBytesWritten);

                    // Return the stream back to the end of the last sprite data written.
                    writer.BaseStream.Position = endOffset;
                }
            }

            writer.Flush();
            File.WriteAllBytes(FilePath, ms.ToArray());
        }
    }
}
