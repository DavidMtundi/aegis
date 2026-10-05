namespace Aegis.Modules.Screening.Domain;

public static class NameMatcher
{
    /// <summary>A single shared token is too weak to call a match against a longer name.</summary>
    private const double SingleTokenCap = 0.85;

    /// <summary>
    /// Every paired part must be at least this similar; otherwise the score is capped at the weakest pair, so one
    /// exact common part (MOHAMED, HASSAN) cannot carry a loosely similar other part over the threshold.
    /// </summary>
    public const double MinTokenSimilarity = 0.85;

    /// <summary>
    /// Order-insensitive similarity in [0, 1]. Each token of the shorter name is paired with its best unused
    /// token in the longer name; the average pair similarity is reduced when the longer name has extra tokens.
    /// </summary>
    public static double Score(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var (shorter, longer) = a.Count <= b.Count ? (a, b) : (b, a);

        var pairs = new List<(int S, int L, double Sim)>(shorter.Count * longer.Count);
        for (var s = 0; s < shorter.Count; s++)
            for (var l = 0; l < longer.Count; l++)
                pairs.Add((s, l, JaroWinkler.Similarity(shorter[s], longer[l])));

        var usedS = new bool[shorter.Count];
        var usedL = new bool[longer.Count];
        var total = 0.0;
        var weakest = 1.0;
        foreach (var p in pairs.OrderByDescending(p => p.Sim))
        {
            if (usedS[p.S] || usedL[p.L]) continue;
            usedS[p.S] = usedL[p.L] = true;
            total += p.Sim;
            weakest = Math.Min(weakest, p.Sim);
        }

        var coverage = (double)shorter.Count / longer.Count;
        var score = Math.Round(total / shorter.Count * (0.8 + 0.2 * coverage), 4);
        if (weakest < MinTokenSimilarity) score = Math.Min(score, Math.Round(weakest, 4));
        return shorter.Count == 1 && longer.Count > 1 ? Math.Min(score, SingleTokenCap) : score;
    }
}
