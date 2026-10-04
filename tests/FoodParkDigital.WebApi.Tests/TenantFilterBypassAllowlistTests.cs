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
        ["src/FoodParkDigital.Application/Identity/Commands/ConfirmEmailCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Commands/ConfirmPhoneCommand.cs"] = 2,
        ["src/FoodParkDigital.Application/Identity/Commands/ForgotPasswordCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Commands/LoginCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Commands/RefreshTokenCommand.cs"] = 3,
        ["src/FoodParkDigital.Application/Identity/Commands/RegisterBusinessCommand.cs"] = 2,
        ["src/FoodParkDigital.Application/Identity/Commands/RegisterCustomerCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Commands/ResendConfirmationEmailCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Commands/ResendPhoneConfirmationCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Commands/ResetPasswordCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Commands/SwitchTenantCommand.cs"] = 2,
        ["src/FoodParkDigital.Application/Identity/Commands/UpdateOwnerWhatsAppConfigCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Queries/GetMyTenantsQuery.cs"] = 2,
        ["src/FoodParkDigital.Application/Identity/Queries/GetOwnerWhatsAppConfigQuery.cs"] = 1,
        ["src/FoodParkDigital.Application/Identity/Services/AuthSessionIssuer.cs"] = 2,
        // Employees: the account (ApplicationUser) is shared by the businesses of a person.
        ["src/FoodParkDigital.Application/Employees/Commands/CreateEmployeeCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Employees/Commands/SetEmployeeActiveCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Employees/Commands/UpdateEmployeeCommand.cs"] = 2,
        ["src/FoodParkDigital.Application/Employees/Queries/GetEmployeeByIdQuery.cs"] = 1,
        ["src/FoodParkDigital.Application/Employees/Queries/GetEmployeesQuery.cs"] = 1,
        // Platform console and plans: platform-wide reads (the platform admin), and actions that set the target business first.
        ["src/FoodParkDigital.Application/Billing/PlanFeature.cs"] = 5,
        ["src/FoodParkDigital.Application/Billing/PlatformQueries.cs"] = 6,
        ["src/FoodParkDigital.Application/Billing/SubscriptionStatusRefresher.cs"] = 1,
        ["src/FoodParkDigital.Application/Catalog/Queries/GetCategoryTemplatesQuery.cs"] = 1,
        ["src/FoodParkDigital.Application/Tenants/Queries/GetAllTenantsQuery.cs"] = 1,
        // Access, modules and the business's own settings, read before the business is set for the request.
        ["src/FoodParkDigital.Application/Common/Rules/TenantCapabilityRules.cs"] = 2,
        ["src/FoodParkDigital.Application/Orders/Queries/GetDashboardMetricsQuery.cs"] = 1,
        ["src/FoodParkDigital.Infrastructure/Services/TenantAccessService.cs"] = 7,
        // Webhooks, background jobs and messaging providers: they resolve the business from the provider data before setting it.
        ["src/FoodParkDigital.Application/AiAgents/Commands/CreateAiAgentCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Messaging/MessagingConsentService.cs"] = 2,
        ["src/FoodParkDigital.Application/Tenants/Commands/UpdateWhatsAppConfigCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/WhatsApp/Commands/ProcessInboundMessageCommand.cs"] = 2,
        ["src/FoodParkDigital.Application/WhatsApp/Queries/VerifyWhatsAppWebhookTokenQuery.cs"] = 1,
        ["src/FoodParkDigital.Infrastructure/BackgroundJobs/BackgroundQueuesHealthCheck.cs"] = 1,
        ["src/FoodParkDigital.Infrastructure/BackgroundJobs/Messaging/MessagingWorker.cs"] = 4,
        ["src/FoodParkDigital.Infrastructure/Services/AiAgentAutoReplyService.cs"] = 1,
        ["src/FoodParkDigital.Infrastructure/Services/SmsService.cs"] = 2,
        ["src/FoodParkDigital.Infrastructure/Services/WhatsAppService.cs"] = 1,
        ["src/FoodParkDigital.Infrastructure/Services/WhatsAppWebhookProcessor.cs"] = 3,
        // Customers shared by businesses (registered accounts and temporary customers looked up by phone).
        ["src/FoodParkDigital.Application/Orders/Commands/CreateOrderCommand.cs"] = 1,
        ["src/FoodParkDigital.Application/Orders/Commands/CreateTenantOrderCommand.cs"] = 4,
        ["src/FoodParkDigital.Infrastructure/Services/CustomerLookupService.cs"] = 2,
        ["src/FoodParkDigital.Infrastructure/Services/TemporaryCustomerClaimService.cs"] = 2,
        // Inventory consumption fallback (the default location of the business of the order).
        ["src/FoodParkDigital.Infrastructure/Services/OrderInventoryService.cs"] = 1,
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

    private static int CountUses(string source)
    {
        var count = 0;
        for (var index = source.IndexOf("IgnoreQueryFilters(", StringComparison.Ordinal);
             index >= 0;
             index = source.IndexOf("IgnoreQueryFilters(", index + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
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
