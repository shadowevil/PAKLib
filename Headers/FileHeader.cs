using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PAKLib.Headers
{
    public struct FileHeader
    {
        public string Magic { get; set; } = string.Empty;
        public byte[] Padding = [0x00, 0x00, 0x00];

        public FileHeader()
        { }

        public static FileHeader Default()
        {
            return new FileHeader
            {
                Magic = "<Pak file header>"
            };
        }
    }
}
