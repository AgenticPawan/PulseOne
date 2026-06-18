using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace PulseOne.BackgroundWorker.Storage;

/// <summary>
/// Uploads a generated report to a tenant-scoped blob container and returns a time-limited SAS URL
/// (02-report-worker.md "Excel Export" / constraints). Containers are per-tenant
/// (<c>reports-{tenantId}</c>) so the worker can NEVER write to another tenant's container, and the
/// returned URL is always a short-lived SAS — never a permanent URL.
/// </summary>
public interface IReportBlobStore
{
    /// <summary>
    /// Upload <paramref name="content"/> to <c>reports-{tenantId}/{reportId}/{timestamp}.{ext}</c>
    /// and return a read-only SAS URL valid for 24 hours.
    /// </summary>
    Task<string> UploadAsync(
        string tenantId,
        string reportId,
        string fileExtension,
        string contentType,
        Stream content,
        CancellationToken ct);
}

/// <summary>
/// Azure Blob Storage implementation. The <see cref="BlobServiceClient"/> is built from a Key
/// Vault-backed connection string; SAS generation requires a shared-key credential, which the
/// connection-string client carries.
/// </summary>
/// <remarks>
/// SAS expiry is fixed at 24h read-only (constraint). The tenant id is sanitized into a valid blob
/// container name; a leading constant prefix guarantees the per-tenant container can never collide
/// with another tenant's by construction.
/// </remarks>
public sealed class ReportBlobStore(BlobServiceClient client) : IReportBlobStore
{
    private static readonly TimeSpan SasLifetime = TimeSpan.FromHours(24);

    public async Task<string> UploadAsync(
        string tenantId,
        string reportId,
        string fileExtension,
        string contentType,
        Stream content,
        CancellationToken ct)
    {
        var container = client.GetBlobContainerClient(ContainerName(tenantId));

        // Private container — access is only ever via the SAS URL we mint below.
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

        var blobName = $"{reportId}/{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.{fileExtension}";
        var blob = container.GetBlobClient(blobName);

        if (content.CanSeek)
            content.Position = 0;

        await blob.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            ct);

        if (!blob.CanGenerateSasUri)
            throw new InvalidOperationException(
                "The blob client cannot generate a SAS URI. A shared-key (connection-string) " +
                "credential is required to mint the time-limited download link.");

        var sas = new BlobSasBuilder
        {
            BlobContainerName = blob.BlobContainerName,
            BlobName = blob.Name,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.Add(SasLifetime),
        };
        sas.SetPermissions(BlobSasPermissions.Read); // read-only.

        return blob.GenerateSasUri(sas).ToString();
    }

    /// <summary>
    /// Maps a tenant id to its container name. Azure container names are lowercase, 3-63 chars,
    /// alphanumeric + single hyphens. We lower-case, replace invalid chars, and prefix so the
    /// container is unambiguously tenant-scoped.
    /// </summary>
    private static string ContainerName(string tenantId)
    {
        var sanitized = new string(tenantId
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray());

        var name = $"reports-{sanitized}";
        return name.Length <= 63 ? name : name[..63];
    }
}
