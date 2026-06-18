using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PulseOne.Application.Features.Billing;
using PulseOne.Application.Features.Billing.Commands;
using Xunit;

namespace PulseOne.Billing.Tests;

/// <summary>
/// The webhook test suite the blueprint §7.1 mandates — proving the three v1 defects are closed:
/// spoofed signatures are rejected (no enqueue), valid deliveries enqueue exactly once, and duplicate
/// event ids are suppressed (acked, NOT re-enqueued). These would all fail against v1.
/// </summary>
public sealed class ProcessRazorpayWebhookHandlerTests
{
    private const string Secret = "test-webhook-secret-not-a-real-key";
    private const string Body = """{"event":"payment.captured","payload":{}}""";

    private static string SignHex(string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    private static (ProcessRazorpayWebhookHandler Handler, RecordingBackgroundJobClient Jobs) NewHandler(
        IWebhookDeduplicationStore? dedupe = null)
    {
        var verifier = new RazorpayWebhookVerifier(
            new StaticOptionsMonitor<RazorpayOptions>(new RazorpayOptions { WebhookSecret = Secret }));
        var jobs = new RecordingBackgroundJobClient();
        var handler = new ProcessRazorpayWebhookHandler(
            verifier,
            dedupe ?? new InMemoryDeduplicationStore(),
            jobs,
            NullLogger<ProcessRazorpayWebhookHandler>.Instance);
        return (handler, jobs);
    }

    [Fact]
    public async Task Rejects_when_signature_invalid()
    {
        var (handler, jobs) = NewHandler();

        var outcome = await handler.Handle(
            new ProcessRazorpayWebhookCommand(Body, Signature: "deadbeef", EventId: "evt_1"),
            CancellationToken.None);

        Assert.Equal(WebhookOutcome.InvalidSignature, outcome);
        Assert.Equal(0, jobs.CreateCount);   // a spoofed delivery must NEVER enqueue work
    }

    [Fact]
    public async Task Verifies_when_signature_valid()
    {
        var (handler, jobs) = NewHandler();

        var outcome = await handler.Handle(
            new ProcessRazorpayWebhookCommand(Body, SignHex(Body), EventId: "evt_2"),
            CancellationToken.None);

        Assert.Equal(WebhookOutcome.Verified, outcome);
        Assert.Equal(1, jobs.CreateCount);   // verified → enqueued exactly once (fast-ack)
    }

    [Fact]
    public async Task Suppresses_duplicate_event_id()
    {
        var dedupe = new InMemoryDeduplicationStore();
        var (handler, jobs) = NewHandler(dedupe);
        var signed = SignHex(Body);

        var first = await handler.Handle(
            new ProcessRazorpayWebhookCommand(Body, signed, EventId: "evt_dup"), CancellationToken.None);
        var second = await handler.Handle(
            new ProcessRazorpayWebhookCommand(Body, signed, EventId: "evt_dup"), CancellationToken.None);

        Assert.Equal(WebhookOutcome.Verified, first);
        Assert.Equal(WebhookOutcome.Duplicate, second);   // Razorpay retry — acked but not re-applied
        Assert.Equal(1, jobs.CreateCount);                // enqueued ONCE across both deliveries
    }
}
