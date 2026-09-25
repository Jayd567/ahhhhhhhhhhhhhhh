using Unity.Burst;
using Unity.Mathematics;

namespace ColonySim.Simulation.Generation
{
    [BurstCompile]
    public struct EcsSimplexNoise
    {
        // 512-entry permutation table, built once from a seed. Lives in a fixed-size
        // buffer inside the struct so it can be captured by value into a job.
        public unsafe fixed int Permutation[512];

        public unsafe EcsSimplexNoise(int seed)
        {
            var temp = new NativeArrayLike256();
            for (int i = 0; i < 256; i++) temp[i] = i;
            uint state = unchecked((uint)seed) ^ 0x9E3779B9u;
            for (int i = 255; i > 0; i--)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                int j = (int)(state % (uint)(i + 1));
                (temp[i], temp[j]) = (temp[j], temp[i]);
            }
            fixed (int* p = Permutation)
            {
                for (int i = 0; i < 256; i++) { p[i] = temp[i]; p[i + 256] = temp[i]; }
            }
        }

        private unsafe struct NativeArrayLike256
        {
            public fixed int Values[256];
            public int this[int i] { get => Values[i]; set => Values[i] = value; }
        }

        public unsafe float Sample(float x, float y)
        {
            const float skew = 0.366025403784f, unskew = 0.211324865405f;
            float s = (x + y) * skew;
            int i = (int)math.floor(x + s), j = (int)math.floor(y + s);
            float t = (i + j) * unskew;
            float x0 = x - (i - t), y0 = y - (j - t);
            int i1 = x0 > y0 ? 1 : 0, j1 = 1 - i1;
            int ii = i & 255, jj = j & 255;
            fixed (int* p = Permutation)
            {
                return 70f * (Corner(p[ii + p[jj]], x0, y0)
                    + Corner(p[ii + i1 + p[jj + j1]], x0 - i1 + unskew, y0 - j1 + unskew)
                    + Corner(p[ii + 1 + p[jj + 1]], x0 - 1 + 2 * unskew, y0 - 1 + 2 * unskew));
            }
        }

        private static float Corner(int hash, float x, float y)
        {
            float t = 0.5f - x * x - y * y;
            if (t <= 0) return 0;
            float dot = (hash % 12) switch
            {
                0 => x + y, 1 => -x + y, 2 => x - y, 3 => -x - y,
                4 or 6 => x, 5 or 7 => -x, 8 or 10 => y, _ => -y,
            };
            t *= t;
            return t * t * dot;
        }
    }
}
