using System.Numerics;

namespace MiiAnim.Core.Animation;

/// <summary>
/// A keyframe. For <see cref="Interpolation.Custom"/> segments the two bezier handles of the segment
/// that starts at this key are stored here:
/// <see cref="HandleOut"/> = (fraction of segment length after this key, value offset from this key),
/// <see cref="HandleIn"/> = (fraction of segment length before the next key, value offset from the next key).
/// </summary>
public struct Keyframe(int frame, float value, Interpolation interpolation = Interpolation.Smooth)
{
    public int Frame = frame;
    public float Value = value;
    public Interpolation Interpolation = interpolation;
    public Vector2 HandleOut = new(1f / 3f, 0f);
    public Vector2 HandleIn = new(1f / 3f, 0f);
}

public sealed class AnimCurve
{
    private readonly List<Keyframe> _keys = [];

    public IReadOnlyList<Keyframe> Keys => _keys;

    public int Count => _keys.Count;

    public AnimCurve Clone()
    {
        var clone = new AnimCurve();
        clone._keys.AddRange(_keys);
        return clone;
    }

    /// <summary>Insert or replace the key at <paramref name="frame"/>, keeping existing easing when replacing.</summary>
    public void SetKey(int frame, float value, Interpolation? interpolation = null)
    {
        var index = IndexOf(frame);
        if (index >= 0)
        {
            var existing = _keys[index];
            existing.Value = value;
            if (interpolation.HasValue)
                existing.Interpolation = interpolation.Value;
            _keys[index] = existing;
            return;
        }

        var key = new Keyframe(frame, value, interpolation ?? InheritInterpolation(frame));
        var insertAt = _keys.FindIndex(k => k.Frame > frame);
        if (insertAt < 0)
            _keys.Add(key);
        else
            _keys.Insert(insertAt, key);
    }

    public void SetKey(Keyframe key)
    {
        var index = IndexOf(key.Frame);
        if (index >= 0)
        {
            _keys[index] = key;
            return;
        }

        var insertAt = _keys.FindIndex(k => k.Frame > key.Frame);
        if (insertAt < 0)
            _keys.Add(key);
        else
            _keys.Insert(insertAt, key);
    }

    public void ReplaceAt(int index, Keyframe key) => _keys[index] = key;

    public bool RemoveKey(int frame)
    {
        var index = IndexOf(frame);
        if (index < 0)
            return false;
        _keys.RemoveAt(index);
        return true;
    }

    public int IndexOf(int frame) => _keys.FindIndex(k => k.Frame == frame);

    public bool HasKeyAt(int frame) => IndexOf(frame) >= 0;

    /// <summary>Moves a key to another frame; replaces any key already there.</summary>
    public void MoveKey(int fromFrame, int toFrame)
    {
        var index = IndexOf(fromFrame);
        if (index < 0 || fromFrame == toFrame)
            return;
        var key = _keys[index];
        _keys.RemoveAt(index);
        RemoveKey(toFrame);
        key.Frame = toFrame;
        SetKey(key);
    }

    private Interpolation InheritInterpolation(int frame)
    {
        // New keys copy the easing of the key before them so a curve keeps a consistent feel.
        Interpolation? previous = null;
        foreach (var key in _keys)
        {
            if (key.Frame > frame)
                break;
            previous = key.Interpolation;
        }

        return previous is null or Interpolation.Custom ? Interpolation.Smooth : previous.Value;
    }

    public float Evaluate(float frame, float defaultValue)
    {
        var count = _keys.Count;
        if (count == 0)
            return defaultValue;
        if (count == 1 || frame <= _keys[0].Frame)
            return _keys[0].Value;
        if (frame >= _keys[count - 1].Frame)
            return _keys[count - 1].Value;

        var i = FindSegment(frame);
        var a = _keys[i];
        var b = _keys[i + 1];
        var span = b.Frame - a.Frame;
        var u = (frame - a.Frame) / span;

        switch (a.Interpolation)
        {
            case Interpolation.Hold:
                return a.Value;
            case Interpolation.Smooth:
            {
                var m0 = AutoSlope(i) * span;
                var m1 = AutoSlope(i + 1) * span;
                return Hermite(a.Value, m0, b.Value, m1, u);
            }
            case Interpolation.Custom:
            {
                var p1 = new Vector2(Math.Clamp(a.HandleOut.X, 0f, 1f), a.Value + a.HandleOut.Y);
                var p2 = new Vector2(1f - Math.Clamp(a.HandleIn.X, 0f, 1f), b.Value + a.HandleIn.Y);
                return EvaluateBezier(a.Value, p1, p2, b.Value, u);
            }
            default:
                return a.Value + (b.Value - a.Value) * InterpolationInfo.Ease(a.Interpolation, u);
        }
    }

    /// <summary>Stepped evaluation for discrete channels (expression, toggles).</summary>
    public float EvaluateStepped(float frame, float defaultValue)
    {
        if (_keys.Count == 0)
            return defaultValue;
        var value = _keys[0].Value;
        foreach (var key in _keys)
        {
            if (key.Frame > frame)
                break;
            value = key.Value;
        }

        return value;
    }

    private int FindSegment(float frame)
    {
        int lo = 0,
            hi = _keys.Count - 2;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (_keys[mid].Frame <= frame)
                lo = mid;
            else
                hi = mid - 1;
        }

        return lo;
    }

    /// <summary>Auto-clamped slope (value per frame): flat at extremes so smooth curves never overshoot.</summary>
    public float AutoSlope(int index)
    {
        if (index <= 0 || index >= _keys.Count - 1)
            return 0f;
        var prev = _keys[index - 1];
        var key = _keys[index];
        var next = _keys[index + 1];
        var d0 = key.Value - prev.Value;
        var d1 = next.Value - key.Value;
        if (d0 * d1 <= 0f)
            return 0f;
        var slope = (next.Value - prev.Value) / (next.Frame - prev.Frame);
        // Clamp so the hermite segment stays monotonic on both sides.
        var max0 = 3f * d0 / (key.Frame - prev.Frame);
        var max1 = 3f * d1 / (next.Frame - key.Frame);
        return slope > 0 ? MathF.Min(slope, MathF.Min(max0, max1)) : MathF.Max(slope, MathF.Max(max0, max1));
    }

    /// <summary>Handles that reproduce the current curve shape, used when switching a segment to Custom.</summary>
    public (Vector2 Out, Vector2 In) SuggestHandles(int index)
    {
        if (index < 0 || index >= _keys.Count - 1)
            return (new(1f / 3f, 0f), new(1f / 3f, 0f));
        var a = _keys[index];
        var b = _keys[index + 1];
        var span = b.Frame - a.Frame;
        switch (a.Interpolation)
        {
            case Interpolation.Custom:
                return (a.HandleOut, a.HandleIn);
            case Interpolation.Linear:
            {
                var dv = b.Value - a.Value;
                return (new(1f / 3f, dv / 3f), new(1f / 3f, -dv / 3f));
            }
            default:
                return (new(1f / 3f, AutoSlope(index) * span / 3f), new(1f / 3f, -AutoSlope(index + 1) * span / 3f));
        }
    }

    private static float Hermite(float p0, float m0, float p1, float m1, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * m1;
    }

    private static float EvaluateBezier(float v0, Vector2 p1, Vector2 p2, float v3, float x)
    {
        // Solve x(s) = x for s on the time axis (monotonic since handle x is clamped to 0..1), then return y(s).
        float s = x;
        for (var i = 0; i < 8; i++)
        {
            var bx = BezierComponent(0f, p1.X, p2.X, 1f, s) - x;
            var dx = BezierDerivative(0f, p1.X, p2.X, 1f, s);
            if (MathF.Abs(bx) < 1e-6f)
                break;
            if (MathF.Abs(dx) < 1e-6f)
                break;
            s = Math.Clamp(s - bx / dx, 0f, 1f);
        }

        // Bisection fallback for flat handles.
        if (MathF.Abs(BezierComponent(0f, p1.X, p2.X, 1f, s) - x) > 1e-4f)
        {
            float lo = 0f,
                hi = 1f;
            for (var i = 0; i < 30; i++)
            {
                s = (lo + hi) * 0.5f;
                if (BezierComponent(0f, p1.X, p2.X, 1f, s) < x)
                    lo = s;
                else
                    hi = s;
            }
        }

        return BezierComponent(v0, p1.Y, p2.Y, v3, s);
    }

    private static float BezierComponent(float a, float b, float c, float d, float s)
    {
        var inv = 1f - s;
        return inv * inv * inv * a + 3f * inv * inv * s * b + 3f * inv * s * s * c + s * s * s * d;
    }

    private static float BezierDerivative(float a, float b, float c, float d, float s)
    {
        var inv = 1f - s;
        return 3f * inv * inv * (b - a) + 6f * inv * s * (c - b) + 3f * s * s * (d - c);
    }
}
