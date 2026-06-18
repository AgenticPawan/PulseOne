using PulseOne.SharedKernel.Domain;

namespace PulseOne.BackgroundWorker.Jobs;

/// <summary>
/// <see cref="ICurrentUser"/> for background execution. There is no HTTP principal in a worker, so
/// mutations made by a job are audit-stamped as the system actor. The tenant is NOT taken from here
/// — it is resolved from the job's <c>tenantId</c> argument into the scoped <c>ITenantContext</c>,
/// which is what the <c>ApplicationDbContext</c> tenant filter and audit writer read.
/// </summary>
public sealed class WorkerCurrentUser : ICurrentUser
{
    public string UserId => "background-worker";

    // Jobs resolve tenancy from their tenantId argument via ITenantContext, never from here.
    public string? TenantId => null;

    // A worker is not a host operator; it must never bypass tenant scoping.
    public bool IsHostOperator => false;
}
