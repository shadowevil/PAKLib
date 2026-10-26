using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PAKLib.Headers;

namespace PAKLib
{
    // Salvages sprites from damaged PAK data so they can be rebuilt into a clean file.
    // Sprites are found by their image data (BMP or PNG), then the frame rectangles and color key in front
    // of each image are recovered, using the sprite table, the sprite header magic and the end of the
    // previous sprite as supporting evidence. Corrupt or incomplete sprites are flagged; with force they
    // are replaced by a 32x32 placeholder so the sprite indices of the rebuilt file stay the same.
    public static class PAKRepair
    {
        private const int SpriteCountOffset = 20;
        private const int SpriteTableOffset = 24;
        private const int SpriteTableEntrySize = 8;
        private const int SpriteHeaderSize = 100;
        private const int MinSpriteEntrySize = SpriteHeaderSize + 4 + 4; // header + rectangle count + color key
        private const int WideRectangleSize = 16;
        private const int MaxRectangles = 65535;
        private const int PlaceholderSize = 32;

        private static readonly int RectangleSize = SpriteRectangle.Size();
        private static readonly byte[] FileMagic = Encoding.UTF8.GetBytes(FileHeader.Default().Magic);
        private static readonly byte[] SpriteMagic = Encoding.UTF8.GetBytes(SpriteHeader.Default().Magic);

        private class Slot
        {
            public long Position;               // start of the sprite entry, or of its image when the entry is lost
            public long End;                    // end of the bytes this sprite claims
            public Sprite? Sprite;              // null when the sprite is corrupt
            public string Problem = string.Empty;
            public int RectangleCountHint = -1; // frame count to give a placeholder, when known
            public List<(long Offset, string Text)> Notes = new List<(long, string)>();
        }

        private struct ImageProbe
        {
            public bool IsImage;
            public bool Complete;
            public long Length;     // full length when complete, otherwise the bytes available
            public bool SizeFieldDamaged;   // BMP size field runs past the data; Length comes from the pixel layout
        }

        // Salvages every sprite in a PAK file.
        public static PAKRepairReport Repair(byte[] bytes, bool force = false)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            return Repair(bytes, 0, bytes.Length, force, true);
        }

        // Salvages the sprites in one section [start, end) of a PAK file. The section must contain whole sprite entries.
        public static PAKRepairReport Repair(byte[] bytes, int start, int end, bool force = false)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            if (start < 0 || end > bytes.Length || start > end)
                throw new ArgumentOutOfRangeException(nameof(start), "Section must lie within the data.");
            return Repair(bytes, start, end, force, false);
        }

        private static PAKRepairReport Repair(byte[] bytes, int from, int to, bool force, bool wholeFile)
        {
            PAKRepairReport report = new PAKRepairReport();

            List<(long Offset, long Length)> table = ReadTable(bytes, wholeFile ? report : null, out long tableEnd, out bool tableReadable);
            long floor = wholeFile ? tableEnd : from;

            // Slots come out of each step sorted by position
            List<Slot> slots = FindSprites(bytes, floor, to, table);
            AddLostHeaders(bytes, floor, to, slots);
            if (wholeFile)
            {
                CheckTable(bytes, table, tableEnd, slots, report);
                if (tableReadable && table.Count == 0 && slots.Count > 0)
                    AddIssue(report, null, SpriteCountOffset, $"Sprite count was 0 but {slots.Count} sprites were found; table rebuilt.", true);
            }

            if (wholeFile && slots.Count == 0 && bytes.Length > 0)
                AddIssue(report, null, 0, "No sprites could be found.", false);

            int index = 0;
            foreach (Slot slot in slots)
            {
                foreach (var note in slot.Notes)
                    AddIssue(report, index, note.Offset, note.Text, true);

                if (slot.Sprite != null)
                {
                    report.Data.Sprites.Add(slot.Sprite);
                }
                else if (force)
                {
                    report.Data.Sprites.Add(CreatePlaceholder(slot.RectangleCountHint));
                    report.PlaceholderSprites.Add(report.Data.Sprites.Count - 1);
                    AddIssue(report, index, slot.Position, $"{slot.Problem}; replaced with a {PlaceholderSize}x{PlaceholderSize} placeholder.", true);
                }
                else
                {
                    AddIssue(report, index, slot.Position, $"{slot.Problem}; dropped (repair with force to insert a placeholder).", false);
                }
                index++;
            }

            if (wholeFile && slots.Count > 0)
            {
                long lastEnd = slots.Max(s => s.End);
                if (lastEnd < bytes.Length)
                    AddIssue(report, null, lastEnd, $"{bytes.Length - lastEnd} trailing bytes after the last sprite; dropped.", true);
            }

            return report;
        }

        // Reads the sprite table. Returns no entries when the table cannot be trusted.
        private static List<(long Offset, long Length)> ReadTable(byte[] bytes, PAKRepairReport? report, out long tableEnd, out bool tableReadable)
        {
            var table = new List<(long Offset, long Length)>();
            tableReadable = false;
            if (bytes.Length < SpriteTableOffset)
            {
                tableEnd = 0;
                if (report != null)
                    AddIssue(report, null, 0, $"File is {bytes.Length} bytes, too short for a PAK header.", false);
                return table;
            }

            if (report != null && !bytes.AsSpan(0, FileMagic.Length).SequenceEqual(FileMagic))
                AddIssue(report, null, 0, "File header magic was invalid; rebuilt.", true);

            int count = BitConverter.ToInt32(bytes, SpriteCountOffset);
            if (count < 0 || SpriteTableOffset + (long)count * SpriteTableEntrySize > bytes.Length)
            {
                tableEnd = SpriteTableOffset;
                if (report != null)
                    AddIssue(report, null, SpriteCountOffset, $"Sprite count {count} does not fit in the file; table rebuilt from the sprites found.", true);
                return table;
            }

            for (int i = 0; i < count; i++)
            {
                int entry = SpriteTableOffset + i * SpriteTableEntrySize;
                table.Add((BitConverter.ToInt32(bytes, entry), BitConverter.ToInt32(bytes, entry + 4)));
            }
            tableEnd = SpriteTableOffset + (long)count * SpriteTableEntrySize;
            tableReadable = true;
            return table;
        }

        private static List<Slot> FindSprites(byte[] bytes, long floor, long to, List<(long Offset, long Length)> table)
        {
            var slots = new List<Slot>();
            var tableLengths = new Dictionary<long, long>();
            foreach (var entry in table)
                tableLengths.TryAdd(entry.Offset, entry.Length);

            // Every sprite header magic in the data, in order
            var headers = new List<long>();
            for (long found = FindSpriteMagic(bytes, floor, to); found >= 0; found = FindSpriteMagic(bytes, found + 1, to))
                headers.Add(found);

            // Each found image claims its bytes and the scan resumes after it, so the work stays linear in the file size
            long previousEnd = floor;
            for (long position = floor + MinSpriteEntrySize; position < to; position++)
            {
                ImageProbe image = ProbeImage(bytes, position, to);
                if (!image.IsImage)
                    continue;

                // The nearest sprite header in front of the image: an image inside its fixed part (header, rectangle count,
                // color key) is a false match, and the sprite cannot start before it, so frames never run over another header
                long lastHeader = LastBefore(headers, position, previousEnd);
                if (lastHeader >= 0 && position < lastHeader + MinSpriteEntrySize)
                    continue;
                long earliestStart = Math.Max(previousEnd, lastHeader);

                // An incomplete image only claims up to the next sprite header, so it cannot swallow the sprites after it
                long imageClaimEnd = image.Complete ? position + image.Length : IncompleteEnd(bytes, position, to);

                Slot slot;
                if (!TryFindEntryStart(bytes, position, previousEnd, earliestStart, tableLengths, out long start, out int rectangleCount, out bool countRecovered, out bool wide))
                {
                    slot = new Slot
                    {
                        Position = position,
                        End = imageClaimEnd,
                        Problem = $"Image data found at {position} but its sprite header and frames could not be recovered"
                    };
                    slots.Add(slot);
                    previousEnd = slot.End;
                    position = slot.End - 1;
                    continue;
                }

                slot = new Slot { Position = start, RectangleCountHint = rectangleCount };
                if (wide)
                {
                    slot.End = imageClaimEnd;
                    slot.Problem = $"Uses {WideRectangleSize}-byte rectangle records, which are not supported";
                }
                else if (!image.Complete)
                {
                    slot.End = imageClaimEnd;
                    slot.Problem = "Image data is incomplete";
                }
                else
                {
                    long imageEnd = position + image.Length;

                    // Keep bytes the sprite table assigns to this entry after the image, as the reader does
                    if (tableLengths.TryGetValue(start, out long tableLength) && start + tableLength > imageEnd && start + tableLength <= to
                        && FindSpriteMagic(bytes, imageEnd, start + tableLength) < 0)
                    {
                        imageEnd = start + tableLength;
                    }

                    Sprite sprite = new Sprite();
                    for (int i = 0; i < rectangleCount; i++)
                    {
                        int r = (int)(start + SpriteHeaderSize + 4 + (long)i * RectangleSize);
                        sprite.Rectangles.Add(new SpriteRectangle
                        {
                            x = BitConverter.ToUInt16(bytes, r),
                            y = BitConverter.ToUInt16(bytes, r + 2),
                            width = BitConverter.ToUInt16(bytes, r + 4),
                            height = BitConverter.ToUInt16(bytes, r + 6),
                            pivotX = BitConverter.ToInt16(bytes, r + 8),
                            pivotY = BitConverter.ToInt16(bytes, r + 10)
                        });
                    }
                    sprite.ColorKey = ReadUInt32BigEndian(bytes, position - 4);
                    sprite.data = bytes.AsSpan((int)position, (int)(imageEnd - position)).ToArray();
                    if (image.SizeFieldDamaged)
                    {
                        // The pixel layout fixes the length; correct the BMP size field so the reader accepts the image
                        slot.Notes.Add((position + 2, $"BMP size field was {BitConverter.ToUInt32(bytes, (int)position + 2)}, image is {sprite.data.Length} bytes; rebuilt."));
                        BitConverter.TryWriteBytes(sprite.data.AsSpan(2), sprite.data.Length);
                    }

                    slot.End = imageEnd;

                    // BMPs are saved as 24bpp, so one that cannot be decoded could not be written; treat it as corrupt
                    string? decodeError = null;
                    try { SpriteImage.CheckSize(sprite.data); }
                    catch (InvalidDataException ex) { decodeError = ex.Message; }
                    if (decodeError == null && sprite.ImageFormat == SpriteImageFormat.Bmp && SpriteImage.GetBmpBitsPerPixel(sprite.data) != 24)
                    {
                        try { SpriteImage.Decode(sprite.data); }
                        catch (InvalidDataException ex) { decodeError = ex.Message; }
                    }
                    if (decodeError != null)
                    {
                        slot.Problem = $"Image data cannot be decoded ({decodeError})";
                        slot.Notes.Clear();
                        slots.Add(slot);
                        previousEnd = slot.End;
                        position = slot.End - 1;
                        continue;
                    }
                    slot.Sprite = sprite;
                    if (!LooksLikeSpriteStart(bytes, start))
                        slot.Notes.Add((start, "Sprite header magic was invalid; rebuilt."));
                    if (countRecovered)
                        slot.Notes.Add((start + SpriteHeaderSize, $"Rectangle count was {BitConverter.ToInt32(bytes, (int)start + SpriteHeaderSize)}, {rectangleCount} frames found; rebuilt."));
                }

                slots.Add(slot);
                previousEnd = slot.End;
                position = slot.End - 1;
            }

            return slots;
        }

        // The last value in sorted that is below position and at least minimum, or -1
        private static long LastBefore(List<long> sorted, long position, long minimum)
        {
            int index = sorted.BinarySearch(position);
            index = index >= 0 ? index - 1 : ~index - 1;
            return index >= 0 && sorted[index] >= minimum ? sorted[index] : -1;
        }

        private static long IncompleteEnd(byte[] bytes, long imageStart, long to)
        {
            long nextHeader = FindSpriteMagic(bytes, imageStart + 8, to);
            return nextHeader >= 0 ? nextHeader : to;
        }

        // Finds the slot whose bytes contain position, in slots sorted by position, or null
        private static Slot? FindContaining(List<Slot> sorted, long position)
        {
            int low = 0, high = sorted.Count - 1, found = -1;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                if (sorted[mid].Position <= position) { found = mid; low = mid + 1; }
                else high = mid - 1;
            }
            if (found < 0)
                return null;
            Slot slot = sorted[found];
            return position < Math.Max(slot.End, slot.Position + 1) ? slot : null;
        }

        // Works out where the sprite entry for the image at imageStart begins, by trying each possible frame count
        // and weighing the evidence: a matching rectangle count, the header magic, a sprite table entry, and the
        // entry starting where the previous sprite ended.
        private static bool TryFindEntryStart(byte[] bytes, long imageStart, long previousEnd, long earliestStart, Dictionary<long, long> tableLengths,
            out long start, out int rectangleCount, out bool countRecovered, out bool wide)
        {
            start = -1;
            rectangleCount = 0;
            countRecovered = false;
            wide = false;
            int bestScore = 0;

            long available = imageStart - MinSpriteEntrySize - earliestStart;
            if (available < 0)
                return false;

            for (int recordSize = RectangleSize; ; recordSize = WideRectangleSize)
            {
                for (long k = 0; k * recordSize <= available && k <= MaxRectangles; k++)
                {
                    long candidate = imageStart - MinSpriteEntrySize - k * recordSize;
                    bool countMatch = BitConverter.ToInt32(bytes, (int)candidate + SpriteHeaderSize) == k;
                    bool magic = LooksLikeSpriteStart(bytes, candidate);
                    bool inTable = tableLengths.ContainsKey(candidate);
                    bool contiguous = candidate == previousEnd;
                    int evidence = (magic ? 1 : 0) + (inTable ? 1 : 0) + (contiguous ? 1 : 0);

                    // A wide layout is only taken on a matching count. A 12-byte layout also on starting where the previous
                    // sprite ended, or on two other pieces of evidence; this recovers the frame count when it is damaged.
                    // A count of 0 on its own proves little (any four zero bytes match), so it needs support too.
                    bool plausible = recordSize == RectangleSize
                        ? (countMatch && (k > 0 || evidence >= 1)) || contiguous || evidence >= 2
                        : countMatch && k > 0 && evidence >= 1;
                    if (!plausible)
                        continue;

                    // Contiguity outweighs a bare count match: a last frame with zero pivots reads as a count of 0
                    int score = (countMatch ? 3 : 0) + (magic ? 2 : 0) + (inTable ? 2 : 0) + (contiguous ? 3 : 0);
                    if (score > bestScore || (score == bestScore && recordSize == RectangleSize && !wide && k > rectangleCount))
                    {
                        bestScore = score;
                        start = candidate;
                        rectangleCount = (int)k;
                        countRecovered = !countMatch;
                        wide = recordSize == WideRectangleSize;
                    }
                }

                if (recordSize == WideRectangleSize)
                    break;
            }

            return start >= 0;
        }

        // Matches the sprite table to the sprites found, walking both in file order. An entry pointing into an unmatched
        // sprite matches it; an entry pointing at nothing usable (garbage, or into a sprite already matched) is taken to
        // be the next sprite in order; an entry pointing at an unclaimed place in the file is a lost sprite there.
        // Expects slots sorted by position.
        private static void CheckTable(byte[] bytes, List<(long Offset, long Length)> table, long tableEnd, List<Slot> slots, PAKRepairReport report)
        {
            var matched = new HashSet<Slot>();
            var lost = new Dictionary<long, Slot>();
            int next = 0;

            for (int i = 0; i < table.Count; i++)
            {
                var (offset, length) = table[i];
                int entry = SpriteTableOffset + i * SpriteTableEntrySize;
                while (next < slots.Count && matched.Contains(slots[next]))
                    next++;

                Slot? containing = FindContaining(slots, offset);
                Slot? slot = containing != null && !matched.Contains(containing) ? containing : null;

                bool offsetUsable = containing == null && !lost.ContainsKey(offset)
                    && offset >= tableEnd && offset + MinSpriteEntrySize <= bytes.Length;
                if (slot == null && !offsetUsable && next < slots.Count)
                    slot = slots[next];

                if (slot != null)
                {
                    matched.Add(slot);
                    if (slot.Position != offset)
                        slot.Notes.Add((entry, $"Sprite table offset was {offset}, sprite found at {slot.Position}; rebuilt."));
                    else if (slot.Sprite != null && slot.End - slot.Position != length)
                        slot.Notes.Add((entry + 4, $"Sprite table length was {length}, sprite is {slot.End - slot.Position} bytes; rebuilt."));
                    continue;
                }

                if (!offsetUsable)
                {
                    // Nothing left to match and nowhere to put it; say so rather than lose it silently
                    AddIssue(report, null, entry, $"Sprite table entry {i} (offset {offset}, length {length}) matches no recoverable sprite.", false);
                    continue;
                }

                int hint = BitConverter.ToInt32(bytes, (int)offset + SpriteHeaderSize);
                lost[offset] = new Slot
                {
                    Position = offset,
                    End = offset,
                    RectangleCountHint = hint >= 0 && hint <= MaxRectangles ? hint : -1,
                    Problem = $"Listed in the sprite table (offset {offset}, length {length}) but no sprite could be recovered there"
                };
            }

            if (table.Count > 0)
            {
                foreach (Slot slot in slots.Where(s => s.Sprite != null && !matched.Contains(s)))
                    slot.Notes.Add((slot.Position, "Sprite was not listed in the sprite table; added."));
            }
            slots.AddRange(lost.Values);
            slots.Sort((a, b) => a.Position.CompareTo(b.Position));
        }

        // Sprite header magic with no image after it is a corrupt sprite too. Its slot covers the fixed part of the
        // entry (up to the next slot), so a sprite table offset that is slightly off still matches it.
        // Expects slots sorted by position, and keeps them sorted.
        private static void AddLostHeaders(byte[] bytes, long floor, long to, List<Slot> slots)
        {
            var headers = new List<Slot>();
            for (long found = FindSpriteMagic(bytes, floor, to); found >= 0; found = FindSpriteMagic(bytes, found + 1, to))
            {
                if (FindContaining(slots, found) != null || (headers.Count > 0 && found < headers[^1].End))
                    continue;

                int hint = found + MinSpriteEntrySize <= bytes.Length ? BitConverter.ToInt32(bytes, (int)found + SpriteHeaderSize) : -1;
                long end = Math.Min(found + MinSpriteEntrySize, to);
                Slot? following = slots.FirstOrDefault(s => s.Position > found);
                if (following != null)
                    end = Math.Min(end, following.Position);
                headers.Add(new Slot
                {
                    Position = found,
                    End = end,
                    RectangleCountHint = hint >= 0 && hint <= MaxRectangles ? hint : -1,
                    Problem = "Sprite header found but its image data is missing or damaged"
                });
            }

            slots.AddRange(headers);
            slots.Sort((a, b) => a.Position.CompareTo(b.Position));
        }

        private static ImageProbe ProbeImage(byte[] bytes, long position, long to)
        {
            ImageProbe none = new ImageProbe();
            if (position + 8 > to)
                return none;

            // PNG: signature, IHDR first, then walk the chunks to IEND
            if (bytes[position] == 0x89 && bytes[position + 1] == 0x50)
            {
                if (!bytes.AsSpan((int)position, 8).SequenceEqual(PngSignature))
                    return none;
                long chunk = position + 8;
                if (chunk + 8 > to || ReadUInt32BigEndian(bytes, chunk) != 13 || !bytes.AsSpan((int)chunk + 4, 4).SequenceEqual("IHDR"u8))
                    return none;

                while (chunk + 12 <= to)
                {
                    long next = chunk + 12 + ReadUInt32BigEndian(bytes, chunk);
                    if (bytes.AsSpan((int)chunk + 4, 4).SequenceEqual("IEND"u8))
                    {
                        return next <= to
                            ? new ImageProbe { IsImage = true, Complete = true, Length = next - position }
                            : new ImageProbe { IsImage = true, Length = to - position };
                    }
                    chunk = next;
                }
                return new ImageProbe { IsImage = true, Length = to - position };
            }

            // BMP: file header plus a believable DIB header, then the length from the pixel layout
            if (bytes[position] == 0x42 && bytes[position + 1] == 0x4D)
            {
                if (position + 30 > to)
                    return none;
                int p = (int)position;
                long declaredSize = BitConverter.ToUInt32(bytes, p + 2);
                long pixelOffset = BitConverter.ToUInt32(bytes, p + 10);
                int dibSize = BitConverter.ToInt32(bytes, p + 14);
                long width, height;
                int planes, bitsPerPixel, compression = 0;
                if (dibSize == 12)
                {
                    width = BitConverter.ToUInt16(bytes, p + 18);
                    height = BitConverter.ToInt16(bytes, p + 20);
                    planes = BitConverter.ToUInt16(bytes, p + 22);
                    bitsPerPixel = BitConverter.ToUInt16(bytes, p + 24);
                }
                else if (dibSize == 40 || dibSize == 52 || dibSize == 56 || dibSize == 108 || dibSize == 124)
                {
                    if (position + 34 > to)
                        return none;
                    width = BitConverter.ToInt32(bytes, p + 18);
                    height = BitConverter.ToInt32(bytes, p + 22);
                    planes = BitConverter.ToUInt16(bytes, p + 26);
                    bitsPerPixel = BitConverter.ToUInt16(bytes, p + 28);
                    compression = BitConverter.ToInt32(bytes, p + 30);
                }
                else
                {
                    return none;
                }

                // Height must be positive: the reader rejects top-down bitmaps
                bool headerValid = planes == 1
                    && (bitsPerPixel == 1 || bitsPerPixel == 4 || bitsPerPixel == 8 || bitsPerPixel == 16 || bitsPerPixel == 24 || bitsPerPixel == 32)
                    && width > 0 && width <= 32768 && height > 0 && height <= 32768
                    && pixelOffset >= 14 + dibSize && pixelOffset <= 14 + dibSize + 1024 + 16;
                if (!headerValid)
                    return none;

                long length;
                if (compression == 0 || compression == 3)
                {
                    long rowSize = (bitsPerPixel * width + 31) / 32 * 4;
                    length = pixelOffset + rowSize * height;

                    // Keep extra bytes a larger size field claims, unless that size runs past the data: then the
                    // size field is damaged and the pixel layout decides
                    if (declaredSize > length)
                    {
                        if (position + declaredSize <= to)
                            length = declaredSize;
                        else if (position + length <= to)
                            return new ImageProbe { IsImage = true, Complete = true, Length = length, SizeFieldDamaged = true };
                    }
                }
                else if (declaredSize > pixelOffset)
                {
                    length = declaredSize;
                }
                else
                {
                    return none;
                }

                return position + length <= to
                    ? new ImageProbe { IsImage = true, Complete = true, Length = length }
                    : new ImageProbe { IsImage = true, Length = to - position };
            }

            return none;
        }

        private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        private static byte[]? placeholderImage;

        // 32x32 24-bit BMP, an 8-pixel checkerboard of magenta (the usual transparency key) and black
        private static Sprite CreatePlaceholder(int rectangleCount)
        {
            if (placeholderImage == null)
            {
                const int rowSize = PlaceholderSize * 3;
                byte[] bmp = new byte[54 + rowSize * PlaceholderSize];
                bmp[0] = 0x42; bmp[1] = 0x4D;
                BitConverter.TryWriteBytes(bmp.AsSpan(2), bmp.Length);
                BitConverter.TryWriteBytes(bmp.AsSpan(10), 54);
                BitConverter.TryWriteBytes(bmp.AsSpan(14), 40);
                BitConverter.TryWriteBytes(bmp.AsSpan(18), PlaceholderSize);
                BitConverter.TryWriteBytes(bmp.AsSpan(22), PlaceholderSize);
                BitConverter.TryWriteBytes(bmp.AsSpan(26), (short)1);
                BitConverter.TryWriteBytes(bmp.AsSpan(28), (short)24);
                for (int y = 0; y < PlaceholderSize; y++)
                {
                    for (int x = 0; x < PlaceholderSize; x++)
                    {
                        if (((x / 8) + (y / 8)) % 2 != 0)
                            continue;
                        int pixel = 54 + y * rowSize + x * 3;
                        bmp[pixel] = 0xFF;      // B
                        bmp[pixel + 2] = 0xFF;  // R
                    }
                }
                placeholderImage = bmp;
            }

            // Keep the frame count so frame indices stay valid; every frame shows the whole placeholder
            Sprite sprite = new Sprite { data = (byte[])placeholderImage.Clone() };
            for (int i = 0; i < Math.Max(rectangleCount, 1); i++)
                sprite.Rectangles.Add(new SpriteRectangle { width = PlaceholderSize, height = PlaceholderSize });
            return sprite;
        }

        private static bool LooksLikeSpriteStart(byte[] bytes, long position)
        {
            return position >= 0
                && position + SpriteMagic.Length <= bytes.Length
                && bytes.AsSpan((int)position, SpriteMagic.Length).SequenceEqual(SpriteMagic);
        }

        private static long FindSpriteMagic(byte[] bytes, long from, long to)
        {
            if (from < 0 || from >= to)
                return -1;
            int found = bytes.AsSpan((int)from, (int)(to - from)).IndexOf(SpriteMagic);
            return found < 0 ? -1 : from + found;
        }

        private static uint ReadUInt32BigEndian(byte[] bytes, long position)
        {
            return System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan((int)position, 4));
        }

        private static void AddIssue(PAKRepairReport report, int? spriteIndex, long offset, string description, bool repaired)
        {
            report.Issues.Add(new PAKRepairIssue
            {
                SpriteIndex = spriteIndex,
                Offset = offset,
                Description = description,
                Repaired = repaired
            });
        }
    }
}
