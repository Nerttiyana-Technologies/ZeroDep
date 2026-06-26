namespace ZeroDep.Filters.Jpx;

/// <summary>
/// Inverse discrete wavelet transforms (ITU-T T.800 Annex F). The reversible 5/3 integer transform is
/// bit-exact; the irreversible 9/7 float transform reconstructs lossy components. Each level interleaves
/// the previous low-low band with the level's HL/LH/HH detail bands, then applies the 1D inverse along
/// rows and columns. Boundary samples use the whole-sample symmetric extension (clamped neighbours).
/// </summary>
internal static class JpxWavelet
{
    private const double Alpha = -1.586134342059924;
    private const double Beta = -0.052980118572961;
    private const double Gamma = 0.882911075530934;
    private const double Delta = 0.443506852043971;
    private const double K = 1.230174104914001;
    private const double InvK = 1.0 / K;

    /// <summary>
    /// 5/3 reversible synthesis. <paramref name="data"/>[r] is resolution r's array (size w*h); index 0 is
    /// the LL band, indices &gt;=1 hold the HL/LH/HH coefficients placed at the non-low-low parities.
    /// </summary>
    public static int[] SynthesizeReversible(JpxResolution[] resolutions, int[][] data)
    {
        int[] ll = data[0];
        int llw = resolutions[0].Width;
        int llh = resolutions[0].Height;

        for (int r = 1; r < resolutions.Length; r++)
        {
            JpxResolution res = resolutions[r];
            int w = res.Width;
            int h = res.Height;
            if (w <= 0 || h <= 0)
            {
                continue;
            }

            int px = res.X0 & 1;
            int py = res.Y0 & 1;
            int[] cur = data[r];

            for (int y = 0; y < llh; y++)
            {
                int row = ((2 * y) + py) * w;
                int src = y * llw;
                for (int x = 0; x < llw; x++)
                {
                    cur[row + (2 * x) + px] = ll[src + x];
                }
            }

            var line = new int[w > h ? w : h];
            for (int y = 0; y < h; y++)
            {
                int off = y * w;
                for (int x = 0; x < w; x++)
                {
                    line[x] = cur[off + x];
                }

                Inverse53(line, w, px);
                for (int x = 0; x < w; x++)
                {
                    cur[off + x] = line[x];
                }
            }

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    line[y] = cur[(y * w) + x];
                }

                Inverse53(line, h, py);
                for (int y = 0; y < h; y++)
                {
                    cur[(y * w) + x] = line[y];
                }
            }

            ll = cur;
            llw = w;
            llh = h;
        }

        return ll;
    }

    /// <summary>9/7 irreversible synthesis (float). Layout matches <see cref="SynthesizeReversible"/>.</summary>
    public static double[] SynthesizeIrreversible(JpxResolution[] resolutions, double[][] data)
    {
        double[] ll = data[0];
        int llw = resolutions[0].Width;
        int llh = resolutions[0].Height;

        for (int r = 1; r < resolutions.Length; r++)
        {
            JpxResolution res = resolutions[r];
            int w = res.Width;
            int h = res.Height;
            if (w <= 0 || h <= 0)
            {
                continue;
            }

            int px = res.X0 & 1;
            int py = res.Y0 & 1;
            double[] cur = data[r];

            for (int y = 0; y < llh; y++)
            {
                int row = ((2 * y) + py) * w;
                int src = y * llw;
                for (int x = 0; x < llw; x++)
                {
                    cur[row + (2 * x) + px] = ll[src + x];
                }
            }

            var line = new double[w > h ? w : h];
            for (int y = 0; y < h; y++)
            {
                int off = y * w;
                for (int x = 0; x < w; x++)
                {
                    line[x] = cur[off + x];
                }

                Inverse97(line, w, px);
                for (int x = 0; x < w; x++)
                {
                    cur[off + x] = line[x];
                }
            }

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    line[y] = cur[(y * w) + x];
                }

                Inverse97(line, h, py);
                for (int y = 0; y < h; y++)
                {
                    cur[(y * w) + x] = line[y];
                }
            }

            ll = cur;
            llw = w;
            llh = h;
        }

        return ll;
    }

    // ----- 5/3 integer inverse -----

    private static void Inverse53(int[] a, int len, int cas)
    {
        if (len == 1)
        {
            if (cas == 1)
            {
                a[0] >>= 1;
            }

            return;
        }

        int sn = cas == 0 ? (len + 1) / 2 : len / 2;
        int dn = len - sn;

        if (cas == 0)
        {
            for (int i = 0; i < sn; i++)
            {
                a[2 * i] -= (DOdd(a, dn, i - 1) + DOdd(a, dn, i) + 2) >> 2;
            }

            for (int i = 0; i < dn; i++)
            {
                a[(2 * i) + 1] += (SEven(a, sn, i) + SEven(a, sn, i + 1)) >> 1;
            }
        }
        else
        {
            for (int i = 0; i < sn; i++)
            {
                a[(2 * i) + 1] -= (HEven(a, dn, i) + HEven(a, dn, i + 1) + 2) >> 2;
            }

            for (int i = 0; i < dn; i++)
            {
                a[2 * i] += (LOdd(a, sn, i - 1) + LOdd(a, sn, i)) >> 1;
            }
        }
    }

    private static int SEven(int[] a, int sn, int i) => a[2 * Clamp(i, 0, sn - 1)];

    private static int DOdd(int[] a, int dn, int i) => a[(2 * Clamp(i, 0, dn - 1)) + 1];

    private static int HEven(int[] a, int dn, int i) => a[2 * Clamp(i, 0, dn - 1)];

    private static int LOdd(int[] a, int sn, int i) => a[(2 * Clamp(i, 0, sn - 1)) + 1];

    // ----- 9/7 float inverse -----

    private static void Inverse97(double[] a, int len, int cas)
    {
        if (len == 1)
        {
            return;
        }

        int sn = cas == 0 ? (len + 1) / 2 : len / 2;
        int dn = len - sn;

        if (cas == 0)
        {
            for (int i = 0; i < sn; i++)
            {
                a[2 * i] *= K;
            }

            for (int i = 0; i < dn; i++)
            {
                a[(2 * i) + 1] *= InvK;
            }

            for (int i = 0; i < sn; i++)
            {
                a[2 * i] -= Delta * (DOddF(a, dn, i - 1) + DOddF(a, dn, i));
            }

            for (int i = 0; i < dn; i++)
            {
                a[(2 * i) + 1] -= Gamma * (SEvenF(a, sn, i) + SEvenF(a, sn, i + 1));
            }

            for (int i = 0; i < sn; i++)
            {
                a[2 * i] -= Beta * (DOddF(a, dn, i - 1) + DOddF(a, dn, i));
            }

            for (int i = 0; i < dn; i++)
            {
                a[(2 * i) + 1] -= Alpha * (SEvenF(a, sn, i) + SEvenF(a, sn, i + 1));
            }
        }
        else
        {
            for (int i = 0; i < sn; i++)
            {
                a[(2 * i) + 1] *= K;
            }

            for (int i = 0; i < dn; i++)
            {
                a[2 * i] *= InvK;
            }

            for (int i = 0; i < sn; i++)
            {
                a[(2 * i) + 1] -= Delta * (HEvenF(a, dn, i) + HEvenF(a, dn, i + 1));
            }

            for (int i = 0; i < dn; i++)
            {
                a[2 * i] -= Gamma * (LOddF(a, sn, i - 1) + LOddF(a, sn, i));
            }

            for (int i = 0; i < sn; i++)
            {
                a[(2 * i) + 1] -= Beta * (HEvenF(a, dn, i) + HEvenF(a, dn, i + 1));
            }

            for (int i = 0; i < dn; i++)
            {
                a[2 * i] -= Alpha * (LOddF(a, sn, i - 1) + LOddF(a, sn, i));
            }
        }
    }

    private static double SEvenF(double[] a, int sn, int i) => a[2 * Clamp(i, 0, sn - 1)];

    private static double DOddF(double[] a, int dn, int i) => a[(2 * Clamp(i, 0, dn - 1)) + 1];

    private static double HEvenF(double[] a, int dn, int i) => a[2 * Clamp(i, 0, dn - 1)];

    private static double LOddF(double[] a, int sn, int i) => a[(2 * Clamp(i, 0, sn - 1)) + 1];

    private static int Clamp(int v, int lo, int hi)
    {
        if (hi < lo)
        {
            return lo;
        }

        return v < lo ? lo : (v > hi ? hi : v);
    }
}
