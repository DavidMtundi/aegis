namespace Aegis.Modules.Identity.Application;

using System;
using System.Threading;
using System.Threading.Tasks;
using Aegis.Modules.Identity.Domain;
using Aegis.Shared.Domain;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Tenant?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default);
}

public interface IUserRepository
{
    Task<User?> GetByTenantAndIdAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<User?> GetByTenantAndEmailAsync(TenantId tenantId, string email, CancellationToken cancellationToken = default);
    Task AddAsync(User user, CancellationToken cancellationToken = default);
    Task UpdateAsync(User user, CancellationToken cancellationToken = default);
}
