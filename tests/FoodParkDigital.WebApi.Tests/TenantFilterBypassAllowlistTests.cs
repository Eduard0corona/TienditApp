namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// IgnoreQueryFilters reads the data of every business. It is allowed only where there is no business in context yet
/// (accounts, login, webhooks, background jobs) or for explicit platform reads, and every use is listed here: a new
/// use, or one more in a file, fails until it is reviewed and added with its reason.
/// </summary>
public class TenantFilterBypassAllowlistTests
{
    private static readonly Dictionary<string, int> Approved = new(StringComparer.Ordinal)
    {
        // Identity and session: accounts are not tenant-scoped, and login, refresh, confirmations and the business switch run before or across a business.
        ["src/FoodParkDigital.Application/Common/Utils/MyTenantResolver.cs"] = 2,
        ["src/FoodParkDigital.Application/Identity/Commands/AcceptInvitationCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Commands/ConfirmEmailCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Commands/ConfirmPhoneCommand.cs"] = 2,
        ["src/FoodParkDigital.Application/Identity/Commands/ForgotPasswordCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Commands/LoginCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Commands/PhoneChangeCommands.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Commands/RefreshTokenCommand.cs"] = 3,
        ["src/FoodParkDigital.Application/Identity/Commands/RegisterBusinessCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Commands/RegisterCustomerCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Commands/ResendConfirmationEmailCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Commands/ResendPhoneConfirmationCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Commands/ResetPasswordCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Commands/SmsPasswordResetCommands.cs"] = 2,
        ["src/FoodParkDigital.Application/Identity/Commands/SwitchTenantCommand.cs"] = 2,
        ["src/FoodParkDigital.Application/Identity/Commands/UpdateOwnerWhatsAppConfigCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Queries/GetMyTenantsQuery.cs"] = 2,
        ["src/FoodParkDigital.Application/Identity/Queries/GetOwnerWhatsAppConfigQuery.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Services/AuthSessionIssuer.cs"] = 2,
        // A new business: slugs are unique across the platform, and the owner limit counts the account's businesses.
        ["src/FoodParkDigital.Application/Tenants/BusinessProvisioning.cs"] = 1,
        ["src/FoodParkDigital.Application/Tenants/Commands/CreateBusinessCommand.cs"] = 2,
        // Employees: the account (ApplicationUser) is shared by the businesses of a person.
        ["src/FoodParkDigital.Application/Employees/Commands/CreateEmployeeCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Employees/Commands/ResendEmployeeInvitationCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Employees/Commands/SetEmployeeActiveCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Employees/Commands/UpdateEmployeeCommand.cs"] = 2,
        ["src/FoodParkDigital.Application/Employees/Queries/GetEmployeeByIdQuery.cs"] = 1,
        ["src/FoodParkDigital.Application/Employees/Queries/GetEmployeesQuery.cs"] = 1,
        // Accepting an invitation happens before any business is selected, for every pending invitation of the account.
        ["src/FoodParkDigital.Application/Employees/EmployeeInvitations.cs"] = 2,
        // Platform console and plans: platform-wide reads (the platform admin), and actions that set the target business first.
        ["src/FoodParkDigital.Application/Billing/PlanFeature.cs"] = 5,
        ["src/FoodParkDigital.Application/Billing/PlatformQueries.cs"] = 6,
        ["src/FoodParkDigital.Application/Billing/SubscriptionStatusRefresher.cs"] = 1,
        ["src/FoodParkDigital.Application/Catalog/Queries/GetCategoryTemplatesQuery.cs"] = 1,
        ["src/FoodParkDigital.Application/Tenants/Queries/GetAllTenantsQuery.cs"] = 1,
        // Access, modules and the business's own settings, read before the business is set for the request.
        ["src/FoodParkDigital.Application/Common/Rules/TenantCapabilityRules.cs"] = 2,
        ["src/FoodParkDigital.Infrastructure/Services/TenantAccessService.cs"] = 7,
        // The business's own row, and its WhatsApp number, which must not belong to another business.
        ["src/FoodParkDigital.Application/AiAgents/Commands/CreateAiAgentCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Tenants/Commands/UpdateWhatsAppConfigCommand.cs"] = 1,
        // Webhooks and background jobs: only to find the work or the business it belongs to (a phone_number_id, a
        // webhook token, what is due). The work itself runs in that business's scope (BackgroundScopes).
        ["src/FoodParkDigital.Application/WhatsApp/Queries/VerifyWhatsAppWebhookTokenQuery.cs"] = 1,
        ["src/FoodParkDigital.Infrastructure/BackgroundJobs/BackgroundQueuesHealthCheck.cs"] = 1,
        ["src/FoodParkDigital.Infrastructure/BackgroundJobs/Messaging/MessagingWorker.cs"] = 4,
        ["src/FoodParkDigital.Infrastructure/Services/WhatsAppWebhookProcessor.cs"] = 1,
        // Verification codes by WhatsApp: the platform's account, owned by an active platform admin.
        ["src/FoodParkDigital.Infrastructure/Services/SmsService.cs"] = 2,
        // Customers shared by businesses (registered accounts and temporary customers looked up by phone).
        ["src/FoodParkDigital.Application/Orders/Queries/GetMyOrdersQuery.cs"] = 1, // a customer account's own orders, filtered by its user id
        ["src/FoodParkDigital.Application/Orders/Commands/CreateOrderCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Orders/Commands/CreateTenantOrderCommand.cs"] = 3,
        ["src/FoodParkDigital.Application/Customers/Services/CustomerLookupService.cs"] = 2,
        ["src/FoodParkDigital.Application/Customers/Services/TemporaryCustomerClaimService.cs"] = 2,
        // The folio counters of the business named by the document (also the online menu, which has no business in
        // context): the counter and the folios it continues after, filtered by that tenant.
        ["src/FoodParkDigital.Infrastructure/Services/FolioGenerator.cs"] = 4,
        // Development seed data.
        ["src/FoodParkDigital.Infrastructure/Persistence/Seeds/SeedData.cs"] = 9,
    };

    [Fact]
    public void IgnoreQueryFilters_ShouldOnlyAppearWhereItWasReviewed()
    {
        var root = FindRepositoryRoot();
        var actual = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(path => (Path: Path.GetRelativePath(root, path).Replace('\\', '/'), Count: CountUses(File.ReadAllText(path))))
            .Where(file => file.Count > 0)
            .ToDictionary(file => file.Path, file => file.Count, StringComparer.Ordinal);

        var unexpected = actual
            .Where(file => !Approved.TryGetValue(file.Key, out var approved) || file.Value > approved)
            .Select(file => $"{file.Key}: {file.Value} (aprobados {Approved.GetValueOrDefault(file.Key)})")
            .ToList();
        var stale = Approved
            .Where(file => actual.GetValueOrDefault(file.Key) < file.Value)
            .Select(file => $"{file.Key}: {actual.GetValueOrDefault(file.Key)} (la lista dice {file.Value})")
            .ToList();

        Assert.True(unexpected.Count == 0,
            "IgnoreQueryFilters nuevo sin revisar. Usa el filtro del negocio, o agrégalo a la lista con su motivo: "
            + string.Join("; ", unexpected));
        Assert.True(stale.Count == 0, "Hay menos usos que los de la lista: bájala para que siga siendo exacta. " + string.Join("; ", stale));
    }

    [Theory]
    [InlineData("// IgnoreQueryFilters() here would leak other businesses", 0)]
    [InlineData("/* .IgnoreQueryFilters() */ var x = 1;", 0)]
    [InlineData("/// <see cref=\"IgnoreQueryFilters()\"/>", 0)]
    [InlineData("var url = \"https://example.com\"; query.IgnoreQueryFilters();", 1)]
    [InlineData("var c = '\"'; query.IgnoreQueryFilters(); // and IgnoreQueryFilters() in a comment", 1)]
    [InlineData("query\n    .IgnoreQueryFilters()\n    .IgnoreQueryFilters()", 2)]
    public void CountUses_ShouldCountCodeButNotComments(string source, int expected)
    {
        Assert.Equal(expected, CountUses(source));
    }

    /// <summary>Calls in code; a mention in a comment (an explanation of why the filter stays on) is not a use.</summary>
    private static int CountUses(string source)
    {
        var code = WithoutComments(source);
        var count = 0;
        for (var index = code.IndexOf("IgnoreQueryFilters(", StringComparison.Ordinal);
             index >= 0;
             index = code.IndexOf("IgnoreQueryFilters(", index + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// The source without <c>//</c> and <c>/* */</c> comments. String and character literals are copied as they are,
    /// so a <c>//</c> inside a URL does not start a comment.
    /// </summary>
    private static string WithoutComments(string source)
    {
        var code = new System.Text.StringBuilder(source.Length);
        var index = 0;
        while (index < source.Length)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';
            if (current == '/' && next == '/')
            {
                while (index < source.Length && source[index] != '\n')
                    index++;
            }
            else if (current == '/' && next == '*')
            {
                var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? source.Length : end + 2;
            }
            else if (current is '"' or '\'')
            {
                // Verbatim strings (@"...") escape a quote by doubling it; the others with a backslash.
                var verbatim = current == '"' && index > 0 && source[index - 1] == '@';
                code.Append(current);
                index++;
                while (index < source.Length)
                {
                    var character = source[index];
                    code.Append(character);
                    index++;
                    if (!verbatim && character == '\\' && index < source.Length)
                    {
                        code.Append(source[index]);
                        index++;
                    }
                    else if (character == current)
                    {
                        if (verbatim && index < source.Length && source[index] == '"')
                        {
                            code.Append('"');
                            index++;
                            continue;
                        }

                        break;
                    }
                    else if (character == '\n' && !verbatim)
                    {
                        break;
                    }
                }
            }
            else
            {
                code.Append(current);
                index++;
            }
        }

        return code.ToString();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FoodParkDigital.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException($"FoodParkDigital.sln was not found above {AppContext.BaseDirectory}.");
    }
}
