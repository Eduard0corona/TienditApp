using System.Globalization;
using System.Text.RegularExpressions;
using FoodParkDigital.Domain.Inventory.Enums;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The API exchanges enums as numbers (there is no JsonStringEnumConverter), so the client keeps each one as an
/// <c>export const X = { ... } as const</c> object. A copy that drifts makes the screens send and show the wrong
/// values: the inventory movement copies started at 0 while the domain starts at 1, so "Salida" was saved as Entry
/// and added stock. Since the Fase 7 they are generated from the contract (client/src/api/generated/enums.ts, from
/// the names EnumNamesSchemaFilter writes); every exported const named like a domain enum, generated or still
/// written in client/src/types/index.ts, is compared with it, and one defined in both places fails.
/// </summary>
public class ClientEnumContractTests
{
    private static readonly string[] RequiredCopies = [nameof(InventoryMovementType), nameof(InventoryMovementReason)];

    public static TheoryData<string> ClientCopies()
    {
        var domainEnums = DomainEnumsByName();
        var data = new TheoryData<string>();
        foreach (var name in ClientEnumSource.FindConstNames(ReadClientTypes()).Where(domainEnums.ContainsKey))
            data.Add(name);
        return data;
    }

    [Fact]
    public void ClientCopies_ShouldIncludeTheInventoryMovementEnums()
    {
        var names = ClientEnumSource.FindConstNames(ReadClientTypes());

        Assert.All(RequiredCopies, name => Assert.Contains(name, names));
    }

    [Theory]
    [MemberData(nameof(ClientCopies))]
    public void ClientCopy_ShouldMatchTheDomainEnum(string name)
    {
        var domainEnum = DomainEnumsByName()[name];
        var domain = Enum.GetNames(domainEnum)
            .ToDictionary(member => member, member => Convert.ToInt64(Enum.Parse(domainEnum, member), CultureInfo.InvariantCulture));
        var client = ClientEnumSource.ReadConst(ReadClientTypes(), name);
        // The zero of a [Flags] enum (None) is not a value anyone picks; the client may leave it out.
        var clientMayOmitZero = domainEnum.IsDefined(typeof(FlagsAttribute), inherit: false);

        var differences = new List<string>();
        foreach (var (member, value) in domain)
        {
            if (client.TryGetValue(member, out var clientValue))
            {
                if (clientValue != value)
                    differences.Add($"{name}.{member}: domain {value}, client {clientValue}");
            }
            else if (!(clientMayOmitZero && value == 0))
            {
                differences.Add($"{name}.{member}: domain {value}, missing on the client");
            }
        }

        foreach (var (member, clientValue) in client)
        {
            if (!domain.ContainsKey(member))
                differences.Add($"{name}.{member}: client {clientValue}, missing in the domain");
        }

        Assert.Empty(differences);
    }

    [Theory]
    [InlineData("export const Sample = {\n  Entry: 1,\n  Exit: EXIT_VALUE,\n} as const;")]
    [InlineData("export const Sample = {\n  Entry: 1,\n  'Exit': 2,\n} as const;")]
    [InlineData("export const Sample = {\n  Entry: 1,\n  Entry: 2,\n} as const;")]
    [InlineData("export const Sample = {\n  Entry: 1, // see {docs}\n} as const;")]
    [InlineData("export const Sample = makeEnum(['Entry']);")]
    [InlineData("export const Other = {\n  Entry: 1,\n} as const;")]
    public void ReadConst_ShouldFailOnSourceItCannotCompare(string source)
    {
        Assert.Throws<FormatException>(() => ClientEnumSource.ReadConst(source, "Sample"));
    }

    [Fact]
    public void ReadConst_ShouldReadOneOrSeveralEntriesPerLineAndSkipComments()
    {
        const string source = """
            export const Sample = {
              // General
              Unit: 0, Pack: 1,
              Kg: 10, // Food - Weight
              Cancelled: 99,
            } as const;
            """;

        var values = ClientEnumSource.ReadConst(source, "Sample");

        Assert.Equal(
            new Dictionary<string, long> { ["Unit"] = 0, ["Pack"] = 1, ["Kg"] = 10, ["Cancelled"] = 99 },
            values);
    }

    private static Dictionary<string, Type> DomainEnumsByName() =>
        typeof(InventoryMovementType).Assembly.GetExportedTypes()
            .Where(type => type.IsEnum)
            .GroupBy(type => type.Name)
            .ToDictionary(group => group.Key, group => group.Single());

    /// <summary>The generated enums and the hand-written types, as one source (a name in both is two declarations).</summary>
    private static string ReadClientTypes()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FoodParkDigital.sln")))
            {
                var source = Path.Combine(directory.FullName, "client", "src");
                return File.ReadAllText(Path.Combine(source, "api", "generated", "enums.ts"))
                    + "\n"
                    + File.ReadAllText(Path.Combine(source, "types", "index.ts"));
            }
        }

        throw new InvalidOperationException($"FoodParkDigital.sln was not found above {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// Reads numeric <c>export const X = { Key: 1, ... } as const;</c> objects from TypeScript source. Whatever it
    /// cannot compare (another declaration form, computed values, quoted keys, braces) fails instead of being skipped.
    /// </summary>
    private static class ClientEnumSource
    {
        private static readonly Regex ExportedConstPattern = new(
            @"export\s+const\s+(?<name>[A-Za-z_$][\w$]*)\s*=",
            RegexOptions.CultureInvariant);

        private static readonly Regex EntryPattern = new(
            @"^(?<key>[A-Za-z_$][\w$]*)\s*:\s*(?<value>-?\d+)$",
            RegexOptions.CultureInvariant);

        private static readonly Regex LineCommentPattern = new(@"//[^\r\n]*", RegexOptions.CultureInvariant);

        public static IReadOnlyList<string> FindConstNames(string source) =>
            ExportedConstPattern.Matches(source).Select(match => match.Groups["name"].Value).ToList();

        public static Dictionary<string, long> ReadConst(string source, string name)
        {
            var declarations = ExportedConstPattern.Matches(source).Count(match => match.Groups["name"].Value == name);
            var objects = new Regex(
                    $@"export\s+const\s+{Regex.Escape(name)}\s*=\s*\{{(?<body>[^{{}}]*)\}}\s*as\s+const\s*;",
                    RegexOptions.CultureInvariant)
                .Matches(source);
            if (declarations != 1 || objects.Count != 1)
                throw new FormatException($"Expected one 'export const {name} = {{ ... }} as const;' object, found {declarations} declaration(s) and {objects.Count} object(s).");

            var values = new Dictionary<string, long>(StringComparer.Ordinal);
            var body = LineCommentPattern.Replace(objects[0].Groups["body"].Value, string.Empty);
            foreach (var entry in body.Split(',').Select(part => part.Trim()).Where(part => part.Length > 0))
            {
                var parsed = EntryPattern.Match(entry);
                if (!parsed.Success)
                    throw new FormatException($"{name}: unsupported entry '{entry}'.");
                if (!values.TryAdd(parsed.Groups["key"].Value, long.Parse(parsed.Groups["value"].Value, CultureInfo.InvariantCulture)))
                    throw new FormatException($"{name}: '{parsed.Groups["key"].Value}' appears twice.");
            }

            return values;
        }
    }
}
