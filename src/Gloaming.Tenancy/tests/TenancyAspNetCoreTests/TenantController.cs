using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Gloaming.Tenancy.AspNetCore.Tests;

[ApiController]
[Route("controller")]
[AllowAnonymous]
[TenantRequirement(TenantRequirement.TenantlessAllowed)]
public sealed class TenantController(ITenantContextAccessor accessor) : ControllerBase
{
    [HttpGet("optional")]
    public IActionResult Optional() => Content(TestApplication.Snapshot(accessor));

    [HttpGet("required")]
    [TenantRequirement(TenantRequirement.Required)]
    public IActionResult Required() => Content(TestApplication.Snapshot(accessor));
}
