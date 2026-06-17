namespace PulseOne.SharedKernel.Security;

/// <summary>
/// Normalized claim type names used across PulseOne after the
/// <c>TenantClaimsTransformer</c> has run. JWTs from Azure AD B2C carry provider-specific
/// claim names (e.g. <c>extension_tenant_id</c>); the transformer maps them onto these
/// stable, short names so the rest of the system never depends on the IdP's wire format.
/// </summary>
public static class AuthClaimTypes
{
    /// <summary>The tenant the principal belongs to. Consumed by <c>TenantResolutionMiddleware</c>.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>Which portal the principal authenticated against: <c>tenant</c> or <c>host</c>.</summary>
    public const string Portal = "portal";

    /// <summary>The subject (stable user id) — the OIDC <c>sub</c> claim.</summary>
    public const string Subject = "sub";

    /// <summary>Source claim on Azure AD B2C carrying the tenant id (custom attribute).</summary>
    public const string B2CTenantId = "extension_tenant_id";
}

/// <summary>Canonical values for the <see cref="AuthClaimTypes.Portal"/> claim and roles.</summary>
public static class AuthClaimValues
{
    public const string HostPortal = "host";
    public const string TenantPortal = "tenant";

    /// <summary>Role required (in addition to <c>portal=host</c>) by <c>HostOperatorsOnly</c>.</summary>
    public const string PlatformOperatorRole = "platform-operator";
}
