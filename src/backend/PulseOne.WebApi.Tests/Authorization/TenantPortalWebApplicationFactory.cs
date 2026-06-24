using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PulseOne.Application.Authorization;
using PulseOne.Application.Features.TenantPortal;
using PulseOne.SharedKernel.MultiTenancy;

namespace PulseOne.WebApi.Tests.Authorization;

/// <summary>
/// Boots the REAL producer API in-process for the tenant-portal PBAC tests. It swaps only the seams
/// that would otherwise require Azure/Redis/SQL, leaving the genuine per-permission policies
/// (<c>PermissionPolicyProvider</c> + <c>PermissionAuthorizationHandler</c>), the genuine
/// <c>/api/v1/{reports,dashboard,team,settings}</c> endpoint group, and the genuine
/// <c>TenantResolutionMiddleware</c> in the path:
/// <list type="bullet">
///   <item>authentication → <see cref="TestAuthHandler"/> (no Azure AD);</item>
///   <item><see cref="IPermissionService"/> → <see cref="HeaderDrivenPermissionService"/> (grants
///         exactly what the request's <c>X-Test-Permissions</c> header names);</item>
///   <item><see cref="ITenantCatalog"/> → <see cref="StubTenantCatalog"/> so tenant resolution
///         succeeds without Redis/SQL;</item>
///   <item><see cref="ITenantPortalService"/> → <see cref="StubTenantPortalService"/> so the allow
///         path returns 2xx without a shard database.</item>
/// </list>
/// </summary>
public sealed class TenantPortalWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
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
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });

            // The header-driven permission stub reads the current request, so it needs the accessor.
            services.AddHttpContextAccessor();
            services.RemoveAll<IPermissionService>();
            services.AddScoped<IPermissionService, HeaderDrivenPermissionService>();

            // Let TenantResolutionMiddleware resolve a tenant without a real catalog backend.
            services.RemoveAll<ITenantCatalog>();
            services.AddSingleton<ITenantCatalog, StubTenantCatalog>();

            // Allow-path returns 2xx without touching a shard database.
            services.RemoveAll<ITenantPortalService>();
            services.AddScoped<ITenantPortalService, StubTenantPortalService>();

            // Drop the producer's zero-worker Hangfire IHostedService (it connects to SQL on start).
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                services.Remove(hosted);
        });
    }
}
