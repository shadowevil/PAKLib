using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PAKLib
{
    public class PAKRepairIssue
    {
        // Position of the sprite in the original file order (or the sprite table index for table issues),
        // or null for file-level issues
        public int? SpriteIndex { get; set; }
        // Byte offset in the (decrypted) input where the issue was found
        public long Offset { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool Repaired { get; set; }

        public override string ToString()
        {
            string where = SpriteIndex.HasValue ? $"sprite {SpriteIndex}" : "file";
            return $"[{(Repaired ? "repaired" : "not repaired")}] {where} @ {Offset}: {Description}";
        }
    }

    public class PAKRepairReport
    {
        // The salvaged sprites, ready to be written with PAKData.Write or PAK.SaveToFile
        public PAKData Data { get; set; } = new PAKData();

        public List<PAKRepairIssue> Issues { get; } = new List<PAKRepairIssue>();

        // Indices in Data.Sprites of the 32x32 placeholders inserted for corrupt sprites (force only)
        public List<int> PlaceholderSprites { get; } = new List<int>();

        // True when every issue found was repaired (nothing was dropped)
        public bool Success => Issues.All(i => i.Repaired);
    }
}
