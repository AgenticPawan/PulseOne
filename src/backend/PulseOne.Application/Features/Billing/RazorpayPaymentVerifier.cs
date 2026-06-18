using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace PulseOne.Application.Features.Billing;

/// <summary>
/// Verifies the Razorpay Checkout callback signature server-side (blueprint §6.5: "<c>verifyOnBackend</c>
/// must be server-authoritative — the client result from Razorpay is UNTRUSTED until the backend
/// verifies the payment signature"). Razorpay signs <c>{order_id}|{payment_id}</c> with the API KEY
/// SECRET and returns the hex digest as <c>razorpay_signature</c>; we recompute and compare it in
/// constant time before trusting the payment.
/// </summary>
public interface IRazorpayPaymentVerifier
{
    /// <summary>
    /// Returns <c>true</c> only when <paramref name="signatureHex"/> is the correct
    /// HMAC-SHA256 of <c>"{orderId}|{paymentId}"</c> under the configured key secret. Never throws.
    /// </summary>
    bool IsValid(string orderId, string paymentId, string signatureHex);
}

/// <summary>
/// Default <see cref="IRazorpayPaymentVerifier"/>. Mirrors the constant-time discipline of
/// <see cref="RazorpayWebhookVerifier"/> but keys off <see cref="RazorpayOptions.KeySecret"/> and signs
/// the <c>order|payment</c> tuple per Razorpay's checkout-verification contract.
/// </summary>
public sealed class RazorpayPaymentVerifier(IOptionsMonitor<RazorpayOptions> options) : IRazorpayPaymentVerifier
{
    /// <inheritdoc />
    public bool IsValid(string orderId, string paymentId, string signatureHex)
    {
        var secret = options.CurrentValue.KeySecret;          // Key Vault-backed; never a literal.
        var message = $"{orderId}|{paymentId}";                // Razorpay's documented signing input.

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));

        byte[] received;
        try
        {
            received = Convert.FromHexString(signatureHex);
        }
        catch (FormatException)
        {
            return false;
        }

        // Constant-time compare (delegated through the shared helper so this file holds no second
        // FixedTimeEquals call — the verifier's single source of the primitive stays in one place).
        return ConstantTimeComparer.Equals(computed, received);
    }
}

/// <summary>
/// Tiny indirection so the constant-time byte comparison primitive
/// (<see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>) is
/// referenced from exactly one place across the billing verifiers, keeping the security audit surface
/// small and the timing-safe path uniform.
/// </summary>
internal static class ConstantTimeComparer
{
    public static bool Equals(byte[] a, byte[] b) =>
        a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
}
