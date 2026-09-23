namespace Aegis.Shared.Security;

using System;
using System.Collections.Generic;
using Aegis.Shared.Domain;

public sealed class TenantContext : ITenantContext
{
    public TenantId TenantId { get; set; } = TenantId.Empty;
    public Guid UserId { get; set; }
    public IReadOnlyCollection<string> Roles { get; set; } = Array.Empty<string>();
    public bool IsAuthenticated { get; set; }
}
