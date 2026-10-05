using System.Text.RegularExpressions;
using FoodParkDigital.WebApi.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Every error of the API is ProblemDetails with a stable <c>code</c> (the client decides by it, never by the
/// wording), a trace id for support and, while clients migrate, <c>error</c> with the text of <c>detail</c>.
/// </summary>
public class ApiProblemContractTests
{
    [Theory]
    [InlineData(400, "business_rule")]
    [InlineData(401, "unauthorized")]
    [InlineData(403, "forbidden")]
    [InlineData(404, "not_found")]
    [InlineData(409, "conflict")]
    [InlineData(422, "domain_rule")]
    [InlineData(429, "too_many_requests")]
    [InlineData(500, "server_error")]
    [InlineData(503, "server_error")]
    [InlineData(405, "bad_request")]
    public void Status_ShouldHaveItsCode(int status, string code)
    {
        var problem = ApiProblem.Create(new DefaultHttpContext(), status, "Mensaje");

        Assert.Equal(code, problem.Extensions["code"]);
        Assert.Equal(status, problem.Status);
    }

    [Fact]
    public void Problem_ShouldCarryTheTraceId_AndTheTextAsError()
    {
        var http = new DefaultHttpContext { TraceIdentifier = "trace-1" };
        http.Request.Path = "/api/v1/orders";

        var problem = ApiProblem.Create(http, 400, "El carrito está vacío.", ApiErrorCodes.BusinessRule);

        Assert.Equal("trace-1", problem.Extensions["traceId"]);
        Assert.Equal("El carrito está vacío.", problem.Extensions["error"]);
        Assert.Equal("/api/v1/orders", problem.Instance);
    }

    [Fact]
    public void FrameworkProblems_ShouldBeCompleted_WithoutLosingWhatTheyHave()
    {
        var http = new DefaultHttpContext();
        var validation = new ValidationProblemDetails(new Dictionary<string, string[]> { ["Quantity"] = ["Mayor a cero."] }) { Status = 400 };
        var withCode = new ProblemDetails { Status = 403, Detail = "Módulo apagado." };
        withCode.Extensions["code"] = ApiErrorCodes.ModuleDisabled;
        var empty = new ProblemDetails { Status = 404 };

        ApiProblem.Complete(validation, http);
        ApiProblem.Complete(withCode, http);
        ApiProblem.Complete(empty, http);

        Assert.Equal("validation_failed", validation.Extensions["code"]);
        Assert.Equal("module_disabled", withCode.Extensions["code"]);
        Assert.Equal("not_found", empty.Extensions["code"]);
        // Without a text there is nothing to repeat as "error": the client reads the field errors or its own message.
        Assert.False(empty.Extensions.ContainsKey("error"));
        Assert.False(validation.Extensions.ContainsKey("error"));
    }

    [Fact]
    public void Controllers_ShouldAnswerErrors_OnlyAsProblemDetails()
    {
        var controllers = Path.Combine(RepositoryRoot(), "src", "FoodParkDigital.WebApi", "Controllers");
        var looseErrors = new Regex(@"new\s*\{\s*error\s*=|\b(BadRequest|NotFound|Conflict|Unauthorized|UnprocessableEntity)\(\s*(""|new\b|[a-z_]\w*\s*\))", RegexOptions.CultureInvariant);

        var offenders = Directory.EnumerateFiles(controllers, "*.cs")
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (file, line, number: index + 1))
                .Where(x => looseErrors.IsMatch(x.line)))
            .Select(x => $"{Path.GetFileName(x.file)}:{x.number}: {x.line.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "Use this.ApiError(status, message[, code]):\n" + string.Join('\n', offenders));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FoodParkDigital.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException($"FoodParkDigital.sln was not found above {AppContext.BaseDirectory}.");
    }
}
