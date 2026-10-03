namespace Aegis.Tests.Unit.Customers;

using Aegis.Modules.Customers.Domain;
using Aegis.Shared.Domain;

public sealed class AccountCurrencyTests
{
    private static Account Create(string currency) => Account.Create(
        new TenantId(Guid.NewGuid()), new CustomerId(Guid.NewGuid()), null, AccountType.MOBILE_WALLET, currency);

    [Theory]
    [InlineData("KES", "KES")]
    [InlineData("usd", "USD")]
    [InlineData(" ugx ", "UGX")]
    public void Accepts_iso_codes_and_normalises_case(string input, string expected)
    {
        Assert.Equal(expected, Create(input).Currency);
    }

    [Theory]
    [InlineData("")]
    [InlineData("KE")]
    [InlineData("KESS")]
    [InlineData("K3S")]
    public void Rejects_malformed_codes(string input)
    {
        Assert.Throws<ArgumentException>(() => Create(input));
    }
}
