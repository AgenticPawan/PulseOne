using System.ComponentModel.DataAnnotations;

namespace PulseOne.WebApi.Auth;

/// <summary>
/// Bound from the <c>AzureAd</c> configuration section. Every value is supplied via Key Vault /
/// environment — NEVER hardcoded, and there is intentionally NO client-secret property: the SPAs
/// use Authorization Code + PKCE (public clients), so the API only ever VALIDATES tokens.
/// </summary>
public sealed class AzureAdOptions
{
    public const string SectionName = "AzureAd";

    /// <summary>OIDC authority (issuer). e.g. the B2C user-flow metadata endpoint.</summary>
    [Required]
    public string Authority { get; init; } = default!;

    /// <summary>Expected token audience (the API's application/client id).</summary>
    [Required]
    public string Audience { get; init; } = default!;

    /// <summary>
    /// Optional explicit issuer override. When null, the issuer is taken from the authority's
    /// discovery document. Used when B2C issues tokens with a tenant-scoped issuer.
    /// </summary>
    public string? ValidIssuer { get; init; }
}
