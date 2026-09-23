namespace Aegis.Modules.Customers.Domain;

using System;
using Aegis.Shared.Domain;

public sealed class Customer : AggregateRoot
{
    public CustomerId CustomerId { get; private set; }
    public CustomerType Type { get; private set; }
    public string? ExternalReference { get; private set; }
    public CustomerStatus Status { get; private set; }
    public string Country { get; private set; } = null!;
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string? LegalName { get; private set; }

    private Customer() { }

    public static Customer CreateIndividual(
        TenantId tenantId,
        string? externalReference,
        string country,
        string firstName,
        string lastName)
    {
        var id = Guid.NewGuid();
        return new Customer
        {
            Id = id,
            CustomerId = new CustomerId(id),
            TenantId = tenantId,
            Type = CustomerType.INDIVIDUAL,
            ExternalReference = externalReference,
            Status = CustomerStatus.ACTIVE,
            Country = country.Trim().ToUpperInvariant(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public static Customer CreateBusiness(
        TenantId tenantId,
        string? externalReference,
        string country,
        string legalName)
    {
        var id = Guid.NewGuid();
        return new Customer
        {
            Id = id,
            CustomerId = new CustomerId(id),
            TenantId = tenantId,
            Type = CustomerType.BUSINESS,
            ExternalReference = externalReference,
            Status = CustomerStatus.ACTIVE,
            Country = country.Trim().ToUpperInvariant(),
            LegalName = legalName.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
