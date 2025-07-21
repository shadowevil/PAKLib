using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PAKLib.Headers
{
    public struct SpriteHeader
    {
        public string Magic { get; set; } = string.Empty;
        public byte[] Padding = new byte[80];

        public SpriteHeader()
        { }

        public static SpriteHeader Default()
        {
            return new SpriteHeader
            {
                Magic = "<Sprite File Header>"
            };
        }
    }
}
