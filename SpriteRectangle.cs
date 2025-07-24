using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PAKLib
{
    public class SpriteRectangle
    {
        public Int16 x;
        public Int16 y;
        public Int16 width;
        public Int16 height;
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
