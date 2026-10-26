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