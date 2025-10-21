using System.Net.Http;

namespace GameCoverScraper.Services;

public static class HttpClientHelper
{
    private static readonly Lazy<HttpClient> HttpClient = new(static () =>
    {
        var handler = new SocketsHttpHandler
        {
            // Configure connection pooling
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            MaxConnectionsPerServer = 10
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Set default headers if needed
        client.DefaultRequestHeaders.ConnectionClose = false; // Keep connections alive

        return client;
    });

    public static HttpClient Client => HttpClient.Value;

    public static void Dispose()
    {
        if (HttpClient.IsValueCreated)
        {
            HttpClient.Value?.Dispose();
        }
    }
}