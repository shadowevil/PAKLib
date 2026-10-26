# Example
```C#
    const string inFilePath = @"SPRITES\New-Dialog.pak";
    const string outFilePath = @"SPRITES\New-Dialog-new.pak";

    PAK pak = PAK.ReadFromFile(inFilePath);

    int i = 0;
    foreach (var sprite in pak.Data.Sprites)
    {
        using (MemoryStream ms = new MemoryStream(sprite.data))
        using (Bitmap bmp = new Bitmap(Image.FromStream(ms)))
        {
            bmp.Save($"{i++}.bmp", System.Drawing.Imaging.ImageFormat.Bmp);
        }
    }

    if (pak.Data == null)
    {
        Console.WriteLine("Failed to read PAK data.");
        return;
    }

    Console.WriteLine($"Read {pak.Data.Sprites.Count} sprites from PAK file.");

    pak.Data.Write(outFilePath);
    Console.WriteLine($"PAK data written to {outFilePath} successfully.");
```

# Repairing damaged files
Repair salvages the sprites from a damaged file and rebuilds it. It finds each sprite by its image data (BMP or PNG), recovers the frames (rectangles) and color key in front of it, and writes a fresh file from those sprites, so the file header, sprite table and sprite headers are all regenerated.

`PAK.RepairFile` repairs a `.pak` (or `.epak`, with its key) and writes the rebuilt file to a new path; the output is encrypted when its extension is `.epak`. It returns a report listing every issue found.
```C#
    PAKRepairReport report = PAK.RepairFile(@"C:\SPRITES\MHelm3.pak", @"C:\SPRITES\MHelm3-repaired.pak");
    foreach (PAKRepairIssue issue in report.Issues)
        Console.WriteLine(issue);
    Console.WriteLine(report.Success ? "All sprites recovered or replaced." : "Some sprites were dropped.");
    Console.WriteLine($"Placeholders at: {string.Join(", ", report.PlaceholderSprites)}");
```
`report.Success` is true when nothing was dropped; with `force`, corrupt sprites count as handled, so check `report.PlaceholderSprites` too. The output path must differ from the input path.
`PAKRepair.Repair(bytes)` does the same on decrypted bytes in memory and returns the salvaged sprites in `report.Data`. `PAKRepair.Repair(bytes, start, end)` salvages only the sprites in one section of the data.

**Corrupt sprites.** A sprite whose image is missing, damaged or cut off, or that uses 16-byte rectangle records (not supported), is flagged in the report and dropped. Pass `force: true` to put a 32x32 placeholder in its place instead (a magenta and black checkerboard, keeping the sprite's frame count where it is known), so the sprite indices of the rebuilt file match the original. `report.PlaceholderSprites` lists where placeholders went.
```C#
    PAKRepairReport report = PAK.RepairFile(inPath, outPath, force: true);
```

How sprites are recovered, so damage to one part is covered by another:
- **Image data** is found by its BMP or PNG signature, and its length is read from the image itself (BMP header, PNG chunks), not from the sprite table.
- **Sprite start and frame count** are worked out from the rectangle count, the sprite header magic, the sprite table, and the end of the previous sprite. If the rectangle count is damaged, the frames are recovered from the space between the sprite header and the image.
- **Sprite table** entries are matched to the sprites found in file order. A sprite the table lists but that cannot be recovered is a corrupt sprite; a sprite the table misses is added.
- A sprite never starts before the nearest `<Sprite File Header>` in front of its image, so recovered frames cannot run over another sprite. A sprite with nothing left to anchor it (header, image and table entry all gone) is reported but cannot be placed.
- Header padding is not kept (the rebuilt headers are zero-padded), and color keys follow the usual rule (alpha other than `FF` reads as unset). Trailing bytes after the last sprite are dropped.

# Nuget Package
https://www.nuget.org/packages/PAKLib

# PAK File Format Specification

## 1. File Header *`20 bytes total`*
| Offset | Field   | Type     | Size (bytes) | Description                 |
| ------ | ------- | -------- | ---- | --------------------------- |
| 0      | Magic   | UTF-8    | 17   | `"<Pak file header>"`       |
| 17     | Padding | byte[3]  | 3    | `0x00, 0x00, 0x00`          |

## 2. Sprite Count  *`4 bytes total`*
| Offset | Field       | Type   | Size (bytes) | Description                       |
| ------ | ----------- | ------ | ---- | --------------------------------- |
| 20     | Count       | Int32  | 4    | Number of sprite entries in file  |

## 3. Sprite Table  *`8*n bytes total`*
Starts at offset 24, repeats `Count` times (8 bytes each):  
| Offset (rel) | Field   | Type   | Size (bytes) | Description                                        |
| ------------ | ------- | ------ | ------------- | -------------------------------------------------- |
| `24+n*4`       | Offset  | Int32  | 4             | Byte position of this sprite entry from file start |
| `24+n*4+4`       | Length  | Int32  | 4             | Total bytes of this sprite entry                   |

## 4. Sprite Entry  *`108+(12*n) bytes total`*
At each table `Offset`, layout is:

### 4.1 Sprite Header  *`100 bytes total`*
| Offset (rel) | Field   | Type      | Size (bytes) | Description                |
| ------------ | ------- | --------- | ---- | -------------------------- |
| 0            | Magic   | UTF-8     | 20   | `"<Sprite File Header>"`   |
| 20           | Padding | byte[80]  | 80   | Reserved                   |

### 4.2 Rectangle Data  *`4+(12*n) bytes total`*
| Offset (rel) | Field | Type   | Size (bytes) | Description                                |
| ------------ | ----- | ------ | ------------------------ | ------------------------------------------ |
| 100          | Count | Int32  | 4                        | Number of rectangles                       |
| 104          | Data  | Records| Count × 12 bytes         | Each record: x(Int16), y(Int16), width(Int16), height(Int16), pivotX(Int16), pivotY(Int16) |

### 4.3 Transparency Color Key  *`4 bytes total`*
| Offset (rel) | Field    | Type   | Size (bytes) | Description        |
| ------------ | -------- | ------ | ---- | ------------------ |
| ... | ColorKey | byte[4] | 4    | Authored transparency color key as `A RR GG BB`. `00 00 00 00` means none was authored and the key is derived from the top-left pixel of the sprite sheet (typical for PNG). `A` only marks the key as set: `FF` means set, so black is `FF 00 00 00`. Any other alpha is read as unset and written back as `00 00 00 00`; this includes original client files, which store `00 RR GG BB` (the 2.20 client ignores this field and always uses the top-left pixel). Exposed as `Sprite.ColorKey` (`0xFFRRGGBB`, or `null` when unset). |

### 4.4 Image Data  *`variable length`*
| Offset (rel) | Field | Type    | Size (bytes) | Description                |
| ------------ | ----- | ------- | ------------ | -------------------------- |
| ... | Data  | byte[]  | ... 				  | Raw sprite image bytes     |

---

**Notes:**  
- All multi‑byte integers are little‑endian.  
- `Length` in the sprite table covers from the sprite’s `Offset` through the end of its image data.  
- Rectangle record size is fixed at 12 bytes per rectangle.  