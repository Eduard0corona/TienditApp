using FoodParkDigital.Infrastructure.Tests.Support;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The [SqlServerFact] tests of this project run only in the Migrations job of CI, which picks them by the name of their
/// class: one in a class named otherwise is skipped in every job without failing anywhere. Infrastructure.Tests checks
/// its own.
/// </summary>
public class SqlServerFactPlacementTests
{
    [Fact]
    public void SqlServerFacts_ShouldBeInClassesTheMigrationsJobRuns()
    {
        var tests = SqlServerFactAttribute.TestsIn(typeof(SqlServerFactPlacementTests).Assembly);

        var skippedEverywhere = tests
            .Where(test => !test.TestClass.Name.EndsWith(SqlServerFactAttribute.ClassNameSuffix, StringComparison.Ordinal))
            .Select(test => $"{test.TestClass.FullName}.{test.Method.Name}")
            .ToList();

        Assert.NotEmpty(tests);
        Assert.True(skippedEverywhere.Count == 0,
            $"[SqlServerFact] tests that CI never runs: move them to a class whose name ends in {SqlServerFactAttribute.ClassNameSuffix}. "
            + string.Join(", ", skippedEverywhere));
    }
}
