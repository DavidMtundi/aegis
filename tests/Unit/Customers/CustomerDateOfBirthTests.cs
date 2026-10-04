namespace Aegis.Tests.Unit.Customers;

using Aegis.Modules.Customers.Domain;
using Aegis.Shared.Domain;

public sealed class CustomerDateOfBirthTests
{
    [Fact]
    public void Individual_keeps_an_optional_date_of_birth()
    {
        var customer = Customer.CreateIndividual(new TenantId(Guid.NewGuid()), null, "KE", "Jane", "Doe", new DateOnly(1980, 5, 1));
        Assert.Equal(new DateOnly(1980, 5, 1), customer.DateOfBirth);
        Assert.Null(Customer.CreateIndividual(new TenantId(Guid.NewGuid()), null, "KE", "Jane", "Doe").DateOfBirth);
    }
}
