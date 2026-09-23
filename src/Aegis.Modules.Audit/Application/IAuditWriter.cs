namespace Aegis.Modules.Audit.Application;

using System;
using System.Threading;
using System.Threading.Tasks;
using Aegis.Modules.Audit.Domain;

public interface IAuditWriter
{
    Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}

public interface IAuditEventRepository
{
    Task<AuditEvent?> GetByTenantAndIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
}
