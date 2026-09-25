using System;

namespace ColonySim.Simulation.Generation
{
    /// <summary>Seeded, allocation-free 2D simplex samples. No Unity dependency.</summary>
    public sealed class SimplexNoise
    {
        private readonly int[] _permutation = new int[512];

        public SimplexNoise(int seed)
        {
            for (int i = 0; i < 256; i++) _permutation[i] = i;
            uint state = unchecked((uint)seed) ^ 0x9E3779B9u;
            for (int i = 255; i > 0; i--)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                int j = (int)(state % (uint)(i + 1));
                int temp = _permutation[i];
                _permutation[i] = _permutation[j];
                _permutation[j] = temp;
            }
            for (int i = 0; i < 256; i++) _permutation[i + 256] = _permutation[i];
        }

        public float Sample(float x, float y)
        {
            const float skew = 0.366025403784f, unskew = 0.211324865405f;
            float s = (x + y) * skew;
            int i = (int)Math.Floor(x + s), j = (int)Math.Floor(y + s);
            float t = (i + j) * unskew;
            float x0 = x - (i - t), y0 = y - (j - t);
            int i1 = x0 > y0 ? 1 : 0, j1 = 1 - i1;
            int ii = i & 255, jj = j & 255;
            return 70f * (Corner(_permutation[ii + _permutation[jj]], x0, y0)
                + Corner(_permutation[ii + i1 + _permutation[jj + j1]], x0 - i1 + unskew, y0 - j1 + unskew)
                + Corner(_permutation[ii + 1 + _permutation[jj + 1]], x0 - 1 + 2 * unskew, y0 - 1 + 2 * unskew));
        }

        private static float Corner(int hash, float x, float y)
        {
            float t = 0.5f - x * x - y * y;
            if (t <= 0) return 0;
            float dot;
            switch (hash % 12)
            {
                case 0: dot = x + y; break;
                case 1: dot = -x + y; break;
                case 2: dot = x - y; break;
                case 3: dot = -x - y; break;
                case 4: case 6: dot = x; break;
                case 5: case 7: dot = -x; break;
                case 8: case 10: dot = y; break;
                default: dot = -y; break;
            }
            t *= t;
            return t * t * dot;
        }
    }
}
