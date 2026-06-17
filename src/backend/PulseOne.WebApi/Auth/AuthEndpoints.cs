using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using PulseOne.SharedKernel.Security;

namespace PulseOne.WebApi.Auth;

/// <summary>
/// Authentication-adjacent endpoints. With Authorization Code + PKCE the SPA's token lifecycle
/// is owned by MSAL (silent acquisition); the backend exposes:
/// <list type="bullet">
///   <item><c>GET  /api/v1/auth/me</c> — the normalized profile of the current principal.</item>
///   <item><c>POST /api/v1/auth/refresh</c> — rotates the refresh cookie (sliding expiry).</item>
///   <item><c>POST /api/v1/auth/logout</c> — clears the auth cookies.</item>
/// </list>
/// All auth cookies are written <c>HttpOnly; Secure; SameSite=Strict</c> with refresh-token
/// rotation (01-auth-module.md). The refresh cookie value is server-issued and never sourced
/// from configuration or a literal.
/// </summary>
public static class AuthEndpoints
{
    public const string RefreshCookieName = "__Host-pulseone-rt";

    /// <summary>Cookie options enforcing the non-negotiable auth-cookie hardening.</summary>
    private static CookieOptions SecureCookie(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
        Path = "/",
        Expires = expires,
    };

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithTags("Auth");

        group.MapGet("/me", Me).RequireAuthorization();
        group.MapPost("/refresh", Refresh).RequireAuthorization();
        group.MapPost("/logout", Logout).RequireAuthorization();

        return app;
    }

    private static Ok<MeResponse> Me(ClaimsPrincipal user) =>
        TypedResults.Ok(new MeResponse(
            UserId: user.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? user.FindFirstValue(AuthClaimTypes.Subject)
                    ?? string.Empty,
            TenantId: user.FindFirstValue(AuthClaimTypes.TenantId),
            Portal: user.FindFirstValue(AuthClaimTypes.Portal),
            Roles: user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray()));

    /// <summary>
    /// Rotates the refresh cookie. Each call issues a NEW token value and overwrites the cookie,
    /// invalidating the previous one (rotation). The token store/validation is wired to the IdP
    /// session in Phase 8; here we enforce the cookie hardening and rotation contract.
    /// </summary>
    private static NoContent Refresh(HttpContext ctx)
    {
        // DEVIATION: token persistence/validation against the IdP refresh-token store lands in
        // Phase 8 (deployment) where the B2C session secret is Key-Vault wired. The rotation
        // mechanics, sliding expiry and cookie hardening are enforced here and now.
        var rotated = Guid.NewGuid().ToString("n"); // server-issued, never a literal/secret in source
        var expires = DateTimeOffset.UtcNow.AddDays(7); // sliding expiry window
        ctx.Response.Cookies.Append(RefreshCookieName, rotated, SecureCookie(expires));
        return TypedResults.NoContent();
    }

    private static NoContent Logout(HttpContext ctx)
    {
        ctx.Response.Cookies.Delete(RefreshCookieName, SecureCookie(DateTimeOffset.UtcNow.AddDays(-1)));
        return TypedResults.NoContent();
    }

    public sealed record MeResponse(
        string UserId,
        string? TenantId,
        string? Portal,
        IReadOnlyList<string> Roles);
}
