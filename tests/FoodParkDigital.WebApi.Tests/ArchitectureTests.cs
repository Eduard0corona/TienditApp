using System.Reflection;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.WebApi.Controllers;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The architecture rules of AGENTS.md, checked on the compiled assemblies: dependencies point inwards
/// (Domain ← Application ← Infrastructure ← WebApi), use cases live in Application, controllers stay thin and every
/// entity of a business is filtered by it. A change that breaks a rule fails here; an exception is added to its list
/// with the reason.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(BaseEntity).Assembly;
    private static readonly Assembly Application = typeof(Result).Assembly;
    private static readonly Assembly Infrastructure = typeof(ApplicationDbContext).Assembly;
    private static readonly Assembly WebApi = typeof(OrdersController).Assembly;

    [Fact]
    public void Layers_ShouldOnlyDependInwards()
    {
        Assert.Empty(ProjectReferences(Domain));
        Assert.Equal(["FoodParkDigital.Domain"], ProjectReferences(Application));
        Assert.DoesNotContain("FoodParkDigital.WebApi", ProjectReferences(Infrastructure));
    }

    [Fact]
    public void Domain_ShouldNotKnowPersistenceWebOrProviders()
    {
        // MediatR.Contracts (domain events) and Identity stores (ApplicationUser) are the known technical
        // dependencies of the domain (layers-and-dependencies.md); nothing else may join them.
        var allowed = new[] { "MediatR.Contracts", "Microsoft.Extensions.Identity.Stores", "Microsoft.Extensions.Identity.Core" };

        var offending = ExternalReferences(Domain).Where(name => !allowed.Contains(name)).ToList();

        Assert.True(offending.Count == 0, "Domain references: " + string.Join(", ", offending));
    }

    [Fact]
    public void Application_ShouldNotKnowHttpOrDatabaseProviders()
    {
        string[] forbiddenPrefixes =
        [
            "Microsoft.AspNetCore.Mvc", "Microsoft.AspNetCore.Http", "Microsoft.AspNetCore.Hosting",
            "Microsoft.EntityFrameworkCore.SqlServer", "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.Data.SqlClient", "Azure.", "Swashbuckle", "Serilog", "SixLabors"
        ];

        var offending = ExternalReferences(Application)
            .Where(name => forbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.True(offending.Count == 0, "Application references: " + string.Join(", ", offending));
    }

    [Fact]
    public void UseCases_ShouldLiveInApplication()
    {
        var misplaced = new[] { Infrastructure, WebApi }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsAbstract && type.GetInterfaces().Any(i =>
                i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
                                    || i.GetGenericTypeDefinition() == typeof(IRequestHandler<>))))
            .Select(type => type.FullName)
            .ToList();

        Assert.True(misplaced.Count == 0, "Request handlers outside Application: " + string.Join(", ", misplaced));
    }

    [Fact]
    public void TransactionalCommands_ShouldAnswerWithAResult()
    {
        // TransactionBehavior commits by the result; its constraints leave any other answer out, without a transaction.
        var offending = Application.GetTypes()
            .Where(type => typeof(ITransactionalRequest).IsAssignableFrom(type) && !type.IsInterface)
            .Where(type => !type.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)
                && typeof(Result).IsAssignableFrom(i.GetGenericArguments()[0])))
            .Select(type => type.Name)
            .ToList();

        Assert.True(offending.Count == 0, "Transactional commands without a Result: " + string.Join(", ", offending));
    }

    [Fact]
    public void CommandsThatTakeAFolio_ShouldRunInATransaction()
    {
        // Outside one, a folio is committed at once and a refused request leaves a gap in the business's numbering.
        // The inventory ledger takes the folio of every movement it posts.
        Type[] takeFolios = [typeof(IFolioGenerator), typeof(InventoryLedger)];
        var offending = Application.GetTypes()
            .Where(type => !type.IsAbstract && type.GetConstructors()
                .Any(c => c.GetParameters().Any(p => takeFolios.Contains(p.ParameterType))))
            .SelectMany(type => type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                .Select(i => i.GetGenericArguments()[0]))
            .Where(request => !typeof(ITransactionalRequest).IsAssignableFrom(request))
            .Select(request => request.Name)
            .ToList();

        Assert.True(offending.Count == 0, "Commands that take a folio outside a transaction: " + string.Join(", ", offending));
    }

    [Fact]
    public void Controllers_ShouldNotUseTheDatabase()
    {
        // The webhook still resolves the business and the statuses itself (technical-debt.md, "Media").
        var knownExceptions = new HashSet<string> { "WhatsAppWebhookController" };

        var offending = WebApi.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .Where(type => !knownExceptions.Contains(type.Name))
            .Where(type => type.GetConstructors().SelectMany(c => c.GetParameters()).Any(parameter =>
                typeof(DbContext).IsAssignableFrom(parameter.ParameterType)
                || parameter.ParameterType == typeof(IApplicationDbContext)))
            .Select(type => type.Name)
            .ToList();

        Assert.True(offending.Count == 0, "Controllers with database access: " + string.Join(", ", offending));
    }

    [Fact]
    public void Controllers_ShouldLeaveWhoMayDoWhatToTheUseCases()
    {
        // A controller asks for a signed-in user or is explicitly public; roles and policies here duplicated the rule
        // each request declares (and contradicted it). Every action sends a request the authorization behavior checks.
        var controllers = WebApi.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .ToList();

        var withoutDecision = controllers
            .Where(type => type.GetCustomAttribute<AuthorizeAttribute>() is null && type.GetCustomAttribute<AllowAnonymousAttribute>() is null)
            .Select(type => type.Name)
            .ToList();
        var byRoleOrPolicy = controllers
            .SelectMany(type => type.GetCustomAttributes<AuthorizeAttribute>()
                .Concat(type.GetMethods().SelectMany(method => method.GetCustomAttributes<AuthorizeAttribute>()))
                .Where(attribute => !string.IsNullOrEmpty(attribute.Roles) || !string.IsNullOrEmpty(attribute.Policy))
                .Select(_ => type.Name))
            .Distinct()
            .ToList();

        Assert.True(withoutDecision.Count == 0, "Controllers without [Authorize] or [AllowAnonymous]: " + string.Join(", ", withoutDecision));
        Assert.True(byRoleOrPolicy.Count == 0, "Controllers that authorize by role or policy: " + string.Join(", ", byRoleOrPolicy));
    }

    [Fact]
    public void ControllerActions_ShouldReceiveTheRequestCancellation()
    {
        // Each action passes it to its use case (below); RequestCancellation decides what it is: the request's own for
        // a read, none for a change.
        var offending = WebApi.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any())
            .Where(method => method.GetParameters().All(parameter => parameter.ParameterType != typeof(CancellationToken)))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .ToList();

        Assert.True(offending.Count == 0, "Actions without a CancellationToken: " + string.Join(", ", offending));
    }

    [Fact]
    public void Controllers_ShouldPassTheCancellationToTheUseCase()
    {
        var controllers = Path.Combine(TenantFilterBypassAllowlistTests.FindRepositoryRoot(), "src", "FoodParkDigital.WebApi", "Controllers");
        var calls = Directory.EnumerateFiles(controllers, "*.cs")
            .SelectMany(path => MediatorCalls(File.ReadAllText(path)).Select(arguments => (File: Path.GetFileName(path), Arguments: arguments)))
            .ToList();

        var offending = calls
            .Where(call => call.Arguments.Count != 2 || call.Arguments[1] != "cancellationToken")
            .Select(call => $"{call.File}: Send({string.Join(", ", call.Arguments)})")
            .ToList();

        Assert.NotEmpty(calls);
        Assert.True(offending.Count == 0, "Calls to the mediator without the action's cancellationToken: " + string.Join("; ", offending));
    }

    [Theory]
    [InlineData("await _mediator.Send(command, cancellationToken);", "command|cancellationToken")]
    [InlineData("await _mediator.Send(command);", "command")]
    [InlineData("_mediator.Send(new Query { Search = search, Page = Math.Max(1, page) }, cancellationToken)", "new Query { Search = search, Page = Math.Max(1, page) }|cancellationToken")]
    [InlineData("_mediator.Send(new X { Lines = l.Select(x => new Y { A = x.A, B = x.B }).ToList() }, ct)", "new X { Lines = l.Select(x => new Y { A = x.A, B = x.B }).ToList() }|ct")]
    [InlineData("_mediator.Send(new Note(\"a, b) c\", ','), CancellationToken.None)", "new Note(\"a, b) c\", ',')|CancellationToken.None")]
    [InlineData("_mediator.Send(new Note(@\"a \"\", b\"), cancellationToken)", "new Note(@\"a \"\", b\")|cancellationToken")]
    [InlineData("// _mediator.Send(command)\n_mediator.Send(other, cancellationToken)", "other|cancellationToken")]
    public void MediatorCalls_ShouldSplitTheArgumentsAtTheTopLevel(string source, string expected)
    {
        Assert.Equal(expected, string.Join("|", Assert.Single(MediatorCalls(source))));
    }

    [Fact]
    public void EntitiesWithATenant_ShouldBeFilteredByIt()
    {
        using var context = NewContext();
        var entityTypes = context.Model.GetEntityTypes().Where(e => !e.IsOwned()).ToList();

        var notScoped = entityTypes
            .Where(e => e.ClrType.GetProperty("TenantId")?.PropertyType == typeof(Guid))
            .Where(e => !typeof(ITenantScoped).IsAssignableFrom(e.ClrType))
            .Select(e => e.ClrType.Name)
            .ToList();
        var unfiltered = entityTypes
            .Where(e => typeof(ITenantScoped).IsAssignableFrom(e.ClrType) && e.GetDeclaredQueryFilters().Count == 0)
            .Select(e => e.ClrType.Name)
            .ToList();

        Assert.True(notScoped.Count == 0, "Entities with TenantId that are not ITenantScoped: " + string.Join(", ", notScoped));
        Assert.True(unfiltered.Count == 0, "ITenantScoped entities without the tenant filter: " + string.Join(", ", unfiltered));
    }

    [Fact]
    public void EntitiesWithoutATenant_ShouldBeAKnownCase()
    {
        // Platform-wide data, accounts, or children reached only through a tenant-scoped parent. A new entity without
        // TenantId must be added here on purpose (and its parent must be filtered).
        var known = new HashSet<string>
        {
            // The platform: businesses, plans, its own WhatsApp number and the delivery records of the outbox and inbox.
            "Tenant", "Plan", "OwnerWhatsAppConfig", "OutboxMessageConsumer", "InboxMessage",
            // Accounts (Identity), shared by the businesses of a person, and their sessions and customer profile.
            "ApplicationUser", "ApplicationRole", "IdentityUserRole`1", "IdentityUserClaim`1", "IdentityUserLogin`1",
            "IdentityRoleClaim`1", "IdentityUserToken`1", "RefreshToken", "CustomerProfile",
            // The addresses a customer account keeps for its orders (Fase 24B): read only by the account's own id.
            "AccountAddress",
            // A child of a tenant-scoped aggregate, filtered through it (its conversation's business).
            "WhatsAppMessage"
        };

        using var context = NewContext();
        var unknown = context.Model.GetEntityTypes()
            .Where(e => !e.IsOwned() && !e.HasSharedClrType)
            .Where(e => e.ClrType.GetProperty("TenantId") is null)
            .Select(e => e.ClrType.Name)
            .Where(name => !known.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(unknown.Count == 0, "Entities without TenantId to review: " + string.Join(", ", unknown));
    }

    private static List<string> ProjectReferences(Assembly assembly)
        => assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("FoodParkDigital.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    private static List<string> ExternalReferences(Assembly assembly)
        => assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !name.StartsWith("FoodParkDigital.", StringComparison.Ordinal)
                           && !name.StartsWith("System", StringComparison.Ordinal)
                           && name is not "netstandard" and not "mscorlib")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    private static ApplicationDbContext NewContext()
        => new(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant(),
            new NoMediator());

    /// <summary>The arguments, split at the top level, of every call to the mediator in the code (comments left out).</summary>
    private static IEnumerable<IReadOnlyList<string>> MediatorCalls(string source)
    {
        const string call = "_mediator.Send(";
        var code = TenantFilterBypassAllowlistTests.WithoutComments(source);
        for (var index = code.IndexOf(call, StringComparison.Ordinal);
             index >= 0;
             index = code.IndexOf(call, index + call.Length, StringComparison.Ordinal))
        {
            yield return Arguments(code, index + call.Length);
        }
    }

    /// <summary>Splits the arguments of the call whose list starts at <paramref name="start"/>, right after its parenthesis.</summary>
    private static List<string> Arguments(string code, int start)
    {
        var arguments = new List<string>();
        var depth = 0;
        var from = start;
        for (var index = start; index < code.Length; index++)
        {
            switch (code[index])
            {
                case '"' or '\'':
                    index = EndOfLiteral(code, index);
                    break;
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' when depth == 0:
                    arguments.Add(code[from..index].Trim());
                    return arguments;
                case ')' or ']' or '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    arguments.Add(code[from..index].Trim());
                    from = index + 1;
                    break;
            }
        }

        throw new InvalidOperationException("A call to the mediator is not closed.");
    }

    /// <returns>Where the string or character literal that opens at <paramref name="start"/> closes.</returns>
    private static int EndOfLiteral(string code, int start)
    {
        // Verbatim strings (@"...", $@"...") escape a quote by doubling it; the others with a backslash.
        var quote = code[start];
        var verbatim = quote == '"'
            && ((start > 0 && code[start - 1] == '@') || (start > 1 && code[start - 2] == '@' && code[start - 1] == '$'));
        for (var index = start + 1; index < code.Length; index++)
        {
            if (!verbatim && code[index] == '\\')
            {
                index++;
            }
            else if (code[index] == quote)
            {
                if (!verbatim || index + 1 >= code.Length || code[index + 1] != '"')
                    return index;
                index++;
            }
        }

        throw new InvalidOperationException("A literal is not closed.");
    }

    private sealed class NoTenant : ICurrentTenantService
    {
        public Guid TenantId => Guid.Empty;
        public void SetTenant(Guid tenantId) { }
    }

    private sealed class NoMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Publish(object notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification => Task.CompletedTask;
    }
}
