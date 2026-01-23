using System;
using System.Globalization;
using System.Numerics;

namespace ValidatorApi.Services;

public static class HammingDistance
{
    public static int BetweenHex64(string aHex, string bHex)
    {
        if (aHex is null)
        {
            throw new ArgumentNullException(nameof(aHex));
        }
        if (bHex is null)
        {
            throw new ArgumentNullException(nameof(bHex));
        }
        if (aHex.Length != 16 || bHex.Length != 16)
        {
            throw new ArgumentException("Expected 16-character hex strings.");
        }

        var a = ulong.Parse(aHex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        var b = ulong.Parse(bHex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        return BitOperations.PopCount(a ^ b);
    }
}