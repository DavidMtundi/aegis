namespace Aegis.Tests.Unit.Identity;

using Aegis.Modules.Identity.Domain;
using Aegis.Shared.Domain;

public sealed class RefreshTokenTests
{
    [Fact]
    public void Issue_sets_active_token()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var userId = Guid.NewGuid();
        var expires = DateTimeOffset.UtcNow.AddDays(14);

        var token = RefreshToken.Issue(tenantId, userId, "abc123hash", expires);

        Assert.Equal(tenantId, token.TenantId);
        Assert.Equal(userId, token.UserId);
        Assert.Equal("abc123hash", token.TokenHash);
        Assert.Equal(expires, token.ExpiresAt);
        Assert.Null(token.RevokedAt);
        Assert.True(token.IsActive(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Revoke_marks_inactive_and_records_replacement()
    {
        var token = RefreshToken.Issue(new TenantId(Guid.NewGuid()), Guid.NewGuid(), "hash", DateTimeOffset.UtcNow.AddDays(1));
        var replacementId = Guid.NewGuid();

        token.Revoke(replacementId);

        Assert.NotNull(token.RevokedAt);
        Assert.Equal(replacementId, token.ReplacedByTokenId);
        Assert.False(token.IsActive(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsActive_false_when_expired()
    {
        var token = RefreshToken.Issue(
            new TenantId(Guid.NewGuid()),
            Guid.NewGuid(),
            "hash",
            DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.False(token.IsActive(DateTimeOffset.UtcNow));
    }
}
