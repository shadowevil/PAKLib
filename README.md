# Example
```C#
    const string inFilePath = @"C:\SPRITES\New-Dialog.pak";     // paths must be absolute
    const string outFilePath = @"C:\SPRITES\New-Dialog-new.pak";

    PAK pak = PAK.ReadFromFile(inFilePath);
    Console.WriteLine($"Read {pak.Data!.Sprites.Count} sprites from PAK file.");

    int i = 0;
    foreach (Sprite sprite in pak.Data.Sprites)
    {
        string extension = sprite.ImageFormat == SpriteImageFormat.Png ? "png" : "bmp";
        File.WriteAllBytes($@"C:\SPRITES\out\{i++}.{extension}", sprite.data);
    }

    PAK.SaveToFile(pak, outFilePath);
    Console.WriteLine($"PAK data written to {outFilePath} successfully.");
```
`PAKData.Read(bytes)` and `PAKData.ToBytes()` do the same in memory.

# Image formats
Each sprite's image is a BMP or a PNG (`sprite.ImageFormat`).
- **Images are capped at 4096x4096** (`SpriteImage.MaxDimension`; 4K UHD fits). Reading, saving and converting throw `InvalidDataException` for a larger image, and repair flags it as a corrupt sprite.
- **BMPs are always saved as 24bpp.** When a PAK is written, any BMP of another bit depth (1, 4, 8, 16 or 32 bpp, uncompressed, bitfields or RLE) is converted to 24bpp on the way out; 32bpp alpha is dropped. The sprite in memory is not changed, and 24bpp BMPs and PNGs are written unchanged.
- **Converting between BMP and PNG:** `sprite.ConvertToBmp()`, `sprite.ConvertToPng()`, or `pakData.ConvertImages(SpriteImageFormat.Png)` for every sprite.
  - BMP to PNG: pixels matching the color key become transparent. A BMP without a color key uses its top-left pixel, as the game client does, and that color becomes the sprite's color key so converting back restores it.
  - PNG to BMP: transparent pixels (alpha below 128) are filled with the color key; a sprite without one gets magenta (`FF 00 FF`) as its key.
  - PNGs are written as standard 8-bit RGBA, non-interlaced. Any PNG color type, bit depth or interlacing can be read.
- `SpriteImage.ToBmp24(bytes, colorKey)` and `SpriteImage.ToPng(bytes, colorKey)` convert image bytes directly.

# Version 2.0.1
The 2.0.0 package on NuGet was built from stale output and contains the 1.1 code. Use 2.0.1, which has the 2.0 changes below.

# Version 2.0 changes
Breaking changes:
- Encryption (`.epak`, `Encryption` class, the `encryption_key` parameters) is removed. `PAK.ReadFromFile` on an `.epak` throws `NotSupportedException`.
- `SpriteRectangle.x`, `y`, `width` and `height` are `UInt16` (were `Int16`): negative values no longer fit, and arithmetic on them is unsigned. `pivotX` and `pivotY` stay `Int16`.
- Images larger than 4096x4096 are rejected on read and save.
- `PAKData.Write(path)` and `PAK.SaveToFile(pak, path)` no longer take a key. Saving throws `InvalidDataException` for a BMP that is not 24bpp and cannot be decoded for conversion.

Additions:
- The 4 bytes before each image are the transparency color key (`Sprite.ColorKey`).
- BMPs are saved as 24bpp; BMP and PNG conversion added.
- `PAK.RepairFile` / `PAKRepair` salvage damaged files.
- `PAKData.ToBytes()` added.

# Repairing damaged files
Repair salvages the sprites from a damaged file and rebuilds it. It finds each sprite by its image data (BMP or PNG), recovers the frames (rectangles) and color key in front of it, and writes a fresh file from those sprites, so the file header, sprite table and sprite headers are all regenerated.

`PAK.RepairFile` repairs a `.pak` and writes the rebuilt file to a new path. It returns a report listing every issue found.
```C#
    PAKRepairReport report = PAK.RepairFile(@"C:\SPRITES\MHelm3.pak", @"C:\SPRITES\MHelm3-repaired.pak");
    foreach (PAKRepairIssue issue in report.Issues)
        Console.WriteLine(issue);
    Console.WriteLine(report.Success ? "All sprites recovered or replaced." : "Some sprites were dropped.");
    Console.WriteLine($"Placeholders at: {string.Join(", ", report.PlaceholderSprites)}");
```
`report.Success` is true when nothing was dropped; with `force`, corrupt sprites count as handled, so check `report.PlaceholderSprites` too. The output path must differ from the input path.
`PAKRepair.Repair(bytes)` does the same on bytes in memory and returns the salvaged sprites in `report.Data`. `PAKRepair.Repair(bytes, start, end)` salvages only the sprites in one section of the data.

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
| 104          | Data  | Records| Count × 12 bytes         | Each record: x(UInt16), y(UInt16), width(UInt16), height(UInt16), pivotX(Int16), pivotY(Int16) |

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