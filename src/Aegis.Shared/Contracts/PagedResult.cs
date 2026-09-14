namespace Aegis.Shared.Contracts;

using System;
using System.Collections.Generic;

/// <summary>
/// Represents a paginated result.
/// </summary>
public sealed record PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long TotalCount { get; init; }

    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
}
