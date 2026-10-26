using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PAKLib
{
    public class SpriteRectangle
    {
        public UInt16 x;
        public UInt16 y;
        public UInt16 width;
        public UInt16 height;
        public Int16 pivotX;
        public Int16 pivotY;

        public static int Size()
        {
            return typeof(SpriteRectangle).GetFields(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance)
                .Sum(f => System.Runtime.InteropServices.Marshal.SizeOf(f.FieldType));
        }
    }
}
