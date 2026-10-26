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

        public SpriteImageFormat ImageFormat => SpriteImage.GetFormat(data);

        // Converts the image to a 24bpp BMP. Transparent PNG pixels are filled with the color key; a PNG without
        // a color key gets SpriteImage.DefaultColorKey (magenta) as its key.
        public void ConvertToBmp()
        {
            if (ImageFormat == SpriteImageFormat.Png && !ColorKey.HasValue)
                ColorKey = SpriteImage.DefaultColorKey;
            data = SpriteImage.ToBmp24(data, ColorKey);
        }

        // Converts the image to an 8-bit RGBA PNG. BMP pixels matching the color key (or the top-left pixel when
        // there is none) become transparent. A BMP without a color key gets the top-left color as its key, so
        // converting back to BMP restores those pixels.
        public void ConvertToPng()
        {
            data = SpriteImage.ToPng(data, ColorKey, out uint? keyUsed);
            ColorKey ??= keyUsed;
        }
    }
}
