namespace PulseOne.SharedKernel.Security;

/// <summary>
/// Authorization policy names. The host boundary is enforced server-side via
/// <see cref="HostOperatorsOnly"/> (CLAUDE.md security rule #4) — the Angular
/// router guard is UI-only and never the sole gate.
/// </summary>
public static class AuthorizationPolicies
{
    public const string HostOperatorsOnly = "HostOperatorsOnly";
}

/// <summary>
/// Named rate-limit policies. The webhook endpoint and auth endpoints are
/// throttled independently (CLAUDE.md security rules #5/#6).
/// </summary>
public static class RateLimitPolicies
{
    public const string Webhook = "webhook";
    public const string Auth = "auth";
}
