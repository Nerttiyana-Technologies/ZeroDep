namespace ZeroDep.Content;

/// <summary>A 2-D affine transformation matrix <c>[a b c d e f]</c> (ISO 32000-2 §8.3.3).</summary>
internal readonly struct Matrix
{
    public Matrix(double a, double b, double c, double d, double e, double f)
    {
        A = a;
        B = b;
        C = c;
        D = d;
        E = e;
        F = f;
    }

    public double A { get; }

    public double B { get; }

    public double C { get; }

    public double D { get; }

    public double E { get; }

    public double F { get; }

    /// <summary>The identity transform.</summary>
    public static Matrix Identity => new Matrix(1, 0, 0, 1, 0, 0);

    /// <summary>Returns <paramref name="x"/> · <paramref name="y"/> (apply <paramref name="x"/> first, then <paramref name="y"/>).</summary>
    public static Matrix Multiply(Matrix x, Matrix y) => new Matrix(
        (x.A * y.A) + (x.B * y.C),
        (x.A * y.B) + (x.B * y.D),
        (x.C * y.A) + (x.D * y.C),
        (x.C * y.B) + (x.D * y.D),
        (x.E * y.A) + (x.F * y.C) + y.E,
        (x.E * y.B) + (x.F * y.D) + y.F);
}
