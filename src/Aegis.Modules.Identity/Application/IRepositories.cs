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
    Task UpdateAsync(Tenant tenant, CancellationToken cancellationToken = default);
}

public interface IUserRepository
{
    Task<User?> GetByTenantAndIdAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<User?> GetByTenantAndEmailAsync(TenantId tenantId, string email, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> ListByTenantAsync(TenantId tenantId, CancellationToken cancellationToken = default);
    Task AddAsync(User user, CancellationToken cancellationToken = default);
    Task UpdateAsync(User user, CancellationToken cancellationToken = default);
}

public static class UserRepositoryExtensions
{
    /// <summary>Resolves an assignee id (or the caller when blank) to an active user in the tenant.</summary>
    public static async Task<User?> FindActiveAssigneeAsync(
        this IUserRepository users, TenantId tenantId, string? assignedTo, Guid callerId, CancellationToken cancellationToken = default)
    {
        var id = callerId;
        if (!string.IsNullOrWhiteSpace(assignedTo) && !Guid.TryParse(assignedTo.Trim(), out id))
            return null;
        var user = await users.GetByTenantAndIdAsync(tenantId, id, cancellationToken);
        return user is { Status: UserStatus.ACTIVE } ? user : null;
    }
}
