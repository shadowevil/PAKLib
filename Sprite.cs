using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PAKLib
{
    public class Sprite
    {
        public List<SpriteRectangle> Rectangles { get; set; } = new List<SpriteRectangle>();
        public byte[] data { get; set; } = Array.Empty<byte>();

        // Authored transparency color key as 0xFFRRGGBB (stored on disk as bytes A RR GG BB).
        // Null means none was authored, in which case the key is derived from the first pixel of the sprite sheet.
        // Alpha only marks the key as set: any value whose alpha is not 0xFF (including legacy 00 RR GG BB)
        // is treated as unset, so black (FF 00 00 00) stays distinct from none (00 00 00 00).
        private uint? colorKey = null;
        public uint? ColorKey
        {
            get => colorKey;
            set => colorKey = value.HasValue && (value.Value >> 24) == 0xFF ? value : null;
        }

        public byte? ColorKeyR => ColorKey.HasValue ? (byte)(ColorKey.Value >> 16) : null;
        public byte? ColorKeyG => ColorKey.HasValue ? (byte)(ColorKey.Value >> 8) : null;
        public byte? ColorKeyB => ColorKey.HasValue ? (byte)ColorKey.Value : null;

        public void SetColorKey(byte r, byte g, byte b)
        {
            ColorKey = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
        }

        public void ClearColorKey()
        {
            ColorKey = null;
        }
    }
}
