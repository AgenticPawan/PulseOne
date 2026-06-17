using PulseOne.SharedKernel.Middleware;
using PulseOne.SharedKernel.MultiTenancy;

var builder = WebApplication.CreateBuilder(args);

// Key Vault configuration (Managed Identity) is wired in Phase 1/8. In Phase 0 the
// producer API is a stateless shell that proves the tenant pipeline compiles end-to-end.
//
// <KEY_VAULT_REFERENCE> builder.Configuration.AddAzureKeyVault(vaultUri, new DefaultAzureCredential());

// Fail-closed tenant context — scoped, one per request (blueprint §6.1).
builder.Services.AddScoped<ITenantContext, TenantContext>();

var app = builder.Build();

// app.UseMiddleware<TenantResolutionMiddleware>();  // enabled once auth + catalog DI land (Phase 1/2)

app.MapGet("/health", () => Results.Ok(new { status = "ok", phase = 0 }));

app.Run();

// Exposed for WebApplicationFactory-based integration tests in Phase 7.
public partial class Program;
