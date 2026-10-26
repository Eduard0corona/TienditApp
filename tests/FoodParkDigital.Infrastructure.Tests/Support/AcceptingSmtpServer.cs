using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// Loopback SMTP endpoint that takes one email and keeps it as it arrived (headers and MIME parts), to check what a
/// sent email carries.
/// </summary>
public sealed class AcceptingSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly StringBuilder _message = new();
    private readonly Task _session;

    public AcceptingSmtpServer()
    {
        _listener.Start();
        _session = ServeSingleClientAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>The email as it was sent, after DATA.</summary>
    public string Message => _message.ToString();

    private async Task ServeSingleClientAsync()
    {
        using var client = await _listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

        await writer.WriteLineAsync("220 localhost ESMTP test");
        var inData = false;
        while (await reader.ReadLineAsync() is { } line)
        {
            if (inData)
            {
                if (line == ".")
                {
                    inData = false;
                    await writer.WriteLineAsync("250 OK");
                }
                else
                {
                    _message.Append(line.StartsWith("..", StringComparison.Ordinal) ? line[1..] : line).Append("\r\n");
                }
            }
            else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
            {
                inData = true;
                await writer.WriteLineAsync("354 Start mail input");
            }
            else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("221 Bye");
                return;
            }
            else
            {
                await writer.WriteLineAsync("250 OK");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        try
        {
            await _session;
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
        {
            // The client never connected or already dropped the connection.
        }
    }
}
