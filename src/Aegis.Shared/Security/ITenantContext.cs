namespace Aegis.Shared.Security;

using System;
using System.Collections.Generic;
using Aegis.Shared.Domain;

public interface ITenantContext
{
    TenantId TenantId { get; }
    Guid UserId { get; }
    IReadOnlyCollection<string> Roles { get; }
    bool IsAuthenticated { get; }
}
