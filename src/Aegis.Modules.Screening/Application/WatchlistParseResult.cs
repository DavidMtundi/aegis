namespace Aegis.Modules.Screening.Application;

using Aegis.Modules.Screening.Domain;

public sealed record WatchlistParseResult(IReadOnlyList<WatchlistSubject> Subjects, DateTimeOffset? PublishedAt);
