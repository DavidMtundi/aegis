namespace Aegis.Modules.Risk.Domain;

public static class RiskModelValidator
{
    public const int MaxWindowDays = 365;

    public static IReadOnlyList<string> Validate(IReadOnlyList<RiskFactorDefinition>? factors, RiskBands? bands)
    {
        var errors = new List<string>();
        factors ??= Array.Empty<RiskFactorDefinition>();

        if (factors.Count == 0 || factors.All(f => f.Weight <= 0))
            errors.Add("The model needs at least one factor with a weight above 0.");

        foreach (var duplicate in factors.GroupBy(f => f.Type).Where(g => g.Count() > 1))
            errors.Add($"{duplicate.Key} appears more than once.");

        foreach (var f in factors)
        {
            if (f.Weight is < 0 or > RiskCalculator.MaxScore)
                errors.Add($"{f.Type}: weight must be between 0 and {RiskCalculator.MaxScore}.");

            switch (f.Type)
            {
                case RiskFactorType.GEOGRAPHY:
                    if (f.Countries is not { Count: > 0 })
                        errors.Add($"{f.Type}: list at least one country (countries).");
                    foreach (var c in f.Countries ?? Array.Empty<string>())
                        if (c is null || c.Trim().Length != 2 || !c.Trim().All(char.IsLetter))
                            errors.Add($"{f.Type}: '{c}' is not a two-letter country code.");
                    break;
                case RiskFactorType.CUSTOMER_TYPE:
                    if (f.CustomerTypes is not { Count: > 0 })
                        errors.Add($"{f.Type}: list at least one customer type (customerTypes).");
                    break;
                case RiskFactorType.OPEN_ALERTS:
                case RiskFactorType.SUSPICIOUS_CASES:
                    RequirePointsEach(f, errors);
                    break;
                case RiskFactorType.HIGH_SEVERITY_ALERTS:
                    RequirePointsEach(f, errors);
                    RequireWindow(f, errors);
                    break;
                case RiskFactorType.TRANSACTION_ACTIVITY:
                    if (f.Threshold is not > 0)
                        errors.Add($"{f.Type}: threshold must be above 0.");
                    RequireWindow(f, errors);
                    break;
            }
        }

        if (bands is null)
            errors.Add("Bands are required.");
        else if (!(bands.Medium >= 1 && bands.Medium < bands.High && bands.High < bands.Critical && bands.Critical <= RiskCalculator.MaxScore))
            errors.Add($"Bands must satisfy 1 <= medium < high < critical <= {RiskCalculator.MaxScore}.");

        return errors;
    }

    private static void RequirePointsEach(RiskFactorDefinition f, List<string> errors)
    {
        if (f.PointsEach is not > 0)
            errors.Add($"{f.Type}: pointsEach must be above 0.");
    }

    private static void RequireWindow(RiskFactorDefinition f, List<string> errors)
    {
        if (f.WindowDays is not (> 0 and <= MaxWindowDays))
            errors.Add($"{f.Type}: windowDays must be between 1 and {MaxWindowDays}.");
    }
}
