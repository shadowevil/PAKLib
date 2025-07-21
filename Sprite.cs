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
    }
}
