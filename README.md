# PAK File Reader Documentation
This document provides a structured breakdown of the PAK file format.

## 1. PAK File Structure Overview
| Offset (byte) | Size (bytes) | Type | Description                                                  |
|--------|-----------|-----------------|--------------------------------------------------------------|
| 0 | 17 | String | "&lt;Pak file header&gt;" Identification of the file type to be read |
| 17 | 3 | Padding *(0x00 null bytes)* | Padding |
| 20 | 4 | Int | Sprite count (N)                                             |
| 24 | 8*N | (Int * 2) * N | Sprite entry offsets and endsets stored as consecutive values. |
| ...    | variable  | Sprite data     | Sequence of N Sprite structures                              |

## 2. Sprite Entry Structure
Each Sprite entry starts with a SpriteHeader.

| Offset (byte) | Size (bytes) | Type | Description                                                  |
|--------|------------|-------------|---------------------------------------------------------------|
| 0 | 20 | String | "&lt;Sprite File Header&gt;" Entry Identity   |
| 20 | 80 | Padding *(0x00 null bytes)* | Padding |
| 100 | 4 | Int | Rectangle count (R) |
| 104   | 12*R bytes | 6 * Short | Rectangle data (x, y, w, h, pivotX, pivotY) |
| ... | variable | Image Data | Should read the bmp headers and read the image appropriately |

### Rectangle Entry Structure

| Field   | Type  | Description         |
|---------|-------|---------------------|
| x       | Short | X-coordinate        |
| y       | Short | Y-coordinate        |
| width   | Short | Width of rectangle  |
| height  | Short | Height of rectangle |
| pivotX  | Short | X pivot offset      |
| pivotY  | Short | Y pivot offset      |
