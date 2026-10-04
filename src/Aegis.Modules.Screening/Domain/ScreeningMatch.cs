namespace Aegis.Modules.Screening.Domain;

using Aegis.Shared.Domain;

public enum ScreeningMatchStatus { PENDING_REVIEW, CONFIRMED, FALSE_POSITIVE }

public static class ScreeningTriggers
{
    public const string CustomerCreated = "CUSTOMER_CREATED";
    public const string ListUpdated = "LIST_UPDATED";
    public const string Manual = "MANUAL";
}

/// <summary>A possible match awaiting or after human review. One per customer and listed subject.</summary>
public sealed class ScreeningMatch : AggregateRoot
{
    public Guid CustomerId { get; private set; }
    public string CustomerName { get; private set; } = null!;
    public WatchlistSource Source { get; private set; }
    public string ExternalId { get; private set; } = null!;
    public WatchlistCategory Category { get; private set; }
    public string MatchedName { get; private set; } = null!;
    public double Score { get; private set; }
    public bool BirthYearMatched { get; private set; }

    /// <summary>The listed subject as it was when matched; lists change, the review record must not.</summary>
    public WatchlistSubject Subject { get; private set; } = null!;

    public string Trigger { get; private set; } = null!;
    public ScreeningMatchStatus Status { get; private set; }
    public string? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? ReviewReason { get; private set; }
    public Guid? AlertId { get; private set; }

    private ScreeningMatch() { }

    public static ScreeningMatch Create(TenantId tenantId, ScreeningCandidate candidate, PotentialMatch match, string trigger)
    {
        var now = DateTimeOffset.UtcNow;
        return new ScreeningMatch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = candidate.CustomerId,
            CustomerName = candidate.Name,
            Source = match.Subject.Source,
            ExternalId = match.Subject.ExternalId,
            Category = match.Subject.Category,
            MatchedName = match.MatchedName,
            Score = match.Score,
            BirthYearMatched = match.BirthYearMatched,
            Subject = match.Subject,
            Trigger = trigger,
            Status = ScreeningMatchStatus.PENDING_REVIEW,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Confirm(string reviewer, string reason, Guid alertId)
    {
        Review(ScreeningMatchStatus.CONFIRMED, reviewer, reason);
        AlertId = alertId;
    }

    public void MarkFalsePositive(string reviewer, string reason) => Review(ScreeningMatchStatus.FALSE_POSITIVE, reviewer, reason);

    private void Review(ScreeningMatchStatus status, string reviewer, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A review reason is required.", nameof(reason));
        if (Status != ScreeningMatchStatus.PENDING_REVIEW) throw new ScreeningMatchAlreadyReviewedException($"Match is already {Status}.");
        Status = status;
        ReviewedBy = reviewer;
        ReviewReason = reason.Trim();
        ReviewedAt = DateTimeOffset.UtcNow;
        UpdatedAt = ReviewedAt.Value;
    }
}
