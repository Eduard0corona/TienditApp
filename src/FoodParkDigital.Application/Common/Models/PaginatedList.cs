using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Common.Models;

public class PaginatedList<T>
{
    public List<T> Items { get; }
    public int PageNumber { get; }
    public int TotalPages { get; }
    public int TotalCount { get; }

    public PaginatedList(List<T> items, int count, int pageNumber, int pageSize)
    {
        (pageNumber, pageSize) = Paging.Normalize(pageNumber, pageSize);
        PageNumber = pageNumber;
        TotalPages = (int)Math.Ceiling(count / (double)pageSize);
        TotalCount = count;
        Items = items;
    }

    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    public static async Task<PaginatedList<T>> CreateAsync(
        IQueryable<T> source, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        (pageNumber, pageSize) = Paging.Normalize(pageNumber, pageSize);

        var count = await source.CountAsync(cancellationToken);
        var items = await source
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PaginatedList<T>(items, count, pageNumber, pageSize);
    }
}

public static class Paging
{
    // The largest page the SPA requests today (reference lists load 500 rows).
    public const int MaxPageSize = 500;

    // Past it, (page - 1) * size no longer fits in an int and the OFFSET turns negative.
    public const int MaxPageNumber = int.MaxValue / MaxPageSize;

    /// <summary>
    /// Clamps client paging values: page numbers go from 1 to <see cref="MaxPageNumber"/> and page sizes are kept between
    /// 1 and <see cref="MaxPageSize"/>, so a request cannot dump a whole table or trigger a negative OFFSET.
    /// </summary>
    public static (int PageNumber, int PageSize) Normalize(int pageNumber, int pageSize) =>
        (Math.Clamp(pageNumber, 1, MaxPageNumber), Math.Clamp(pageSize, 1, MaxPageSize));
}
