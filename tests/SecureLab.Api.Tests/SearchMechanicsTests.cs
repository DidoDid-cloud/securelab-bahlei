using System.Net;
using System.Net.Http.Json;

namespace SecureLab.Api.Tests;

public sealed class SearchMechanicsTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    [Fact]
    public async Task Search_ReturnsJson()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/incidents/search?q=навчальн");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Search_LegitimateApostrophe_ReturnsMatch()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/incidents/search?q=" + Uri.EscapeDataString("комп'ютерного"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await response.Content.ReadFromJsonAsync<List<Dictionary<string, object>>>();
        Assert.NotNull(rows);
        Assert.NotEmpty(rows);
    }

    [Fact]
    public async Task Search_SqlInjectionAttempt_ReturnsNoRows()
    {
        using var client = factory.CreateClient();
        var payload = Uri.EscapeDataString("zz-no-match' OR TRUE -- ");
        using var response = await client.GetAsync($"/api/incidents/search?q={payload}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await response.Content.ReadFromJsonAsync<List<Dictionary<string, object>>>();
        Assert.NotNull(rows);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task Search_UnknownSortBy_ReturnsBadRequest()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/incidents/search?q=USB&sortBy=unknown");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}