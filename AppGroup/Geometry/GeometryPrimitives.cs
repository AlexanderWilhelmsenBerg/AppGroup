using System;

namespace AppGroup.Geometry;

public readonly record struct GeometryPoint(int X, int Y);

public readonly record struct GeometrySize(double Width, double Height);

public readonly record struct GeometryRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);

    public int Height => Math.Max(0, Bottom - Top);

    public bool IsEmpty => Width == 0 || Height == 0;

    public int CenterX => Left + (Width / 2);

    public int CenterY => Top + (Height / 2);

    public static GeometryRect FromXYWH(int x, int y, int width, int height)
    {
        return new GeometryRect(x, y, checked(x + width), checked(y + height));
    }

    public GeometryRect Intersect(GeometryRect other)
    {
        int left = Math.Max(Left, other.Left);
        int top = Math.Max(Top, other.Top);
        int right = Math.Min(Right, other.Right);
        int bottom = Math.Min(Bottom, other.Bottom);

        return right <= left || bottom <= top
            ? new GeometryRect(left, top, left, top)
            : new GeometryRect(left, top, right, bottom);
    }
}
