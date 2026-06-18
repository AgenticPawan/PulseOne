using MediatR;
using Microsoft.Extensions.Options;
using PulseOne.Application.Features.Billing;
using PulseOne.Application.Features.Billing.Commands;
using PulseOne.SharedKernel.Security;

namespace PulseOne.WebApi.Endpoints;

/// <summary>
/// Producer-side billing endpoints (blueprint §6.3 / §6.5). Hosts the Razorpay webhook ingest
/// (verify → dedup → fast-ack → enqueue) and the public-config endpoint that hands the SPA the
/// PUBLISHABLE checkout key id so the front-end never hardcodes it (Appendix A defects #1, #6, #12).
/// </summary>
public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // --- Razorpay webhook ingest (blueprint §6.3) ----------------------------------------------
        // Reads the RAW request body straight from the stream — NOT via model binding. The HMAC is
        // computed over the exact bytes Razorpay sent; any deserialize→reserialize round-trip would
        // re-order JSON keys and change whitespace, invalidating the signature.
        app.MapPost("/api/v1/billing/razorpay/webhook", async (HttpRequest http, IMediator mediator) =>
        {
            // 'using var' so the reader (and the underlying body stream lease) is released promptly.
            using var reader = new StreamReader(http.Body);
            var rawBody = await reader.ReadToEndAsync(http.HttpContext.RequestAborted);
            var signature = http.Headers["X-Razorpay-Signature"].ToString();
            var eventId = http.Headers["X-Razorpay-Event-Id"].ToString();

            var outcome = await mediator.Send(
                new ProcessRazorpayWebhookCommand(rawBody, signature, eventId),
                http.HttpContext.RequestAborted);

            // Always 200 on Verified/Duplicate so Razorpay stops retrying; 400 ONLY on a real signature
            // failure (a spoofed delivery we refuse to ack).
            return outcome is WebhookOutcome.InvalidSignature ? Results.BadRequest() : Results.Ok();
        })
        .RequireRateLimiting(RateLimitPolicies.Webhook)   // 100 req/min — blunts webhook-flood (security rule)
        .AllowAnonymous();                                 // authenticity is the HMAC signature, not a session

        // --- Server-authoritative payment verification (blueprint §6.5) ----------------------------
        // The SPA posts the Razorpay checkout callback here. The client result is UNTRUSTED until the
        // server recomputes the signature with the Key Vault-sourced key secret (constant-time). This
        // is the authoritative payment confirmation — never the browser's word.
        app.MapPost("/api/v1/billing/verify-payment",
            (VerifyPaymentRequest body, IRazorpayPaymentVerifier verifier) =>
            {
                var valid = verifier.IsValid(
                    body.RazorpayOrderId, body.RazorpayPaymentId, body.RazorpaySignature);

                return valid
                    ? Results.Ok(new { verified = true })
                    : Results.BadRequest(new { verified = false });
            })
            .RequireAuthorization();   // a logged-in tenant user confirms their own checkout

        // --- Public config (blueprint §6.5) --------------------------------------------------------
        // Exposes ONLY the publishable Razorpay key id so the Angular SPA fetches it at runtime instead
        // of hardcoding it (Appendix A #6). The WebhookSecret is NEVER surfaced here.
        app.MapGet("/api/v1/config/public", (IOptionsSnapshot<RazorpayOptions> opts) =>
            Results.Ok(new PublicConfigResponse(opts.Value.KeyId)))
        .AllowAnonymous();

        return app;
    }
}

/// <summary>
/// Public, non-secret runtime configuration served to the SPA. Carries ONLY the publishable Razorpay
/// checkout key id — never the webhook secret (blueprint §6.5).
/// </summary>
/// <param name="RazorpayKeyId">The publishable Razorpay checkout key id.</param>
public sealed record PublicConfigResponse(string RazorpayKeyId);

/// <summary>
/// The Razorpay checkout callback fields the SPA forwards for server-side verification (blueprint §6.5).
/// </summary>
public sealed record VerifyPaymentRequest(
    string RazorpayPaymentId,
    string RazorpayOrderId,
    string RazorpaySignature);
