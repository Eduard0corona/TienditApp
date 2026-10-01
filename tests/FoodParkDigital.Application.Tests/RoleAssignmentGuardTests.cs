using System.Reflection;
using System.Runtime.CompilerServices;
using FluentValidation;
using FoodParkDigital.Application.Employees.Commands;
using FoodParkDigital.Domain.Identity.Enums;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

public class RoleAssignmentGuardTests
{
    [Fact]
    public async Task Requests_ShouldRejectSuperAdmin_WhenTheyReceiveUserRole()
    {
        // UserRole.SuperAdmin = 0, so an omitted role field also arrives as SuperAdmin.
        var applicationAssembly = typeof(CreateEmployeeCommand).Assembly;
        var requestsWithRole = applicationAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
                && typeof(IBaseRequest).IsAssignableFrom(type))
            .Select(type => (Type: type, RoleProperties: GetWritableUserRoleProperties(type)))
            .Where(x => x.RoleProperties.Count > 0)
            .ToList();

        Assert.NotEmpty(requestsWithRole);

        using var provider = new ServiceCollection()
            .AddValidatorsFromAssembly(applicationAssembly)
            .BuildServiceProvider();

        var acceptingSuperAdmin = new List<string>();
        foreach (var (requestType, roleProperties) in requestsWithRole)
        {
            var validators = provider
                .GetServices(typeof(IValidator<>).MakeGenericType(requestType))
                .Cast<IValidator>()
                .ToList();

            foreach (var roleProperty in roleProperties)
            {
                var request = requestType.GetConstructor(Type.EmptyTypes) is not null
                    ? Activator.CreateInstance(requestType)!
                    : RuntimeHelpers.GetUninitializedObject(requestType);
                roleProperty.SetValue(request, UserRole.SuperAdmin);

                var rejected = false;
                foreach (var validator in validators)
                {
                    var context = (IValidationContext)Activator.CreateInstance(
                        typeof(ValidationContext<>).MakeGenericType(requestType), request)!;
                    var result = await validator.ValidateAsync(context);
                    rejected |= result.Errors.Any(e => e.PropertyName == roleProperty.Name);
                }

                if (!rejected)
                    acceptingSuperAdmin.Add($"{requestType.FullName}.{roleProperty.Name}");
            }
        }

        Assert.Empty(acceptingSuperAdmin);
    }

    [Theory]
    [InlineData(UserRole.TenantAdmin)]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.Kitchen)]
    public void EmployeeValidators_ShouldAcceptEmployeeRoles(UserRole appRole)
    {
        Assert.True(new CreateEmployeeCommandValidator().Validate(CreateEmployee(appRole)).IsValid);
        Assert.True(new UpdateEmployeeCommandValidator().Validate(UpdateEmployee(appRole)).IsValid);
    }

    [Theory]
    [InlineData(UserRole.SuperAdmin)]
    [InlineData(UserRole.Customer)]
    public void EmployeeValidators_ShouldRejectNonEmployeeRoles(UserRole appRole)
    {
        var createResult = new CreateEmployeeCommandValidator().Validate(CreateEmployee(appRole));
        var updateResult = new UpdateEmployeeCommandValidator().Validate(UpdateEmployee(appRole));

        Assert.Contains(createResult.Errors, e => e.PropertyName == nameof(CreateEmployeeCommand.AppRole));
        Assert.Contains(updateResult.Errors, e => e.PropertyName == nameof(UpdateEmployeeCommand.AppRole));
    }

    private static List<PropertyInfo> GetWritableUserRoleProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true }
                && (p.PropertyType == typeof(UserRole) || p.PropertyType == typeof(UserRole?)))
            .ToList();

    private static CreateEmployeeCommand CreateEmployee(UserRole appRole) => new()
    {
        Email = "empleado@demo.test",
        Password = "Prueba123",
        FirstName = "Empleado",
        LastName = "Demo",
        AppRole = appRole
    };

    private static UpdateEmployeeCommand UpdateEmployee(UserRole appRole) => new()
    {
        TenantUserId = Guid.NewGuid(),
        FirstName = "Empleado",
        LastName = "Demo",
        AppRole = appRole
    };
}
