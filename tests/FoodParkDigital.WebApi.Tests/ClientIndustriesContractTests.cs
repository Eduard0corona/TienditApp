using System.Text.RegularExpressions;
using FoodParkDigital.Domain.Tenants;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// A giro is the text the sign-up picks from the client's INDUSTRY_OPTIONS (client/src/utils/industries.ts), and the
/// server recognizes a trade by that text (Fase 29A, ADR 0053): its category, its visits and its branch without premises.
/// A trade the list does not offer could never be picked, and one written differently would sign up as any other giro.
/// </summary>
public class ClientIndustriesContractTests
{
    [Fact]
    public void TheSignUpList_ShouldOfferEveryTrade_AsTheServerWritesIt()
    {
        var options = ReadIndustryOptions();

        Assert.All(HomeTrades.All, giro => Assert.Contains(giro, options));
        Assert.Equal(HomeTrades.All, options.Where(HomeTrades.IsHomeTrade));
    }

    private static List<string> ReadIndustryOptions()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "client", "src", "utils", "industries.ts"));
        var list = Regex.Match(source, @"export const INDUSTRY_OPTIONS = \[(?<items>.*?)\];", RegexOptions.Singleline);
        Assert.True(list.Success, "INDUSTRY_OPTIONS was not found in client/src/utils/industries.ts.");
        return Regex.Matches(list.Groups["items"].Value, @"'([^']+)'").Select(match => match.Groups[1].Value).ToList();
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FoodParkDigital.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("FoodParkDigital.sln was not found above the test output.");
    }
}
