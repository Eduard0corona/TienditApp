using FoodParkDigital.Application.Catalog.Commands;

namespace FoodParkDigital.Application.Tests;

/// <summary>Creating a category asks for the same as editing it: a name, a description that fits and an order from zero.</summary>
public class CategoryCommandValidationTests
{
    [Theory]
    [InlineData(501, 0, "Description")]
    [InlineData(0, -1, "SortOrder")]
    public void CreatingOrEditingACategory_ShouldRefuseALongDescriptionAndANegativeOrder(int descriptionLength, int sortOrder, string field)
    {
        var description = new string('a', descriptionLength);

        var create = new CreateCategoryCommandValidator().Validate(
            new CreateCategoryCommand { Name = "Bebidas", Description = description, SortOrder = sortOrder });
        var update = new UpdateCategoryCommandValidator().Validate(
            new UpdateCategoryCommand { Id = Guid.NewGuid(), Name = "Bebidas", Description = description, SortOrder = sortOrder });

        Assert.Equal(field, Assert.Single(create.Errors).PropertyName);
        Assert.Equal(field, Assert.Single(update.Errors).PropertyName);
    }

    [Fact]
    public void CreatingOrEditingACategory_ShouldAcceptTheLongestDescriptionAndOrderZero()
    {
        var description = new string('a', 500);

        Assert.True(new CreateCategoryCommandValidator().Validate(
            new CreateCategoryCommand { Name = "Bebidas", Description = description, SortOrder = 0 }).IsValid);
        Assert.True(new UpdateCategoryCommandValidator().Validate(
            new UpdateCategoryCommand { Id = Guid.NewGuid(), Name = "Bebidas", Description = description, SortOrder = 0 }).IsValid);
    }
}
