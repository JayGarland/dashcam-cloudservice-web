using System;

namespace ValidatorApi.Services;

public static class DHash64
{
    private const int DstWidth = 9;
    private const int DstHeight = 8;

    public static ulong FromRgba(byte[] rgba, int srcW, int srcH)
    {
        if (rgba is null)
        {
            throw new ArgumentNullException(nameof(rgba));
        }
        if (rgba.Length != srcW * srcH * 4)
        {
            throw new ArgumentException("RGBA length must be srcW * srcH * 4.", nameof(rgba));
        }
        if (srcW <= 0 || srcH <= 0)
        {
            throw new ArgumentOutOfRangeException("Source dimensions must be positive.");
        }

        var luma = new double[DstWidth * DstHeight];
        for (var dy = 0; dy < DstHeight; dy += 1)
        {
            for (var dx = 0; dx < DstWidth; dx += 1)
            {
                var sx = (dx + 0.5) * (srcW / (double)DstWidth) - 0.5;
                var sy = (dy + 0.5) * (srcH / (double)DstHeight) - 0.5;
                var (r, g, b) = SampleBilinearRgb(rgba, srcW, srcH, sx, sy);
                var y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                luma[dy * DstWidth + dx] = y;
            }
        }

        ulong value = 0;
        for (var dy = 0; dy < 8; dy += 1)
        {
            var rowStart = dy * DstWidth;
            for (var dx = 0; dx < 8; dx += 1)
            {
                var left = luma[rowStart + dx];
                var right = luma[rowStart + dx + 1];
                if (left > right)
                {
                    var bitIndex = dy * 8 + dx;
                    value |= 1UL << bitIndex;
                }
            }
        }

        return value;
    }

    public static string ToHex(ulong value)
    {
        return value.ToString("x16");
    }

    private static (double r, double g, double b) SampleBilinearRgb(
        byte[] rgba,
        int srcW,
        int srcH,
        double sx,
        double sy)
    {
        var x0 = (int)Math.Floor(sx);
        var y0 = (int)Math.Floor(sy);
        var x1 = x0 + 1;
        var y1 = y0 + 1;

        var tx = sx - x0;
        var ty = sy - y0;

        var x0c = Clamp(x0, 0, srcW - 1);
        var x1c = Clamp(x1, 0, srcW - 1);
        var y0c = Clamp(y0, 0, srcH - 1);
        var y1c = Clamp(y1, 0, srcH - 1);

        var (r00, g00, b00) = SampleRgb(rgba, srcW, x0c, y0c);
        var (r10, g10, b10) = SampleRgb(rgba, srcW, x1c, y0c);
        var (r01, g01, b01) = SampleRgb(rgba, srcW, x0c, y1c);
        var (r11, g11, b11) = SampleRgb(rgba, srcW, x1c, y1c);

        var r0 = r00 + (r10 - r00) * tx;
        var r1 = r01 + (r11 - r01) * tx;
        var g0 = g00 + (g10 - g00) * tx;
        var g1 = g01 + (g11 - g01) * tx;
        var b0 = b00 + (b10 - b00) * tx;
        var b1 = b01 + (b11 - b01) * tx;

        var r = r0 + (r1 - r0) * ty;
        var g = g0 + (g1 - g0) * ty;
        var b = b0 + (b1 - b0) * ty;

        return (r, g, b);
    }

    private static (double r, double g, double b) SampleRgb(byte[] rgba, int srcW, int x, int y)
    {
        var idx = (y * srcW + x) * 4;
        return (rgba[idx], rgba[idx + 1], rgba[idx + 2]);
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }
        if (value > max)
        {
            return max;
        }
        return value;
    }
}