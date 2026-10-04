namespace Aegis.Modules.Screening.Domain;

/// <summary>Subjects with their names tokenized once, so many customers can be screened cheaply.</summary>
public sealed class WatchlistIndex
{
    public const double DefaultThreshold = 0.88;

    /// <summary>A customer born this many years outside the listed range is still treated as consistent.</summary>
    public const int BirthYearTolerance = 1;

    private readonly List<(WatchlistSubject Subject, (string Name, IReadOnlyList<string> Tokens)[] Names)> _entries;

    public WatchlistIndex(IEnumerable<WatchlistSubject> subjects)
        => _entries = subjects
            .Select(s => (s, s.Names.Select(n => (n, NameNormalizer.Tokens(n))).ToArray()))
            .ToList();

    public int Count => _entries.Count;

    public IReadOnlyList<PotentialMatch> Screen(ScreeningCandidate candidate, double threshold = DefaultThreshold)
    {
        var tokens = NameNormalizer.Tokens(candidate.Name);
        if (tokens.Count == 0) return Array.Empty<PotentialMatch>();

        var matches = new List<PotentialMatch>();
        foreach (var (subject, names) in _entries)
        {
            if (subject.Kind != candidate.Kind) continue;

            var birthYearMatched = false;
            if (candidate.DateOfBirth is { } dob && subject.BirthYears.Count > 0)
            {
                birthYearMatched = subject.BirthYears.Any(r => r.Contains(dob.Year, BirthYearTolerance));
                if (!birthYearMatched) continue;
            }

            var best = 0.0;
            string? bestName = null;
            foreach (var (name, nameTokens) in names)
            {
                var score = NameMatcher.Score(tokens, nameTokens);
                if (score > best)
                {
                    best = score;
                    bestName = name;
                }
            }

            if (bestName is not null && best >= threshold)
                matches.Add(new PotentialMatch(subject, bestName, best, birthYearMatched));
        }

        return matches.OrderByDescending(m => m.Score).ToList();
    }
}
