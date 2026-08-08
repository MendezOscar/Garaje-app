namespace Garaj.Application.Common;

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

/// <summary>
/// Paginación de las listas. El tope de 200 es deliberado: sin él, una petición con
/// pageSize=100000 traería el histórico completo del taller en una sola consulta.
/// </summary>
public record PageQuery
{
    private const int MaxPageSize = 200;

    public int Page { get; init; } = 1;

    private readonly int _pageSize = 25;
    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value switch
        {
            < 1 => 25,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    public int Skip => (Math.Max(Page, 1) - 1) * PageSize;
}
