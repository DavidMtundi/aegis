namespace Aegis.Tests.Unit.Audit;

using System.Text.Json;
using Aegis.Modules.Audit.Application;

public sealed class AuditPayloadTests
{
    [Fact]
    public void Escapes_quotes_and_does_not_allow_key_injection()
    {
        var hostile = "bob\",\"isAdmin\":true,\"x\":\"";

        var json = AuditPayload.Json(new { AssignedTo = hostile });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(hostile, root.GetProperty("assignedTo").GetString());
        Assert.False(root.TryGetProperty("isAdmin", out _));
    }

    [Fact]
    public void Uses_camel_case_and_keeps_numbers_numeric()
    {
        var json = AuditPayload.Json(new { RuleId = Guid.Empty, Version = 3 });

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(3, doc.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(Guid.Empty, doc.RootElement.GetProperty("ruleId").GetGuid());
    }
}
