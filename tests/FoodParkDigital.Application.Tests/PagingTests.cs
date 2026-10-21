using FoodParkDigital.Application.Common.Models;

namespace FoodParkDigital.Application.Tests;

public class PagingTests
{
    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-5, 20, 1, 20)]
    [InlineData(3, 1_000_000, 3, Paging.MaxPageSize)]
    [InlineData(2, 50, 2, 50)]
    [InlineData(int.MaxValue, Paging.MaxPageSize, Paging.MaxPageNumber, Paging.MaxPageSize)]
    public void Normalize_ShouldClampPageNumberAndSize(int pageNumber, int pageSize, int expectedPage, int expectedSize)
    {
        var (page, size) = Paging.Normalize(pageNumber, pageSize);

        Assert.Equal(expectedPage, page);
        Assert.Equal(expectedSize, size);
        Assert.True((page - 1) * size >= 0);
    }

    [Fact]
    public void PaginatedList_ShouldNotDivideByZero_WhenPageSizeIsZero()
    {
        var list = new PaginatedList<int>([], 10, 0, 0);

        Assert.Equal(1, list.PageNumber);
        Assert.Equal(10, list.TotalPages);
    }
}
