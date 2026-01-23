import { describe, expect, it } from "vitest";
import { dhash64HexFromRgba, hammingDistance64 } from "../hash/dhash64";

const EXPECTED_HEX = "1a1830f0624cf0c0";

function buildFixtureRgba(width: number, height: number): Uint8ClampedArray {
  const rgba = new Uint8ClampedArray(width * height * 4);
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const r = (x * 17 + y * 31) % 256;
      const g = (x * 13 + y * 7 + 50) % 256;
      const b = (x * 3 + y * 29 + 90) % 256;
      const idx = (y * width + x) * 4;
      rgba[idx] = r;
      rgba[idx + 1] = g;
      rgba[idx + 2] = b;
      rgba[idx + 3] = 255;
    }
  }
  return rgba;
}

function buildGrayscaleRgba(rows: number[][]): Uint8ClampedArray {
  const height = rows.length;
  const width = rows[0]?.length ?? 0;
  const rgba = new Uint8ClampedArray(width * height * 4);
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const v = rows[y][x];
      const idx = (y * width + x) * 4;
      rgba[idx] = v;
      rgba[idx + 1] = v;
      rgba[idx + 2] = v;
      rgba[idx + 3] = 255;
    }
  }
  return rgba;
}

describe("dhash64", () => {
  it("should produce expected hex for a deterministic synthetic RGBA fixture", () => {
    const width = 18;
    const height = 16;
    const rgba = buildFixtureRgba(width, height);
    const hashHex = dhash64HexFromRgba(rgba, width, height);
    expect(hashHex).toBe(EXPECTED_HEX);
  });

  it("bit packing should be LSB-first row-major", () => {
    const row0 = [255, 0, 10, 20, 30, 40, 50, 60, 70];
    const rowN = [0, 10, 20, 30, 40, 50, 60, 70, 80];
    const rows = [row0, rowN, rowN, rowN, rowN, rowN, rowN, rowN];
    const rgba = buildGrayscaleRgba(rows);
    const hashHex = dhash64HexFromRgba(rgba, 9, 8);
    expect(hashHex).toBe("0000000000000001");
  });
});

describe("hammingDistance64", () => {
  it("returns 0 for identical hashes", () => {
    expect(hammingDistance64("0000000000000000", "0000000000000000")).toBe(0);
  });

  it("counts differing bits", () => {
    expect(hammingDistance64("0000000000000000", "0000000000000001")).toBe(1);
    expect(hammingDistance64("ffffffffffffffff", "0000000000000000")).toBe(64);
  });
});