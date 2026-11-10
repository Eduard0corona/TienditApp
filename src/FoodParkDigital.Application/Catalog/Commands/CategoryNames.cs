using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Catalog.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Catalog.Commands;

/// <summary>
/// A name is in one category of a business at most, archived ones included, whatever its capitals and spaces
/// (<see cref="Category.NameKey"/>). The unique index (TenantId, Name) backs it when two saves meet.
/// </summary>
public static class CategoryNames
{
    public static string Taken(string name, bool isActive) => isActive
        ? $"Ya tienes una categoría llamada «{name}». Ponle otro nombre."
        : $"Ya tienes una categoría archivada llamada «{name}». Restáurala o ponle otro nombre.";

    /// <summary>
    /// Why <paramref name="name"/> cannot be the name of a category of the business in scope other than
    /// <paramref name="exceptId"/>, naming the one that has it; null when it can.
    /// </summary>
    internal static async Task<string?> FindTakenAsync(
        IApplicationDbContext context, string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var key = Category.NameKey(name);
        // A business has at most Category.MaxCategoriesPerTenant: their names are compared here, with the key the
        // templates use too, whatever the collation of the database.
        var others = await context.Categories
            .AsNoTracking()
            .Where(category => exceptId == null || category.Id != exceptId)
            .Select(category => new { category.Name, category.IsActive })
            .ToListAsync(cancellationToken);

        var taken = others
            .Where(category => Category.NameKey(category.Name) == key)
            .OrderByDescending(category => category.IsActive)
            .ThenBy(category => category.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (taken is not null)
            return Taken(taken.Name, taken.IsActive);

        // The unique index compares with the collation of the database, which takes as the same more than the key (a
        // full-width letter, «ß» and «ss»): it is asked too, so a save it would refuse names the category instead of failing.
        var equal = await context.Categories
            .AsNoTracking()
            .Where(category => (exceptId == null || category.Id != exceptId) && category.Name == name)
            .OrderByDescending(category => category.IsActive)
            .Select(category => new { category.Name, category.IsActive })
            .FirstOrDefaultAsync(cancellationToken);
        return equal is null ? null : Taken(equal.Name, equal.IsActive);
    }

    /// <summary>Whether the database has a category of the business in scope with <paramref name="name"/> by its collation.</summary>
    internal static Task<bool> DatabaseHasAsync(IApplicationDbContext context, string name, CancellationToken cancellationToken)
        => context.Categories.AsNoTracking().AnyAsync(category => category.Name == name, cancellationToken);
}
