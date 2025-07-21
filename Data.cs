using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using PAKLib.Headers;

namespace PAKLib
{
    public class Data
    {
        public List<Sprite> Sprites { get; set; } = new List<Sprite>();
        public int SpriteCount => Sprites.Count;
        public List<Range> SpriteEntryLengthOffsets { get; set; } = new List<Range>();

        private Data()
        { }

        public static Data Create(byte[] bytes)
        {
            using MemoryStream ms = new MemoryStream(bytes);
            using BinaryReader reader = new BinaryReader(ms);

            // Read the file header
            FileHeader header = new FileHeader
            {
                Magic = Encoding.UTF8.GetString(reader.ReadBytes(FileHeader.Default().Magic.Length)),
                Padding = reader.ReadBytes(3)
            };

            if (header.Magic != FileHeader.Default().Magic)
            {
                throw new InvalidDataException("Invalid PAK file header magic.");
            }
            Data data = new Data();

            // Read the sprite count
            int spriteCount = reader.ReadInt32();

            // Read sprite entry offsets beginning and end
            data.SpriteEntryLengthOffsets = new List<Range>(spriteCount);
            for (int i=0; i < spriteCount; i++)
            {
                data.SpriteEntryLengthOffsets.Add(new Range(
                    reader.ReadInt32(),
                    reader.ReadInt32()
                    ));
            }

            data.Sprites = new List<Sprite>(spriteCount);
            foreach (var (start, length) in data.SpriteEntryLengthOffsets.Select(x => (x.Start.Value, x.End.Value)))
            {
                reader.BaseStream.Seek(start, SeekOrigin.Begin);
                SpriteHeader spriteHeader = new SpriteHeader
                {
                    Magic = Encoding.UTF8.GetString(reader.ReadBytes(SpriteHeader.Default().Magic.Length)),
                    Padding = reader.ReadBytes(80)
                };
                if (spriteHeader.Magic != SpriteHeader.Default().Magic)
                    throw new InvalidDataException("Invalid sprite header magic.");

                Sprite sprite = new Sprite();
                sprite.Rectangles = new List<SpriteRectangle>(reader.ReadInt32());
                for (int j = 0; j < sprite.Rectangles.Capacity; j++)
                {
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

                _ = reader.ReadBytes(4);

                sprite.data = reader.ReadBytes(length);

                data.Sprites.Add(sprite);
            }

            return data;
        }
    }
}
