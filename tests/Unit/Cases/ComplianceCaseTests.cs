namespace Aegis.Tests.Unit.Cases;

using Aegis.Modules.Cases.Domain;
using Aegis.Shared.Domain;

public sealed class ComplianceCaseTests
{
    [Fact]
    public void Close_requires_conclusion_and_sets_disposition()
    {
        var c = ComplianceCase.CreateFromAlert(
            new TenantId(Guid.NewGuid()),
            Guid.NewGuid(),
            "Test case",
            CasePriority.HIGH,
            Guid.NewGuid());

        Assert.ThrowsAny<ArgumentException>(() => c.Close(CaseDisposition.FALSE_POSITIVE, "  "));
        c.Close(CaseDisposition.FALSE_POSITIVE, "Explained");
        Assert.Equal(CaseStatus.CLOSED, c.Status);
        Assert.Equal(CaseDisposition.FALSE_POSITIVE, c.Disposition);
        Assert.NotNull(c.ClosedAt);
        Assert.Throws<InvalidOperationException>(() => c.AddNote("late", "u1"));
    }
}
