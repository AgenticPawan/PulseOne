using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PulseOne.Application.Features.HostAdmin;

namespace PulseOne.WebApi.Tests.Authorization;

/// <summary>
/// Boots the REAL producer API in-process for host-boundary tests. It swaps ONLY the authentication
/// scheme (test handler instead of Azure AD B2C JWT bearer) so the genuine <c>HostOperatorsOnly</c>
/// policy and the genuine <c>/api/v1/host/*</c> endpoint group are exercised. The host admin service
/// is stubbed because authorization runs BEFORE the endpoint handler — for the deny cases it is never
/// invoked, and for the allow case the stub keeps the test off any Azure SQL dependency.
/// </summary>
public sealed class HostBoundaryWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Minimal config so options that ValidateOnStart (AzureAd authority/audience) bind to a value.
        // No real authority is contacted — the test auth scheme replaces JWT bearer entirely.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Authority"] = "https://login.test.local/",
                ["AzureAd:Audience"] = "api://pulseone-test",
                ["AzureAd:ValidIssuer"] = "https://login.test.local/",
                ["ConnectionStrings:TenantCatalog"] = "Server=(localdb)\\mssqllocaldb;Database=PulseOne_TestCatalog;Trusted_Connection=True;",
                ["ConnectionStrings:Hangfire"] = "Server=(localdb)\\mssqllocaldb;Database=PulseOne_TestHangfire;Trusted_Connection=True;",
                ["ConnectionStrings:Redis"] = "localhost:6379",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace the default (JWT bearer) authentication scheme with the test handler. Making it
            // the default ensures RequireAuthenticatedUser() inside HostOperatorsOnly resolves against it.
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });

            // Stub the cross-tenant service so the allow path returns a fixed result without Azure SQL.
            services.RemoveAll<IHostAdminService>();
            services.AddScoped<IHostAdminService, StubHostAdminService>();

            // The producer registers a zero-worker Hangfire server as an IHostedService that connects
            // to Azure SQL on StartAsync. The boundary tests assert authorization only and must not
            // touch a real backplane, so drop every hosted service (Hangfire's is the only one the
            // producer adds, and it is registered via a factory so it can't be matched by type). The
            // deny/anon tests never reach the handlers that use IBackgroundJobClient / JobStorage; the
            // allow test hits only the stubbed IHostAdminService.
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                services.Remove(hosted);
        });
    }
}
