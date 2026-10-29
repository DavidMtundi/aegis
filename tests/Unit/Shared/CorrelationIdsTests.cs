using Aegis.Shared.Observability;
using Xunit;

namespace Aegis.Tests.Unit.Shared;

public class CorrelationIdsTests
{
    [Fact]
    public void Normalize_prefers_incoming()
    {
        Assert.Equal("client-1", CorrelationIds.Normalize("client-1", "trace"));
    }

    [Fact]
    public void Normalize_falls_back_to_trace()
    {
        Assert.Equal("trace-9", CorrelationIds.Normalize(null, "trace-9"));
    }

    [Fact]
    public void Normalize_truncates()
    {
        var value = new string('x', 150);
        Assert.Equal(CorrelationIds.MaxLength, CorrelationIds.Normalize(value, null).Length);
    }

    [Fact]
    public void FirstHeader_skips_blank()
    {
        Assert.Equal("b", CorrelationIds.FirstHeader(" ", null, "b"));
    }
}
