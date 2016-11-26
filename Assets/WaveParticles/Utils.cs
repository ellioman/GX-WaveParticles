using UnityEngine;
using System.Collections;

public static class Utils
{
    public static Vector2 Rotate(Vector2 v, float angle)
    {
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);
        return new Vector2(
            v.x * cos - v.y * sin,
            v.x * sin + v.y * cos
        );
    }

    public static Vector2 RotateCW(Vector2 v, Vector2 sc)
    {
        return new Vector2(
            v.x * sc.y - v.y * sc.x,
            v.x * sc.x + v.y * sc.y
        );
    }

    public static Vector2 Reflect(Vector2 v, Vector2 n)
    {
        return v - 2.0f * Vector2.Dot(v, n) * n;
    }

    public static Vector2 RotateCCW(Vector2 v, Vector2 sc)
    {
        return Reflect(RotateCW(v, sc), v);
    }

    public static float ChordLength(float r, float angle)
    {
        return 2.0f * r * Mathf.Sin(angle / 2.0f);
    }
}
