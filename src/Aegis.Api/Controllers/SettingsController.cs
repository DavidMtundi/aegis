namespace Aegis.Api.Controllers;

using Aegis.Application.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("api/v1/settings")]
public sealed class SettingsController : ControllerBase
{
    public sealed record SlaSettingsResponse(int AlertSlaDays, int CaseSlaDays);

    [HttpGet("sla")]
    public SlaSettingsResponse GetSla([FromServices] DashboardOptions options)
        => new(options.AlertSlaDays, options.CaseSlaDays);
}
