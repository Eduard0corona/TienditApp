using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// Loopback SMTP endpoint that rejects every recipient echoing its address, as real servers do
/// ("550 5.1.1 &lt;ana@example.com&gt;: Recipient address rejected"), for good by default or, with a 4xx code, for now.
/// </summary>
public sealed class RejectingSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly ConcurrentQueue<string> _commands = new();
    private readonly string _rejection;
    private readonly Task _session;

    /// <param name="rejection">The start of the answer to each recipient: "550 5.1.1" (for good) or "451 4.3.0" (for now).</param>
    public RejectingSmtpServer(string rejection = "550 5.1.1")
    {
        _rejection = rejection;
        _listener.Start();
        _session = ServeSingleClientAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public IReadOnlyCollection<string> Commands => _commands.ToArray();

    private async Task ServeSingleClientAsync()
    {
        using var client = await _listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

        await writer.WriteLineAsync("220 localhost ESMTP test");
        while (await reader.ReadLineAsync() is { } command)
        {
            _commands.Enqueue(command);

            if (command.StartsWith("RCPT TO:", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync($"{_rejection} {command["RCPT TO:".Length..].Trim()}: Recipient address rejected");
            }
            else if (command.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
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
