namespace PulseOne.SharedKernel.Paging;

/// <summary>A single page of results plus total count for the query.</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);

/// <summary>Inbound paging/sorting/search parameters (records per CLAUDE.md style).</summary>
public sealed record PagingParams(
    int PageNumber = 1,
    int PageSize = 20,
    string? SearchTerm = null,
    string SortColumn = "id",
    string SortOrder = "asc");
