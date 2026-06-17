using Microsoft.Extensions.Hosting;

// Hangfire server + KEDA MSSQL scaler land in Phase 3. Phase 0 ships a runnable host shell
// so the worker container builds and the deployment topology is in place.
var builder = Host.CreateApplicationBuilder(args);

// <KEY_VAULT_REFERENCE> builder.Configuration.AddAzureKeyVault(vaultUri, new DefaultAzureCredential());

var host = builder.Build();
await host.RunAsync();
