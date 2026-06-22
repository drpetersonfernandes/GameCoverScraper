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

    private static volatile bool _disposed;

    public static HttpClient Client
    {
        get
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HttpClientHelper), "HttpClientHelper has been disposed.");

            return HttpClient.Value;
        }
    }

    public static void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        if (HttpClient.IsValueCreated)
        {
            try
            {
                HttpClient.Value.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed, ignore
            }
        }
    }
}