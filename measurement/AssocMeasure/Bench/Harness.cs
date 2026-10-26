using System.Diagnostics;
public sealed record Stat(string Name, int N, double Mean, double SD, double SE, double Err999, double Median, double Min, double Max, double[] Raw);
public static class Harness
{
    public static Stat Measure(string name, Action body, int warmup, int iterations, Action? setup = null, Action? cleanup = null)
    {
        for (int w = 0; w < warmup; w++) { setup?.Invoke(); body(); cleanup?.Invoke(); }
        var t = new double[iterations];
        var sw = new Stopwatch();
        for (int i = 0; i < iterations; i++)
        {
            setup?.Invoke();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            sw.Restart(); body(); sw.Stop();
            t[i] = sw.Elapsed.TotalSeconds;
            cleanup?.Invoke();
        }
        return Summarize(name, t);
    }
    public static Stat Summarize(string name, double[] t)
    {
        int n = t.Length; double mean = t.Average();
        double sd = n > 1 ? Math.Sqrt(t.Sum(x => (x - mean) * (x - mean)) / (n - 1)) : 0;
        double se = sd / Math.Sqrt(n);
        var s = t.OrderBy(x => x).ToArray();
        double med = n % 2 == 1 ? s[n / 2] : 0.5 * (s[n / 2 - 1] + s[n / 2]);
        return new Stat(name, n, mean, sd, se, TTable.Get(n - 1) * se, med, s[0], s[^1], t);
    }
    public static string Fmt(Stat st, double mb) =>
        $"{st.Name,-46} N={st.N,3}  mean={st.Mean * 1e3,10:F2} ms  ±{st.Err999 * 1e3,8:F2} (99.9% CI)  SD={st.SD * 1e3,8:F2}  med={st.Median * 1e3,10:F2}  min={st.Min * 1e3,10:F2}  max={st.Max * 1e3,10:F2}  per-MB={st.Mean / mb * 1e3,8:F2} ms";
}
