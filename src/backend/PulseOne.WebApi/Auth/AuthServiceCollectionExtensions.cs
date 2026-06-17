using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using PulseOne.Application.Authorization;
using PulseOne.CoreDomain.Authorization;
using PulseOne.Infrastructure.Authorization;
using PulseOne.SharedKernel.Domain;
using PulseOne.SharedKernel.Security;

namespace PulseOne.WebApi.Auth;

/// <summary>
/// Composition of Phase 1 auth: OIDC/JWT bearer validation, claims normalization, the
/// server-side host boundary, and the PBAC policy machinery.
/// </summary>
public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddPulseOneAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bind + validate AzureAd options. Fail-fast at startup if authority/audience are absent.
        services.AddOptions<AzureAdOptions>()
            .Bind(configuration.GetSection(AzureAdOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var azureAd = configuration.GetSection(AzureAdOptions.SectionName).Get<AzureAdOptions>();

        services.AddHttpContextAccessor();

        // ICurrentUser over the request principal (scoped — one per request).
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        // Normalize IdP claims (extension_tenant_id -> tenant_id) after token validation.
        services.AddSingleton<IClaimsTransformation, TenantClaimsTransformer>();

        // PBAC service + handler.
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // The dynamic permission policy provider (perm:{name}) sits alongside the static policies.
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                // Authority/Audience come from Key Vault-backed configuration — never literals.
                o.Authority = azureAd?.Authority;
                o.Audience = azureAd?.Audience;
                o.MapInboundClaims = false; // keep raw claim names (sub, tenant_id) — no .NET remap.

                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = AuthClaimTypes.Subject,
                    RoleClaimType = ClaimTypes.Role,
                };

                if (!string.IsNullOrWhiteSpace(azureAd?.ValidIssuer))
                    o.TokenValidationParameters.ValidIssuer = azureAd.ValidIssuer;
            });

        services.AddAuthorizationBuilder()
            // Server-side host-portal boundary — the authoritative gate (security rule #4).
            .AddPolicy(AuthorizationPolicies.HostOperatorsOnly, p => p
                .RequireAuthenticatedUser()
                .RequireClaim(AuthClaimTypes.Portal, AuthClaimValues.HostPortal)
                .RequireRole(AuthClaimValues.PlatformOperatorRole));

        // Eagerly register one policy per permission constant (loop, not manual calls).
        services.Configure<AuthorizationOptions>(options =>
        {
            foreach (var permission in Permissions.AllNames)
            {
                options.AddPolicy(
                    PermissionPolicyProvider.PolicyName(permission),
                    p => p.RequireAuthenticatedUser()
                          .AddRequirements(new PermissionRequirement(permission)));
            }
        });

        return services;
    }
}
