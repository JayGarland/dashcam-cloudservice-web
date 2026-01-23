const DST_WIDTH = 9;
const DST_HEIGHT = 8;

function clamp(value: number, min: number, max: number): number {
  if (value < min) {
    return min;
  }
  if (value > max) {
    return max;
  }
  return value;
}

function sampleBilinearRgb(
  rgba: Uint8ClampedArray,
  srcW: number,
  srcH: number,
  sx: number,
  sy: number
): [number, number, number] {
  const x0 = Math.floor(sx);
  const y0 = Math.floor(sy);
  const x1 = x0 + 1;
  const y1 = y0 + 1;

  const tx = sx - x0;
  const ty = sy - y0;

  const x0c = clamp(x0, 0, srcW - 1);
  const x1c = clamp(x1, 0, srcW - 1);
  const y0c = clamp(y0, 0, srcH - 1);
  const y1c = clamp(y1, 0, srcH - 1);

  const idx00 = (y0c * srcW + x0c) * 4;
  const idx10 = (y0c * srcW + x1c) * 4;
  const idx01 = (y1c * srcW + x0c) * 4;
  const idx11 = (y1c * srcW + x1c) * 4;

  const r00 = rgba[idx00];
  const g00 = rgba[idx00 + 1];
  const b00 = rgba[idx00 + 2];

  const r10 = rgba[idx10];
  const g10 = rgba[idx10 + 1];
  const b10 = rgba[idx10 + 2];

  const r01 = rgba[idx01];
  const g01 = rgba[idx01 + 1];
  const b01 = rgba[idx01 + 2];

  const r11 = rgba[idx11];
  const g11 = rgba[idx11 + 1];
  const b11 = rgba[idx11 + 2];

  const r0 = r00 + (r10 - r00) * tx;
  const r1 = r01 + (r11 - r01) * tx;
  const g0 = g00 + (g10 - g00) * tx;
  const g1 = g01 + (g11 - g01) * tx;
  const b0 = b00 + (b10 - b00) * tx;
  const b1 = b01 + (b11 - b01) * tx;

  const r = r0 + (r1 - r0) * ty;
  const g = g0 + (g1 - g0) * ty;
  const b = b0 + (b1 - b0) * ty;

  return [r, g, b];
}

export function dhash64FromRgba(
  rgba: Uint8ClampedArray,
  srcW: number,
  srcH: number
): bigint {
  const luma = new Array<number>(DST_WIDTH * DST_HEIGHT);
  for (let dy = 0; dy < DST_HEIGHT; dy += 1) {
    for (let dx = 0; dx < DST_WIDTH; dx += 1) {
      const sx = (dx + 0.5) * (srcW / DST_WIDTH) - 0.5;
      const sy = (dy + 0.5) * (srcH / DST_HEIGHT) - 0.5;
      const [r, g, b] = sampleBilinearRgb(rgba, srcW, srcH, sx, sy);
      const y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
      luma[dy * DST_WIDTH + dx] = y;
    }
  }

  let value = 0n;
  for (let dy = 0; dy < 8; dy += 1) {
    const rowStart = dy * DST_WIDTH;
    for (let dx = 0; dx < 8; dx += 1) {
      const left = luma[rowStart + dx];
      const right = luma[rowStart + dx + 1];
      const bit = left > right ? 1n : 0n;
      const bitIndex = BigInt(dy * 8 + dx);
      value |= bit << bitIndex;
    }
  }

  return value;
}

export function dhash64HexFromRgba(
  rgba: Uint8ClampedArray,
  srcW: number,
  srcH: number
): string {
  const value = dhash64FromRgba(rgba, srcW, srcH);
  return value.toString(16).padStart(16, "0");
}

export function hammingDistance64(aHex: string, bHex: string): number {
  if (aHex.length !== 16 || bHex.length !== 16) {
    throw new Error("Expected 16-character hex strings.");
  }

  const a = BigInt(`0x${aHex}`);
  const b = BigInt(`0x${bHex}`);
  let x = a ^ b;
  let count = 0;
  while (x > 0n) {
    count += Number(x & 1n);
    x >>= 1n;
  }
  return count;
}