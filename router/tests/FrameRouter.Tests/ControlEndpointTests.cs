using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FrameRouter.Tests;

public sealed class ControlEndpointTests : IDisposable
{
    private const string Token = "test-control-token";

    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("frame-router-tests").FullName;
    private readonly WebApplicationFactory<Program> _factory;

    public ControlEndpointTests()
    {
        _factory = CreateFactory(Token);
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_dataDirectory, recursive: true);
    }

    [Fact]
    public async Task GetFrames_NoToken_Returns401Async()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/control/frames");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SetOverride_WrongToken_Returns401AndKeepsTheScreenAsync()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");

        var response = await client.PutAsJsonAsync("/api/control/frames/dxmart/override", new { screen = "departures", minutes = 5 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(await GetOverrideUntilAsync(client));
    }

    [Fact]
    public async Task GetFrames_TokenNotConfigured_Returns403Async()
    {
        using var factory = CreateFactory(string.Empty);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        var response = await client.GetAsync("/api/control/frames");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostConfig_ControlTokenOnly_IsNotAcceptedAsync()
    {
        using var client = CreateAuthorizedClient();

        var response = await client.PostAsync("/api/config", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetOverride_ThenClear_ChangesThePublicSnapshotAsync()
    {
        using var client = CreateAuthorizedClient();

        var set = await client.PutAsJsonAsync("/api/control/frames/dxmart/override", new { screen = "ha", minutes = 30 });

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.NotNull(await GetOverrideUntilAsync(client));

        var clear = await client.DeleteAsync("/api/control/frames/dxmart/override");

        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.Null(await GetOverrideUntilAsync(client));
    }

    [Theory]
    [InlineData("dxmart", "nope", 30, HttpStatusCode.BadRequest)]
    [InlineData("dxmart", "clock", 0, HttpStatusCode.BadRequest)]
    [InlineData("dxmart", "clock", 1441, HttpStatusCode.BadRequest)]
    [InlineData("garage", "clock", 30, HttpStatusCode.NotFound)]
    public async Task SetOverride_InvalidRequest_IsRejectedAsync(string frameId, string screen, int minutes, HttpStatusCode expected)
    {
        using var client = CreateAuthorizedClient();

        var response = await client.PutAsJsonAsync($"/api/control/frames/{frameId}/override", new { screen, minutes });

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task GetFrames_ValidToken_ListsFramesAndScreensAsync()
    {
        using var client = CreateAuthorizedClient();

        var state = await client.GetFromJsonAsync<ControlStateDto>("/api/control/frames");

        Assert.NotNull(state);
        Assert.Contains("clock", state.Screens);
        Assert.Contains(state.Frames, frame => frame.Id == "dxmart");
    }

    private HttpClient CreateAuthorizedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }

    private WebApplicationFactory<Program> CreateFactory(string controlToken) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("FrameRouter:DataDirectory", _dataDirectory);
            builder.UseSetting("FrameRouter:ControlToken", controlToken);
        });

    private static async Task<DateTimeOffset?> GetOverrideUntilAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<SnapshotDto>("/api/frames/dxmart/screen"))?.OverrideUntil;

    private sealed record SnapshotDto(string Name, DateTimeOffset? OverrideUntil);

    private sealed record FrameDto(string Id, string? Label);

    private sealed record ControlStateDto(IReadOnlyList<string> Screens, IReadOnlyList<FrameDto> Frames);
}
