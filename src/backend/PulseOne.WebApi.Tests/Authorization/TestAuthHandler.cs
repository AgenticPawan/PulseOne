using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseOne.SharedKernel.Security;

namespace PulseOne.WebApi.Tests.Authorization;

/// <summary>
/// A test authentication handler that mints the principal described by per-request headers, so the
/// host-boundary integration tests can drive the REAL <c>HostOperatorsOnly</c> authorization policy
/// without a live Azure AD B2C token. The policy under test is production code; only token issuance
/// is faked (the constraint forbids depending on Azure services in tests).
/// </summary>
/// <remarks>
/// Headers honoured (all optional; absence => anonymous):
/// <list type="bullet">
///   <item><c>X-Test-Authenticated: true</c> — produce an authenticated identity.</item>
///   <item><c>X-Test-Portal: host|tenant</c> — the <c>portal</c> claim.</item>
///   <item><c>X-Test-Role: platform-operator</c> — a role claim.</item>
///   <item><c>X-Test-Tenant: {id}</c> — a <c>tenant_id</c> claim.</item>
/// </list>
/// </remarks>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headers = Request.Headers;

        if (!string.Equals(headers["X-Test-Authenticated"], "true", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult()); // anonymous

        var claims = new List<Claim>();

        if (headers.TryGetValue("X-Test-Portal", out var portal) && !string.IsNullOrEmpty(portal))
            claims.Add(new Claim(AuthClaimTypes.Portal, portal.ToString()));

        if (headers.TryGetValue("X-Test-Role", out var role) && !string.IsNullOrEmpty(role))
            claims.Add(new Claim(ClaimTypes.Role, role.ToString()));

        if (headers.TryGetValue("X-Test-Tenant", out var tenant) && !string.IsNullOrEmpty(tenant))
            claims.Add(new Claim(AuthClaimTypes.TenantId, tenant.ToString()));

        claims.Add(new Claim(AuthClaimTypes.Subject, "test-subject"));

        var identity = new ClaimsIdentity(claims, SchemeName, AuthClaimTypes.Subject, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
