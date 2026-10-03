namespace Aegis.Tests.Unit.Risk;

using Aegis.Modules.Risk.Domain;
using Aegis.Shared.Domain;

public sealed class RiskModelValidationTests
{
    [Fact]
    public void Defaults_are_valid()
    {
        Assert.Empty(RiskModelValidator.Validate(RiskModelDefaults.Factors, RiskModelDefaults.Bands));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Weight_must_be_between_0_and_100(int weight)
    {
        var errors = RiskModelValidator.Validate(
            new[] { new RiskFactorDefinition(RiskFactorType.CUSTOMER_TYPE, weight, CustomerTypes: new[] { "BUSINESS" }) },
            RiskModelDefaults.Bands);

        Assert.Contains(errors, e => e.Contains("weight", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Each_factor_type_needs_its_parameters_and_may_appear_once()
    {
        var errors = RiskModelValidator.Validate(new[]
        {
            new RiskFactorDefinition(RiskFactorType.GEOGRAPHY, 10),
            new RiskFactorDefinition(RiskFactorType.OPEN_ALERTS, 10),
            new RiskFactorDefinition(RiskFactorType.TRANSACTION_ACTIVITY, 10, Threshold: 0, WindowDays: 400),
            new RiskFactorDefinition(RiskFactorType.OPEN_ALERTS, 10, PointsEach: 5),
        }, RiskModelDefaults.Bands);

        Assert.Contains(errors, e => e.Contains("GEOGRAPHY") && e.Contains("countries", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e => e.Contains("OPEN_ALERTS") && e.Contains("pointsEach", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e => e.Contains("TRANSACTION_ACTIVITY") && e.Contains("threshold", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e => e.Contains("TRANSACTION_ACTIVITY") && e.Contains("windowDays", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e => e.Contains("more than once"));
    }

    [Fact]
    public void Countries_must_be_two_letter_codes()
    {
        var errors = RiskModelValidator.Validate(
            new[] { new RiskFactorDefinition(RiskFactorType.GEOGRAPHY, 10, Countries: new[] { "Iran" }) },
            RiskModelDefaults.Bands);

        Assert.Contains(errors, e => e.Contains("Iran"));
    }

    [Fact]
    public void Model_needs_at_least_one_weighted_factor_and_ascending_bands()
    {
        Assert.NotEmpty(RiskModelValidator.Validate(Array.Empty<RiskFactorDefinition>(), RiskModelDefaults.Bands));
        Assert.NotEmpty(RiskModelValidator.Validate(RiskModelDefaults.Factors, new RiskBands(50, 40, 90)));
        Assert.NotEmpty(RiskModelValidator.Validate(RiskModelDefaults.Factors, new RiskBands(0, 40, 90)));
        Assert.NotEmpty(RiskModelValidator.Validate(RiskModelDefaults.Factors, new RiskBands(10, 40, 101)));
    }

    [Fact]
    public void New_version_retires_the_previous_one()
    {
        var tenant = new TenantId(Guid.NewGuid());
        var v1 = RiskModel.CreateDefault(tenant, "system");
        var v2 = RiskModel.CreateNextVersion(v1, RiskModelDefaults.Factors, new RiskBands(20, 45, 70), "admin");

        Assert.Equal(1, v1.Version);
        Assert.Equal(2, v2.Version);
        Assert.False(v1.IsActive);
        Assert.True(v2.IsActive);
        Assert.Equal(20, v2.Bands.Medium);
    }
}
