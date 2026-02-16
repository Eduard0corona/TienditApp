using AutoMapper;
using FoodParkDigital.Application.AiAgents.Commands;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Mappings;
using FoodParkDigital.Domain.AiAgents.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

public class AiAgentsCommandTests
{
    [Fact]
    public async Task CreateAiAgent_ShouldCreateAgent_ForCurrentTenant()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedTenantAsync();

        var handler = fixture.CreateCreateHandler();
        var result = await handler.Handle(
            new CreateAiAgentCommand
            {
                Name = "Agente Ventas",
                Role = "Asesor comercial",
                Personality = "Empatico",
                Language = "Espanol",
                Tone = "Consultivo",
                BusinessName = "Food Park",
                BusinessDescription = "Comida rapida",
                BusinessProductsAndServices = "Hamburguesas, bebidas"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Agente Ventas", result.Value.Name);
        Assert.Equal("Asesor comercial", result.Value.Role);
        Assert.Equal(1, await fixture.Context.AiAgents.CountAsync());
    }

    [Fact]
    public async Task UpdateAiAgent_ShouldModifyConfiguration()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedTenantAsync();

        var agent = new AiAgent(fixture.TenantId, "Agente Base", "Soporte");
        fixture.Context.AiAgents.Add(agent);
        await fixture.Context.SaveChangesAsync();

        var handler = fixture.CreateUpdateHandler();
        var result = await handler.Handle(
            new UpdateAiAgentCommand
            {
                Id = agent.Id,
                Name = "Agente Operaciones",
                Role = "Coordinador",
                Personality = "Directo",
                Language = "Espanol",
                Tone = "Formal",
                BusinessName = "Food Park",
                BusinessDescription = "Operacion de restaurantes",
                BusinessProductsAndServices = "Platillos, promociones, reservas",
                WelcomeMessage = "Hola, te apoyo con tu orden.",
                FallbackMessage = "No tengo esa respuesta.",
                ClosingMessage = "Gracias por escribir.",
                AskForName = true,
                AskForPhone = true,
                AskForEmail = true,
                AskForAddress = false,
                CustomQuestions = "Sucursal preferida",
                Model = "gpt-4.1-mini",
                Temperature = 0.3m,
                MaxTokens = 1200,
                UseConversationMemory = true,
                StrictMode = true
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Agente Operaciones", result.Value.Name);
        Assert.Equal("Coordinador", result.Value.Role);
        Assert.Equal("Hola, te apoyo con tu orden.", result.Value.WelcomeMessage);
        Assert.Equal(0.3m, result.Value.Temperature);
        Assert.Equal(1200, result.Value.MaxTokens);
        Assert.True(result.Value.StrictMode);
    }

    [Fact]
    public async Task DeleteAiAgent_ShouldRemoveEntity()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedTenantAsync();

        var agent = new AiAgent(fixture.TenantId, "Agente Temporal", "Soporte");
        fixture.Context.AiAgents.Add(agent);
        await fixture.Context.SaveChangesAsync();

        var handler = new DeleteAiAgentCommandHandler(fixture.Context);
        var result = await handler.Handle(new DeleteAiAgentCommand(agent.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, await fixture.Context.AiAgents.CountAsync());
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly TestCurrentTenantService _tenantService;

        private TestFixture(ApplicationDbContext context, IMapper mapper, TestCurrentTenantService tenantService)
        {
            Context = context;
            Mapper = mapper;
            _tenantService = tenantService;
        }

        public ApplicationDbContext Context { get; }
        public IMapper Mapper { get; }
        public Guid TenantId => _tenantService.TenantId;

        public static async Task<TestFixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            var tenantService = new TestCurrentTenantService();
            var context = new ApplicationDbContext(options, tenantService, new NullMediator());
            var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>())
                .CreateMapper();

            var fixture = new TestFixture(context, mapper, tenantService);
            await Task.CompletedTask;
            return fixture;
        }

        public async Task SeedTenantAsync()
        {
            var tenant = new Tenant("Tenant Test", "tenant-test", BusinessType.Food, "tenant@test.com");
            Context.Tenants.Add(tenant);
            await Context.SaveChangesAsync();

            _tenantService.SetTenant(tenant.Id);
        }

        public CreateAiAgentCommandHandler CreateCreateHandler()
            => new(Context, _tenantService, Mapper);

        public UpdateAiAgentCommandHandler CreateUpdateHandler()
            => new(Context, Mapper);

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class NullMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
            => Task.FromResult<TResponse>(default!);

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
            where TRequest : IRequest
            => Task.CompletedTask;

        public Task Publish(object notification, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(object request, CancellationToken ct = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default)
            => AsyncEnumerable.Empty<object?>();

        public Task<object?> Send(object request, CancellationToken ct = default)
            => Task.FromResult<object?>(null);
    }
}
