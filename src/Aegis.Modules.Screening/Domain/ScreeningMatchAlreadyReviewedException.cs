namespace Aegis.Modules.Screening.Domain;

/// <summary>The match has a decision already, possibly recorded by another reviewer a moment ago.</summary>
public sealed class ScreeningMatchAlreadyReviewedException : Exception
{
    public ScreeningMatchAlreadyReviewedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
