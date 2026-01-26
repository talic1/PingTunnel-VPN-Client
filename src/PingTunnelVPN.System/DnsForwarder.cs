using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Serilog;

namespace PingTunnelVPN.Platform;

/// <summary>
/// A local DNS forwarder that proxies DNS queries through a SOCKS5 proxy.
/// This prevents DNS leaks by tunneling all DNS traffic.
/// Includes DNS response caching with TTL support for performance.
/// </summary>
public class DnsForwarder : IDisposable
{
    private readonly int _listenPort;
    private readonly string _socksHost;
    private readonly int _socksPort;
    private readonly List<string> _upstreamServers;
    private UdpClient? _udpListener;
    private TcpListener? _tcpListener;
    private CancellationTokenSource? _cts;
    private Task? _udpTask;
    private Task? _tcpTask;
    private bool _disposed;

    // Retry and timeout configuration
    private const int ConnectionTimeoutMs = 5000;
    private const int MaxRetriesPerUpstream = 2;
    private const int BaseRetryDelayMs = 100;
    private int _consecutiveFailures = 0;

    // DNS cache configuration
    private const int MaxCacheEntries = 1000;
    private const int MinTtlSeconds = 60;      // Minimum TTL to prevent too-frequent queries
    private const int MaxTtlSeconds = 3600;    // Maximum TTL cap (1 hour)
    private const int DefaultTtlSeconds = 300; // Default TTL if not parseable

    private readonly ConcurrentDictionary<string, DnsCacheEntry> _dnsCache = new();
    private long _cacheHits = 0;
    private long _cacheMisses = 0;

    /// <summary>
    /// Represents a cached DNS response.
    /// </summary>
    private class DnsCacheEntry
    {
        public byte[] Response { get; init; } = Array.Empty<byte>();
        public DateTime ExpiresAt { get; init; }
        public DateTime LastAccessedAt { get; set; }
        
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    }

    /// <summary>
    /// Creates a new DNS forwarder.
    /// </summary>
    /// <param name="listenPort">Local port to listen on (default 53)</param>
    /// <param name="socksHost">SOCKS5 proxy host</param>
    /// <param name="socksPort">SOCKS5 proxy port</param>
    /// <param name="upstreamServers">Upstream DNS servers to forward to</param>
    public DnsForwarder(
        int listenPort = 53, 
        string socksHost = "127.0.0.1", 
        int socksPort = 1080,
        IEnumerable<string>? upstreamServers = null)
    {
        _listenPort = listenPort;
        _socksHost = socksHost;
        _socksPort = socksPort;
        _upstreamServers = upstreamServers?.ToList() ?? new List<string> { "1.1.1.1", "8.8.8.8" };
    }

    /// <summary>
    /// Starts the DNS forwarder.
    /// </summary>
    public void Start()
    {
        if (_cts != null)
            throw new InvalidOperationException("DNS forwarder is already running");

        _cts = new CancellationTokenSource();

        // Start UDP listener for standard DNS queries
        try
        {
            _udpListener = new UdpClient(new IPEndPoint(IPAddress.Loopback, _listenPort));
            _udpTask = Task.Run(() => UdpListenLoopAsync(_cts.Token));
            Log.Information("DNS forwarder listening on UDP 127.0.0.1:{Port}", _listenPort);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            Log.Warning("Port {Port} already in use, trying alternative port", _listenPort);
            // Try an alternative port
            _udpListener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 5353));
            _udpTask = Task.Run(() => UdpListenLoopAsync(_cts.Token));
            Log.Information("DNS forwarder listening on UDP 127.0.0.1:5353");
        }

        // Start TCP listener for larger DNS responses
        try
        {
            _tcpListener = new TcpListener(IPAddress.Loopback, _listenPort);
            _tcpListener.Start();
            _tcpTask = Task.Run(() => TcpListenLoopAsync(_cts.Token));
            Log.Information("DNS forwarder listening on TCP 127.0.0.1:{Port}", _listenPort);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to start TCP DNS listener");
        }
    }

    /// <summary>
    /// Stops the DNS forwarder.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();

        _udpListener?.Close();
        _udpListener = null;

        _tcpListener?.Stop();
        _tcpListener = null;

        try
        {
            if (_udpTask != null)
                Task.WhenAny(_udpTask, Task.Delay(1000)).Wait();
            if (_tcpTask != null)
                Task.WhenAny(_tcpTask, Task.Delay(1000)).Wait();
        }
        catch { }

        _cts?.Dispose();
        _cts = null;

        Log.Information("DNS forwarder stopped");
    }

    private async Task UdpListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_udpListener == null) break;

                var result = await _udpListener.ReceiveAsync(ct);
                _ = HandleDnsQueryAsync(result.Buffer, result.RemoteEndPoint, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                // Error 10054 - can happen during network changes, just continue
                Log.Debug("UDP socket reset (10054), continuing...");
                await Task.Delay(100, ct);
            }
            catch (ObjectDisposedException)
            {
                // Socket was closed, exit the loop
                break;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Error in UDP DNS listen loop");
                // Brief delay before retrying to avoid tight loop on persistent errors
                await Task.Delay(100, ct);
            }
        }
    }

    private async Task TcpListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_tcpListener == null) break;

                var client = await _tcpListener.AcceptTcpClientAsync(ct);
                _ = HandleTcpDnsClientAsync(client, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Error in TCP DNS listen loop");
            }
        }
    }

    private async Task HandleDnsQueryAsync(byte[] query, IPEndPoint clientEndpoint, CancellationToken ct)
    {
        try
        {
            var response = await ForwardDnsQueryViaSocksAsync(query, ct);
            if (response != null && _udpListener != null)
            {
                await _udpListener.SendAsync(response, response.Length, clientEndpoint);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error handling DNS query");
        }
    }

    private async Task HandleTcpDnsClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var lengthBuffer = new byte[2];
                
                // Read length prefix
                if (await stream.ReadAsync(lengthBuffer, 0, 2, ct) != 2)
                    return;

                int queryLength = (lengthBuffer[0] << 8) | lengthBuffer[1];
                var query = new byte[queryLength];
                
                int totalRead = 0;
                while (totalRead < queryLength)
                {
                    int read = await stream.ReadAsync(query, totalRead, queryLength - totalRead, ct);
                    if (read == 0) return;
                    totalRead += read;
                }

                var response = await ForwardDnsQueryViaSocksAsync(query, ct);
                if (response != null)
                {
                    var responseLength = new byte[2];
                    responseLength[0] = (byte)(response.Length >> 8);
                    responseLength[1] = (byte)(response.Length & 0xFF);
                    
                    await stream.WriteAsync(responseLength, 0, 2, ct);
                    await stream.WriteAsync(response, 0, response.Length, ct);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error handling TCP DNS client");
        }
    }

    private async Task<byte[]?> ForwardDnsQueryViaSocksAsync(byte[] query, CancellationToken ct)
    {
        // Try to get from cache first
        var cacheKey = GetCacheKey(query);
        if (cacheKey != null && TryGetCachedResponse(cacheKey, query, out var cachedResponse))
        {
            Interlocked.Increment(ref _cacheHits);
            return cachedResponse;
        }

        Interlocked.Increment(ref _cacheMisses);

        foreach (var upstream in _upstreamServers)
        {
            for (int retry = 0; retry <= MaxRetriesPerUpstream; retry++)
            {
                if (ct.IsCancellationRequested) return null;

                try
                {
                    var result = await TryForwardToUpstreamAsync(query, upstream, ct);
                    if (result != null)
                    {
                        // Reset failure counter on success
                        Interlocked.Exchange(ref _consecutiveFailures, 0);
                        
                        // Cache the response
                        if (cacheKey != null)
                        {
                            CacheResponse(cacheKey, result);
                        }
                        
                        return result;
                    }
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (Exception ex)
                {
                    var failures = Interlocked.Increment(ref _consecutiveFailures);
                    
                    // Log at Warning level after several consecutive failures
                    if (failures % 10 == 0)
                    {
                        Log.Warning("DNS forwarding experiencing issues: {Failures} consecutive failures. Last error: {Error}", 
                            failures, ex.Message);
                    }
                    else
                    {
                        Log.Debug(ex, "Failed to forward DNS query to {Upstream} (attempt {Attempt}/{Max})", 
                            upstream, retry + 1, MaxRetriesPerUpstream + 1);
                    }

                    // Exponential backoff before retry
                    if (retry < MaxRetriesPerUpstream)
                    {
                        var delay = BaseRetryDelayMs * (1 << retry); // 100ms, 200ms, 400ms...
                        await Task.Delay(delay, ct);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Generates a cache key from a DNS query.
    /// Key is based on: query name, query type, and query class.
    /// </summary>
    private static string? GetCacheKey(byte[] query)
    {
        try
        {
            if (query.Length < 12) return null;

            // Skip header (12 bytes) and extract the question section
            int pos = 12;
            var nameBuilder = new System.Text.StringBuilder();
            
            // Read domain name labels
            while (pos < query.Length)
            {
                int labelLen = query[pos];
                if (labelLen == 0)
                {
                    pos++;
                    break;
                }
                
                if (labelLen > 63 || pos + labelLen >= query.Length)
                    return null; // Invalid label
                
                if (nameBuilder.Length > 0)
                    nameBuilder.Append('.');
                
                nameBuilder.Append(System.Text.Encoding.ASCII.GetString(query, pos + 1, labelLen));
                pos += labelLen + 1;
            }

            // Read QTYPE and QCLASS (2 bytes each)
            if (pos + 4 > query.Length) return null;
            
            int qtype = (query[pos] << 8) | query[pos + 1];
            int qclass = (query[pos + 2] << 8) | query[pos + 3];

            return $"{nameBuilder}|{qtype}|{qclass}".ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Tries to get a cached DNS response.
    /// Returns a response with the transaction ID updated to match the query.
    /// </summary>
    private bool TryGetCachedResponse(string cacheKey, byte[] query, out byte[] response)
    {
        response = Array.Empty<byte>();

        if (!_dnsCache.TryGetValue(cacheKey, out var entry))
            return false;

        if (entry.IsExpired)
        {
            _dnsCache.TryRemove(cacheKey, out _);
            return false;
        }

        // Update last access time for LRU
        entry.LastAccessedAt = DateTime.UtcNow;

        // Copy the cached response and update the transaction ID
        response = new byte[entry.Response.Length];
        Array.Copy(entry.Response, response, entry.Response.Length);
        
        // Copy transaction ID from query to response (first 2 bytes)
        if (query.Length >= 2 && response.Length >= 2)
        {
            response[0] = query[0];
            response[1] = query[1];
        }

        Log.Debug("DNS cache hit for {Key}", cacheKey);
        return true;
    }

    /// <summary>
    /// Caches a DNS response, extracting TTL from the response.
    /// </summary>
    private void CacheResponse(string cacheKey, byte[] response)
    {
        try
        {
            // Evict expired entries and LRU entries if cache is too large
            if (_dnsCache.Count >= MaxCacheEntries)
            {
                EvictCacheEntries();
            }

            int ttl = ExtractMinTtl(response);
            ttl = Math.Clamp(ttl, MinTtlSeconds, MaxTtlSeconds);

            var entry = new DnsCacheEntry
            {
                Response = response.ToArray(), // Make a copy
                ExpiresAt = DateTime.UtcNow.AddSeconds(ttl),
                LastAccessedAt = DateTime.UtcNow
            };

            _dnsCache[cacheKey] = entry;
            Log.Debug("Cached DNS response for {Key} with TTL {Ttl}s (cache size: {Size})", 
                cacheKey, ttl, _dnsCache.Count);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to cache DNS response for {Key}", cacheKey);
        }
    }

    /// <summary>
    /// Extracts the minimum TTL from a DNS response.
    /// </summary>
    private static int ExtractMinTtl(byte[] response)
    {
        try
        {
            if (response.Length < 12) return DefaultTtlSeconds;

            // Read header counts
            int qdcount = (response[4] << 8) | response[5];   // Questions
            int ancount = (response[6] << 8) | response[7];   // Answers
            int nscount = (response[8] << 8) | response[9];   // Authority
            int arcount = (response[10] << 8) | response[11]; // Additional

            int pos = 12;
            int minTtl = int.MaxValue;

            // Skip questions
            for (int i = 0; i < qdcount && pos < response.Length; i++)
            {
                pos = SkipDomainName(response, pos);
                if (pos < 0) return DefaultTtlSeconds;
                pos += 4; // QTYPE + QCLASS
            }

            // Process answer, authority, and additional sections
            int totalRecords = ancount + nscount + arcount;
            for (int i = 0; i < totalRecords && pos < response.Length; i++)
            {
                pos = SkipDomainName(response, pos);
                if (pos < 0 || pos + 10 > response.Length) break;

                // Skip TYPE (2) and CLASS (2), read TTL (4)
                pos += 4;
                int ttl = (response[pos] << 24) | (response[pos + 1] << 16) | 
                          (response[pos + 2] << 8) | response[pos + 3];
                pos += 4;

                if (ttl > 0 && ttl < minTtl)
                    minTtl = ttl;

                // Read RDLENGTH and skip RDATA
                if (pos + 2 > response.Length) break;
                int rdlength = (response[pos] << 8) | response[pos + 1];
                pos += 2 + rdlength;
            }

            return minTtl == int.MaxValue ? DefaultTtlSeconds : minTtl;
        }
        catch
        {
            return DefaultTtlSeconds;
        }
    }

    /// <summary>
    /// Skips a domain name in a DNS message (handles compression).
    /// </summary>
    private static int SkipDomainName(byte[] data, int pos)
    {
        if (pos < 0 || pos >= data.Length) return -1;

        while (pos < data.Length)
        {
            int len = data[pos];
            
            if (len == 0)
            {
                return pos + 1;
            }
            else if ((len & 0xC0) == 0xC0)
            {
                // Compression pointer
                return pos + 2;
            }
            else if (len > 63)
            {
                return -1; // Invalid
            }
            else
            {
                pos += len + 1;
            }
        }

        return -1;
    }

    /// <summary>
    /// Evicts expired entries and least recently used entries when cache is full.
    /// </summary>
    private void EvictCacheEntries()
    {
        // First, remove all expired entries
        var expiredKeys = _dnsCache
            .Where(kvp => kvp.Value.IsExpired)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _dnsCache.TryRemove(key, out _);
        }

        // If still too large, remove LRU entries
        if (_dnsCache.Count >= MaxCacheEntries)
        {
            var entriesToRemove = _dnsCache
                .OrderBy(kvp => kvp.Value.LastAccessedAt)
                .Take(_dnsCache.Count - MaxCacheEntries + 100) // Remove 100 extra for headroom
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in entriesToRemove)
            {
                _dnsCache.TryRemove(key, out _);
            }

            Log.Debug("Evicted {Count} LRU cache entries", entriesToRemove.Count);
        }
    }

    /// <summary>
    /// Gets DNS cache statistics.
    /// </summary>
    public (long hits, long misses, int size) GetCacheStats()
    {
        return (Interlocked.Read(ref _cacheHits), Interlocked.Read(ref _cacheMisses), _dnsCache.Count);
    }

    private async Task<byte[]?> TryForwardToUpstreamAsync(byte[] query, string upstream, CancellationToken ct)
    {
        // Create a timeout-linked cancellation token
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ConnectionTimeoutMs);
        var linkedCt = timeoutCts.Token;

        // Connect to upstream via SOCKS5 using TCP DNS (port 53)
        using var socksClient = new TcpClient();
        
        // Connect with timeout
        await socksClient.ConnectAsync(_socksHost, _socksPort, linkedCt);

        var stream = socksClient.GetStream();
        stream.ReadTimeout = ConnectionTimeoutMs;
        stream.WriteTimeout = ConnectionTimeoutMs;

        // SOCKS5 handshake - no auth
        var greeting = new byte[] { 0x05, 0x01, 0x00 };
        await stream.WriteAsync(greeting, 0, greeting.Length, linkedCt);

        var greetingResponse = new byte[2];
        if (await ReadExactAsync(stream, greetingResponse, 0, 2, linkedCt) != 2)
        {
            throw new IOException("SOCKS5 handshake failed: incomplete greeting response");
        }
        
        if (greetingResponse[1] != 0x00)
        {
            throw new IOException($"SOCKS5 handshake failed: server returned auth method {greetingResponse[1]}");
        }

        // Connect request to upstream DNS server on port 53
        if (!IPAddress.TryParse(upstream, out var upstreamIp))
        {
            throw new ArgumentException($"Invalid upstream DNS server address: {upstream}");
        }

        var ipBytes = upstreamIp.GetAddressBytes();
        var connectRequest = new byte[10];
        connectRequest[0] = 0x05; // Version
        connectRequest[1] = 0x01; // Connect
        connectRequest[2] = 0x00; // Reserved
        connectRequest[3] = 0x01; // IPv4
        Array.Copy(ipBytes, 0, connectRequest, 4, 4);
        connectRequest[8] = 0x00; // Port high byte (53)
        connectRequest[9] = 0x35; // Port low byte (53)

        await stream.WriteAsync(connectRequest, 0, connectRequest.Length, linkedCt);

        var connectResponse = new byte[10];
        int responseBytes = await ReadExactAsync(stream, connectResponse, 0, 10, linkedCt);
        if (responseBytes < 10)
        {
            throw new IOException($"SOCKS5 connect failed: incomplete response ({responseBytes} bytes)");
        }
        
        if (connectResponse[1] != 0x00)
        {
            throw new IOException($"SOCKS5 connect failed: server returned status {connectResponse[1]}");
        }

        // Send DNS query with TCP length prefix
        var tcpQuery = new byte[query.Length + 2];
        tcpQuery[0] = (byte)(query.Length >> 8);
        tcpQuery[1] = (byte)(query.Length & 0xFF);
        Array.Copy(query, 0, tcpQuery, 2, query.Length);

        await stream.WriteAsync(tcpQuery, 0, tcpQuery.Length, linkedCt);

        // Read response length
        var lengthBuffer = new byte[2];
        if (await ReadExactAsync(stream, lengthBuffer, 0, 2, linkedCt) != 2)
        {
            throw new IOException("Failed to read DNS response length");
        }

        int responseLength = (lengthBuffer[0] << 8) | lengthBuffer[1];
        if (responseLength > 65535 || responseLength < 12)
        {
            throw new IOException($"Invalid DNS response length: {responseLength}");
        }

        var response = new byte[responseLength];
        int totalRead = await ReadExactAsync(stream, response, 0, responseLength, linkedCt);

        if (totalRead == responseLength)
        {
            Log.Debug("DNS query forwarded via SOCKS to {Upstream}", upstream);
            return response;
        }

        throw new IOException($"Incomplete DNS response: expected {responseLength} bytes, got {totalRead}");
    }

    private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, ct);
            if (read == 0) break;
            totalRead += read;
        }
        return totalRead;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
