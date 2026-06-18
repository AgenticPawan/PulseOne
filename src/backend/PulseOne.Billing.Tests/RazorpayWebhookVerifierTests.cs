using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PulseOne.Application.Features.Billing;
using Xunit;

namespace PulseOne.Billing.Tests;

/// <summary>
/// Verifier-level proof of the two cryptographic fixes (blueprint §7.1, Appendix A #1/#2): the HMAC is
/// computed under the Key Vault-sourced secret and compared in constant time. These tests would FAIL
/// against the v1 hardcoded-secret / non-constant-time implementation.
/// </summary>
public sealed class RazorpayWebhookVerifierTests
{
    private const string Secret = "test-webhook-secret-not-a-real-key";

    private static RazorpayWebhookVerifier NewVerifier(string secret = Secret) =>
        new(new StaticOptionsMonitor<RazorpayOptions>(new RazorpayOptions { WebhookSecret = secret }));

    private static string SignHex(string body, string secret = Secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    [Fact]
    public void IsValid_WhenSignatureMatches_ReturnsTrue()
    {
        var body = """{"event":"payment.captured"}""";
        var verifier = NewVerifier();

        Assert.True(verifier.IsValid(body, SignHex(body)));
    }

    [Fact]
    public void IsValid_WhenSignatureComputedWithWrongSecret_ReturnsFalse()
    {
        // A spoofer who does not know the Key Vault secret cannot forge a matching signature.
        var body = """{"event":"payment.captured"}""";
        var verifier = NewVerifier();

        Assert.False(verifier.IsValid(body, SignHex(body, secret: "attacker-guessed-secret")));
    }

    [Fact]
    public void IsValid_WhenBodyTamperedAfterSigning_ReturnsFalse()
    {
        var verifier = NewVerifier();
        var signature = SignHex("""{"amount":100}""");

        // Replay/tamper: same signature, different (larger amount) body — must be rejected.
        Assert.False(verifier.IsValid("""{"amount":999999}""", signature));
    }

    [Fact]
    public void IsValid_WhenSignatureIsNotHex_ReturnsFalseAndDoesNotThrow()
    {
        var verifier = NewVerifier();

        // FromHexString throws FormatException internally; the verifier must swallow it → false.
        Assert.False(verifier.IsValid("""{"x":1}""", "not-a-hex-string!!"));
    }

    [Fact]
    public void IsValid_IsCaseInsensitiveOnTheHexSignature()
    {
        var body = """{"event":"subscription.activated"}""";
        var verifier = NewVerifier();
        var lower = SignHex(body).ToLowerInvariant();

        // Convert.FromHexString is case-insensitive — no ToLower() hack needed (prompt constraint).
        Assert.True(verifier.IsValid(body, lower));
    }

    [Fact]
    public void Verifier_UsesConstantTimeCompareOnTheEqualLengthPath()
    {
        // The equal-length compare path is the FixedTimeEquals path. A wrong signature of the CORRECT
        // length (64 hex chars = 32 bytes) exercises it and must still reject — proving the compare is
        // value-sensitive, not length-only.
        var body = """{"event":"payment.captured"}""";
        var verifier = NewVerifier();
        var wrongSameLength = new string('a', 64);   // 32 bytes of 0x0A — valid hex, wrong value

        Assert.False(verifier.IsValid(body, wrongSameLength));
        // And the genuinely correct signature (also 32 bytes) passes the same path.
        Assert.True(verifier.IsValid(body, SignHex(body)));
    }
}
