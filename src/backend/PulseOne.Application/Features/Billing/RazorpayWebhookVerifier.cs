using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace PulseOne.Application.Features.Billing;

/// <summary>
/// Strongly-typed Razorpay configuration bound from the Key Vault-backed <c>"Razorpay"</c>
/// configuration section (blueprint §6.3 / §6.4). NONE of these values are source literals — they
/// are sourced at runtime from Azure Key Vault via <c>IConfiguration.AddAzureKeyVault</c> and bound
/// through <see cref="IOptionsMonitor{TOptions}"/> so that a secret rotation in the vault hot-reloads
/// here WITHOUT a redeploy (Appendix A defect #1: "live Razorpay secret hardcoded in source").
/// </summary>
/// <remarks>
/// SECURITY (CLAUDE.md security rule #1, blueprint Appendix A #1):
/// <list type="bullet">
///   <item><see cref="WebhookSecret"/> is the HMAC signing secret. It is NEVER logged, NEVER returned
///         from any endpoint, and NEVER written to source. The public-config endpoint exposes ONLY
///         <see cref="KeyId"/>.</item>
///   <item><see cref="KeyId"/> is the PUBLISHABLE checkout key id (the <c>rzp_…</c> identifier the
///         browser needs to open Razorpay Checkout). It is safe to serve from
///         <c>/api/v1/config/public</c> so the SPA never hardcodes it (Appendix A defect #6).</item>
/// </list>
/// Defaults are empty strings (not nulls) so a missing binding fails closed at verification time
/// (an empty secret can never produce a matching HMAC) rather than NRE-ing deep in the pipeline.
/// </remarks>
public sealed class RazorpayOptions
{
    /// <summary>Configuration section name. Bound via <c>Configuration.GetSection(SectionName)</c>.</summary>
    public const string SectionName = "Razorpay";

    /// <summary>
    /// HMAC-SHA256 webhook signing secret. Key Vault-backed; bound through <see cref="IOptionsMonitor{TOptions}"/>
    /// so rotation hot-reloads. Never a source literal, never exposed by any endpoint.
    /// </summary>
    public string WebhookSecret { get; init; } = "";

    /// <summary>
    /// PUBLISHABLE Razorpay key id (the checkout key the browser uses). Safe to expose via the
    /// public-config endpoint so the Angular SPA fetches it at runtime instead of hardcoding it.
    /// </summary>
    public string KeyId { get; init; } = "";

    /// <summary>
    /// Razorpay API KEY SECRET, used to verify the checkout callback signature server-side
    /// (<c>HMAC(order_id|payment_id)</c>). Key Vault-backed; bound through <see cref="IOptionsMonitor{TOptions}"/>;
    /// NEVER exposed by any endpoint and NEVER a source literal. Distinct from <see cref="WebhookSecret"/>.
    /// </summary>
    public string KeySecret { get; init; } = "";
}

/// <summary>
/// Verifies the authenticity of a Razorpay webhook delivery by recomputing the HMAC-SHA256 of the
/// RAW request body under the Key Vault-sourced secret and comparing it to the value Razorpay sent
/// in the <c>X-Razorpay-Signature</c> header (blueprint §6.3).
/// </summary>
public interface IRazorpayWebhookVerifier
{
    /// <summary>
    /// Returns <c>true</c> only when <paramref name="signatureHex"/> is the correct HMAC-SHA256 of
    /// <paramref name="rawBody"/> under the configured webhook secret. Returns <c>false</c> (never
    /// throws) for any malformed input — a spoofed or corrupt signature must be rejected, not faulted.
    /// </summary>
    /// <param name="rawBody">
    /// The EXACT bytes Razorpay sent, as a UTF-8 string. Must be the raw request body read straight
    /// from the stream — any model-binding round-trip (deserialize → reserialize) would re-order keys
    /// and change whitespace, breaking the signature. See <c>BillingEndpoints</c>.
    /// </param>
    /// <param name="signatureHex">The hex-encoded signature from the <c>X-Razorpay-Signature</c> header.</param>
    bool IsValid(string rawBody, string signatureHex);
}

/// <summary>
/// Default <see cref="IRazorpayWebhookVerifier"/> — copied verbatim from blueprint §6.3 and fixes
/// the two v1 cryptographic defects (Appendix A #1 and #2):
/// <list type="number">
///   <item>The secret is read from <see cref="IOptionsMonitor{TOptions}"/> (<c>CurrentValue</c> picks
///         up the latest rotated value) instead of being a hardcoded literal.</item>
///   <item>Comparison uses <see cref="CryptographicOperations"/>' constant-time fixed-length compare
///         instead of a short-circuiting <c>!=</c>, which leaked a timing oracle that let an attacker
///         recover the signature byte-by-byte.</item>
/// </list>
/// </summary>
public sealed class RazorpayWebhookVerifier(IOptionsMonitor<RazorpayOptions> options) : IRazorpayWebhookVerifier
{
    /// <inheritdoc />
    public bool IsValid(string rawBody, string signatureHex)
    {
        // CurrentValue hot-reloads on Key Vault rotation; never a source literal (Appendix A #1).
        var secret = options.CurrentValue.WebhookSecret;

        // 'using var' guarantees the HMAC's key material is zeroed/disposed even on the early-return
        // paths below (Appendix A #1 fix; prompt constraint: "HMACSHA256 MUST be disposed").
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));

        byte[] received;
        try
        {
            // FromHexString is case-insensitive — do NOT lower-case the header first (prompt
            // constraint). A non-hex or odd-length signature is a malformed delivery → reject (false),
            // do not fault the request thread.
            received = Convert.FromHexString(signatureHex);
        }
        catch (FormatException)
        {
            return false;
        }

        // The constant-time compare requires equal lengths to be meaningful; the length guard
        // short-circuits only on a SIZE mismatch (public information — the digest size is fixed at 32
        // bytes), never on content, so no per-byte timing oracle is introduced (Appendix A #2).
        return received.Length == computed.Length
            && CryptographicOperations.FixedTimeEquals(computed, received);
    }
}
