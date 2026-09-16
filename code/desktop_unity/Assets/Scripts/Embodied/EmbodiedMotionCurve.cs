using System;

// Live2D motion3.json 曲线片段求值（0=线性、1=三次贝塞尔、2=阶跃、3=逆阶跃）。
// 供隔离探针回放与未来运行时播放共用；时间轴单调，贝塞尔用二分反解参数。
// 编码已对官方 78 个示例动作 4812 条曲线全量验证（贝塞尔=类型+3 个点对）。
public sealed class EmbodiedMotionCurve
{
    public string ParameterId { get; }
    public double[] Segments { get; }
    public double DurationSeconds { get; }

    public EmbodiedMotionCurve(string parameterId, double[] segments, double durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(parameterId)) throw new ArgumentException("parameter id required", nameof(parameterId));
        if (segments == null || segments.Length < 2) throw new ArgumentException("segments required", nameof(segments));
        ParameterId = parameterId;
        Segments = segments;
        DurationSeconds = durationSeconds;
    }

    public float Evaluate(double time)
    {
        double limit = DurationSeconds > 0 ? DurationSeconds : time;
        double t = Math.Max(0.0, Math.Min(time, limit));
        double[] s = Segments;
        double currentTime = s[0], currentValue = s[1];
        if (t <= currentTime) return (float)currentValue;
        int i = 2;
        while (i + 2 < s.Length || (i < s.Length && s[i] >= 0 && s[i] <= 3))
        {
            if (i >= s.Length) break;
            int type = (int)s[i];
            if (type == 0)
            {
                if (i + 2 >= s.Length) break;
                double nextTime = s[i + 1], nextValue = s[i + 2];
                if (t <= nextTime) return (float)(nextTime > currentTime
                    ? currentValue + (nextValue - currentValue) * ((t - currentTime) / (nextTime - currentTime))
                    : nextValue);
                currentTime = nextTime; currentValue = nextValue; i += 3;
            }
            else if (type == 1)
            {
                if (i + 6 >= s.Length) break;
                double x1 = s[i + 1], y1 = s[i + 2], x2 = s[i + 3], y2 = s[i + 4], x3 = s[i + 5], y3 = s[i + 6];
                if (t <= x3)
                {
                    double span = x3 - currentTime;
                    double u = span > 0 ? SolveCubicParameter(currentTime, x1, x2, x3, t) : 1.0;
                    return (float)CubicValue(currentValue, y1, y2, y3, u);
                }
                currentTime = x3; currentValue = y3; i += 7;
            }
            else if (type == 2)
            {
                if (i + 2 >= s.Length) break;
                double nextTime = s[i + 1], nextValue = s[i + 2];
                if (t < nextTime) return (float)currentValue;
                if (t == nextTime) return (float)nextValue;
                currentTime = nextTime; currentValue = nextValue; i += 3;
            }
            else if (type == 3)
            {
                if (i + 2 >= s.Length) break;
                double nextTime = s[i + 1], nextValue = s[i + 2];
                if (t < nextTime) return (float)nextValue;
                currentTime = nextTime; currentValue = nextValue; i += 3;
            }
            else break;
        }
        return (float)currentValue;
    }

    private static double CubicValue(double p0, double p1, double p2, double p3, double u)
    {
        double w = 1.0 - u;
        return w * w * w * p0 + 3.0 * w * w * u * p1 + 3.0 * w * u * u * p2 + u * u * u * p3;
    }

    private static double SolveCubicParameter(double x0, double x1, double x2, double x3, double t)
    {
        double lo = 0.0, hi = 1.0;
        for (int k = 0; k < 48; k++)
        {
            double mid = 0.5 * (lo + hi);
            double x = CubicValue(x0, x1, x2, x3, mid);
            if (x < t) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }
}
