using System;
using ValidatorApi.Services;
using Xunit;

namespace ValidatorApi.Tests;

public class DHash64Tests
{
    private const string ExpectedHex = "1a1830f0624cf0c0";

    [Fact]
    public void FromRgba_ShouldMatchExpectedHex_ForDeterministicFixture()
    {
        const int width = 18;
        const int height = 16;
        var rgba = BuildFixtureRgba(width, height);

        var value = DHash64.FromRgba(rgba, width, height);
        var hex = DHash64.ToHex(value);

        Assert.Equal(ExpectedHex, hex);
    }

    [Fact]
    public void FromRgba_BitPackingIsLsbFirstRowMajor()
    {
        var row0 = new byte[] { 255, 0, 10, 20, 30, 40, 50, 60, 70 };
        var rowN = new byte[] { 0, 10, 20, 30, 40, 50, 60, 70, 80 };
        var rows = new[] { row0, rowN, rowN, rowN, rowN, rowN, rowN, rowN };
        var rgba = BuildGrayscaleRgba(rows);

        var value = DHash64.FromRgba(rgba, 9, 8);
        var hex = DHash64.ToHex(value);

        Assert.Equal("0000000000000001", hex);
    }

    [Fact]
    public void HammingDistance_CountsBitsCorrectly()
    {
        Assert.Equal(0, HammingDistance.BetweenHex64("0000000000000000", "0000000000000000"));
        Assert.Equal(1, HammingDistance.BetweenHex64("0000000000000000", "0000000000000001"));
        Assert.Equal(64, HammingDistance.BetweenHex64("ffffffffffffffff", "0000000000000000"));
    }

    private static byte[] BuildFixtureRgba(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y += 1)
        {
            for (var x = 0; x < width; x += 1)
            {
                var r = (byte)((x * 17 + y * 31) % 256);
                var g = (byte)((x * 13 + y * 7 + 50) % 256);
                var b = (byte)((x * 3 + y * 29 + 90) % 256);
                var idx = (y * width + x) * 4;
                rgba[idx] = r;
                rgba[idx + 1] = g;
                rgba[idx + 2] = b;
                rgba[idx + 3] = 255;
            }
        }
        return rgba;
    }

    private static byte[] BuildGrayscaleRgba(byte[][] rows)
    {
        var height = rows.Length;
        var width = rows[0].Length;
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y += 1)
        {
            for (var x = 0; x < width; x += 1)
            {
                var v = rows[y][x];
                var idx = (y * width + x) * 4;
                rgba[idx] = v;
                rgba[idx + 1] = v;
                rgba[idx + 2] = v;
                rgba[idx + 3] = 255;
            }
        }
        return rgba;
    }
}