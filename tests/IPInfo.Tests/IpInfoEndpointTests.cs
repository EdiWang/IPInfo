using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IPInfo.Tests;

public sealed class IpInfoEndpointTests
{
    [Fact]
    public async Task GetSpecificIp_ReturnsLocation_WhenDatabaseIsAvailable()
    {
        using var file = TestQqwryDatabase.WriteTempFile(
            TestQqwryDatabase.CreateNormalDb("United States", "Google LLC", padToProviderMinimum: true));
        using var factory = CreateFactory(file.Path);
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<IpLocationResponse>(
            "/ip/1.1.1.8",
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("1.1.1.8", result.QueryIp);
        Assert.Equal(["United States"], result.Country);
        Assert.Equal([string.Empty], result.Area);
        Assert.Equal(["Google LLC"], result.Isp);
    }

    [Fact]
    public async Task GetSpecificIp_ReturnsOneLocationPerConfiguredProvider()
    {
        using var firstFile = TestQqwryDatabase.WriteTempFile(
            TestQqwryDatabase.CreateNormalDb("United States", "Google LLC", padToProviderMinimum: true));
        using var secondFile = TestQqwryDatabase.WriteTempFile(
            TestQqwryDatabase.CreateNormalDb("United States 2", "Provider 2", padToProviderMinimum: true));
        using var factory = CreateFactory([firstFile.Path, secondFile.Path]);
        using var client = factory.CreateClient();

        var result = await client.GetFromJsonAsync<IpLocationResponse>(
            "/ip/1.1.1.8",
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("1.1.1.8", result.QueryIp);
        Assert.Equal(["United States", "United States 2"], result.Country);
        Assert.Equal([string.Empty, string.Empty], result.Area);
        Assert.Equal(["Google LLC", "Provider 2"], result.Isp);
    }

    [Fact]
    public async Task GetSelfIp_UsesLeftmostXForwardedForAddress()
    {
        using var file = TestQqwryDatabase.WriteTempFile(
            TestQqwryDatabase.CreateNormalDb("United States", "Google LLC", padToProviderMinimum: true));
        using var factory = CreateFactory(file.Path);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "1.1.1.8, 10.0.0.1");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var result = await response.Content.ReadFromJsonAsync<IpLocationResponse>(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("1.1.1.8", result.QueryIp);
        Assert.Equal("1.1.1.8", result.ClientIpV4);
        Assert.Null(result.ClientIpV6);
    }

    [Fact]
    public async Task GetSelfIp_ReturnsIpv6WithoutLocation_WhenClientHasNoIpv4()
    {
        using var file = TestQqwryDatabase.WriteTempFile(
            TestQqwryDatabase.CreateNormalDb("United States", "Google LLC", padToProviderMinimum: true));
        using var factory = CreateFactory(file.Path);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "2001:db8::8");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var result = await response.Content.ReadFromJsonAsync<IpLocationResponse>(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("2001:db8::8", result.QueryIp);
        Assert.Null(result.ClientIpV4);
        Assert.Equal("2001:db8::8", result.ClientIpV6);
        Assert.Equal([string.Empty], result.Country);
        Assert.Equal([string.Empty], result.Area);
        Assert.Equal([string.Empty], result.Isp);
    }

    [Fact]
    public async Task GetSpecificIp_ReturnsBadRequest_WhenIpIsInvalid()
    {
        using var file = TestQqwryDatabase.WriteTempFile(
            TestQqwryDatabase.CreateNormalDb("United States", "Google LLC", padToProviderMinimum: true));
        using var factory = CreateFactory(file.Path);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/ip/999.999.999.999",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Request_ReturnsServiceUnavailable_WhenDatabaseIsMissing()
    {
        var missingPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.dat");
        using var factory = CreateFactory(missingPath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/ip/1.1.1.8",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DbInfo_ReturnsPublicMetadataWithoutFullPath_WhenDatabaseIsAvailable()
    {
        using var file = TestQqwryDatabase.WriteTempFile(
            TestQqwryDatabase.CreateNormalDb("United States", "Google LLC", padToProviderMinimum: true));
        using var factory = CreateFactory(file.Path);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/db-info",
            TestContext.Current.CancellationToken);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);

        var root = document.RootElement;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var database = root.GetProperty("databases").EnumerateArray().Single();
        Assert.Equal(System.IO.Path.GetFileName(file.Path), database.GetProperty("fileName").GetString());
        Assert.True(database.TryGetProperty("sizeMb", out _));
        Assert.True(database.TryGetProperty("lastUpdatedUtc", out _));
        Assert.True(database.GetProperty("available").GetBoolean());
        Assert.False(root.TryGetProperty("path", out _));
    }

    [Fact]
    public async Task DbInfo_ReturnsServiceUnavailable_WhenDatabaseIsMissing()
    {
        var missingPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.dat");
        using var factory = CreateFactory(missingPath);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/db-info",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task HealthChecks_ReturnHealthy_WhenDatabaseIsAvailable()
    {
        using var file = TestQqwryDatabase.WriteTempFile(
            TestQqwryDatabase.CreateNormalDb("United States", "Google LLC", padToProviderMinimum: true));
        using var factory = CreateFactory(file.Path);
        using var client = factory.CreateClient();

        using var live = await client.GetAsync(
            "/health/live",
            TestContext.Current.CancellationToken);
        using var ready = await client.GetAsync(
            "/health/ready",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task HealthChecks_KeepLivenessSeparateFromDatabaseReadiness()
    {
        var missingPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.dat");
        using var factory = CreateFactory(missingPath);
        using var client = factory.CreateClient();

        using var live = await client.GetAsync(
            "/health/live",
            TestContext.Current.CancellationToken);
        using var ready = await client.GetAsync(
            "/health/ready",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    [Fact]
    public async Task RateLimiting_UsesLeftmostXForwardedForAddressAsPartitionKey()
    {
        using var file = TestQqwryDatabase.WriteTempFile(
            TestQqwryDatabase.CreateNormalDb("United States", "Google LLC", padToProviderMinimum: true));
        using var factory = CreateFactory(file.Path, perIpPerSecond: 1);
        using var client = factory.CreateClient();

        var first = await SendLookupWithXForwardedFor(client, "1.1.1.8, 10.0.0.1");
        var secondSameClient = await SendLookupWithXForwardedFor(client, "1.1.1.8, 10.0.0.2");
        var thirdDifferentClient = await SendLookupWithXForwardedFor(client, "1.1.1.9, 10.0.0.1");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondSameClient.StatusCode);
        Assert.Equal("application/problem+json", secondSameClient.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, thirdDifferentClient.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(string dbPath, int perIpPerSecond = 1000)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("DBPath", dbPath);
                builder.UseSetting("RateLimiting:PerIpPerSecond", perIpPerSecond.ToString());
                builder.UseSetting("RateLimiting:GlobalPerSecond", "1000");
                builder.UseSetting("IpDb:ReloadIntervalSeconds", "3600");
            });
    }

    private static WebApplicationFactory<Program> CreateFactory(string[] qqwryPaths, int perIpPerSecond = 1000)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                for (var i = 0; i < qqwryPaths.Length; i++)
                {
                    builder.UseSetting($"IpDatabases:Providers:{i}:Type", "Qqwry");
                    builder.UseSetting($"IpDatabases:Providers:{i}:Path", qqwryPaths[i]);
                }

                builder.UseSetting("RateLimiting:PerIpPerSecond", perIpPerSecond.ToString());
                builder.UseSetting("RateLimiting:GlobalPerSecond", "1000");
                builder.UseSetting("IpDb:ReloadIntervalSeconds", "3600");
            });
    }

    private static Task<HttpResponseMessage> SendLookupWithXForwardedFor(HttpClient client, string xForwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ip/1.1.1.8");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", xForwardedFor);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed class IpLocationResponse
    {
        public string QueryIp { get; init; } = string.Empty;
        public string? ClientIpV4 { get; init; }
        public string? ClientIpV6 { get; init; }
        public string[] Country { get; init; } = [];
        public string[] Area { get; init; } = [];
        public string[] Isp { get; init; } = [];
    }
}
