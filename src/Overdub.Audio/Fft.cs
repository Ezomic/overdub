using System.Numerics;

namespace Overdub.Audio;

public static class Fft
{
    public static void Transform(Complex[] data)
    {
        var n = data.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (data[i], data[j]) = (data[j], data[i]);
            }
        }

        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            var step = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (var start = 0; start < n; start += length)
            {
                var twiddle = Complex.One;
                for (var k = 0; k < length / 2; k++)
                {
                    var even = data[start + k];
                    var odd = data[start + k + (length / 2)] * twiddle;
                    data[start + k] = even + odd;
                    data[start + k + (length / 2)] = even - odd;
                    twiddle *= step;
                }
            }
        }
    }
}
