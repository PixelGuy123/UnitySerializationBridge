using System;

namespace UnitySerializationBridge.Utils;

internal static class MathUtils
{
    /// <summary>
    /// Make a slowly curved slope (similar to a logarithmic function) after certain threshold 'l' is passed. 
    /// </summary>
    /// <param name="x">The current x of the function.</param>
    /// <param name="l">The limit before turning into a logarithmic function.</param>
    /// <param name="k">The curving of the slope.</param>
    /// <returns></returns>
    public static double CalculateCurve(double x, double l, double k = 1000.0)
    {
        if (k < 100.0)
            throw new ArgumentOutOfRangeException($"\'k\' is below 100 ({k}).");
        // Linear segment (x = y)
        if (x <= l)
        {
            return x;
        }

        // Logarithmic segment
        return l + k * Math.Log(1.0 + (x - l) / k);
    }
}