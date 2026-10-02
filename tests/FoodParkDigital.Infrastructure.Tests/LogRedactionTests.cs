using System.Data.Common;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

public class LogRedactionTests
{
    // What SQL Server answers when a value does not fit its column (captured from LocalDB).
    private const string TruncatedPhoneMessage =
        "String or binary data would be truncated in table 'TienditApp.dbo.WhatsAppConversations', "
        + "column 'CustomerPhone'. Truncated value: '+5255123456789999999'.";

    [Theory]
    [InlineData("+525512345678", "***5678")]
    [InlineData("55 1234 5678", "***5678")]
    [InlineData("+1 (555) 010-9999", "***9999")]
    [InlineData("1234567", "***")]
    [InlineData("", "***")]
    [InlineData(null, "***")]
    public void MaskPhoneNumber_KeepsOnlyTheLastFourDigitsOfFullNumbers(string? phoneNumber, string expected)
    {
        Assert.Equal(expected, LogRedaction.MaskPhoneNumber(phoneNumber));
    }

    [Theory]
    [InlineData("ana.cliente@example.com", "a***@e***.com")]
    [InlineData("owner@negocio.com.mx", "o***@n***.mx")]
    [InlineData("ana@localhost", "a***@l***")]
    [InlineData("sin-arroba", "***")]
    [InlineData("@example.com", "***")]
    [InlineData("ana@", "***")]
    [InlineData(null, "***")]
    public void MaskEmail_HidesMailboxAndDomainNames(string? email, string expected)
    {
        Assert.Equal(expected, LogRedaction.MaskEmail(email));
    }

    [Theory]
    [InlineData("""{"code": 21211, "message": "The 'To' number +525512345678 is not a valid phone number."}""", 21211)]
    [InlineData("""{"error": {"message": "Recipient phone number not in allowed list", "code": 131030}}""", 131030)]
    [InlineData("""{"code": "21211"}""", null)]
    [InlineData("""["unexpected"]""", null)]
    [InlineData("<html>502 Bad Gateway</html>", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void GetProviderErrorCode_ReturnsOnlyNumericProviderCodes(string? responseBody, int? expected)
    {
        Assert.Equal(expected, LogRedaction.GetProviderErrorCode(responseBody));
    }

    [Fact]
    public void RedactDatabaseMessages_KeepsTypesSqlErrorNumberAndStackTracesButNoMessage()
    {
        var exception = Thrown(() => new InvalidOperationException(
            "Failed to create or retrieve temporary customer.",
            SqlExceptionFactory.SaveFailure(2628, TruncatedPhoneMessage)));

        var redacted = LogRedaction.RedactDatabaseMessages(exception);

        var details = redacted.ToString();
        Assert.DoesNotContain("5512345678", details);
        Assert.DoesNotContain("temporary customer", details);
        Assert.StartsWith($"{typeof(InvalidOperationException).FullName}: [message omitted outside Development]", details);
        Assert.Contains($" ---> {typeof(DbUpdateException).FullName}: [message omitted outside Development]", details);
        Assert.Contains($" ---> {typeof(SqlException).FullName} (SQL error 2628): [message omitted outside Development]", details);
        Assert.Contains("--- End of inner exception stack trace ---", details);
        Assert.Contains(nameof(SqlExceptionFactory.SaveFailure), details);
        Assert.Contains(nameof(Thrown), details);
        Assert.Equal("Database error 2628; exception messages are omitted outside Development.", redacted.Message);
        Assert.Null(redacted.InnerException);
    }

    [Fact]
    public void RedactDatabaseMessages_FindsDatabaseExceptionsInsideAggregateExceptions()
    {
        var exception = new AggregateException(
            new TimeoutException("Timed out."),
            SqlExceptionFactory.SaveFailure(2628, TruncatedPhoneMessage));

        var details = LogRedaction.RedactDatabaseMessages(exception).ToString();

        Assert.DoesNotContain("5512345678", details);
        Assert.DoesNotContain("Timed out", details);
        Assert.Contains(typeof(TimeoutException).FullName!, details);
        Assert.Contains($"{typeof(SqlException).FullName} (SQL error 2628)", details);
    }

    [Fact]
    public void RedactDatabaseMessages_RedactsDatabaseExceptionsOfAnyProvider()
    {
        var redacted = LogRedaction.RedactDatabaseMessages(new TestDbException("UNIQUE constraint failed: +525512345678"));

        Assert.DoesNotContain("5512345678", redacted.ToString());
        Assert.Contains(typeof(TestDbException).FullName!, redacted.ToString());
        Assert.Equal("Database error; exception messages are omitted outside Development.", redacted.Message);
    }

    [Fact]
    public void RedactDatabaseMessages_ReturnsOtherExceptionsUnchanged()
    {
        var exception = new InvalidOperationException("Sequence contains no elements", new FormatException());

        Assert.Same(exception, LogRedaction.RedactDatabaseMessages(exception));
    }

    [Fact]
    public void GetSqlErrorNumber_ReturnsTheFirstSqlServerErrorInTheChain()
    {
        var exception = new InvalidOperationException("Outer", SqlExceptionFactory.SaveFailure(2601, "Duplicate key."));

        Assert.Equal(2601, LogRedaction.GetSqlErrorNumber(exception));
        Assert.Null(LogRedaction.GetSqlErrorNumber(new InvalidOperationException("Outer", new TestDbException("SQLite"))));
    }

    private static Exception Thrown(Func<Exception> create)
    {
        try
        {
            throw create();
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class TestDbException(string message) : DbException(message);
}
