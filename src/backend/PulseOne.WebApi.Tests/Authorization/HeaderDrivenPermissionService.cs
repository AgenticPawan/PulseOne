using Microsoft.AspNetCore.Http;
using PulseOne.Application.Authorization;

namespace PulseOne.WebApi.Tests.Authorization;

/// <summary>
/// Test <see cref="IPermissionService"/> that grants exactly the permissions named in the per-request
/// <c>X-Test-Permissions</c> header (comma-separated). This lets the PBAC endpoint tests drive the
/// REAL <c>PermissionAuthorizationHandler</c> + the REAL per-permission policies over the REAL
/// endpoint group, while standing in for the role/permission tables — production code under test is
/// the authorization plumbing, not the DB read. Fail-closed: no header / no context grants nothing.
/// </summary>
public sealed class HeaderDrivenPermissionService(IHttpContextAccessor accessor) : IPermissionService
{
    public const string HeaderName = "X-Test-Permissions";

    public Task<bool> HasPermissionAsync(
        string? userId, string? tenantId, string permission, CancellationToken ct = default) =>
        Task.FromResult(Granted().Contains(permission));

    public Task<IReadOnlySet<string>> GetPermissionsAsync(
        string userId, string tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlySet<string>>(Granted());

    private HashSet<string> Granted()
    {
        var header = accessor.HttpContext?.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(header))
            return new HashSet<string>(StringComparer.Ordinal);

        return header
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }
}
