using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

/// <summary>
/// The certificate's days and the relational store's size, read once an hour
/// for the Azure card (ADR: The Admin tab, as a product, the addendum on
/// certificate and storage).
/// </summary>
public class HourlyReadingsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    // #region hourly-readings-tests
    [Theory]
    [InlineData("2026-12-01T00:00:00Z", "2026-10-15T00:00:00Z", 47)]
    [InlineData("2026-10-20T12:00:00Z", "2026-10-15T18:00:00Z", 4)]
    [InlineData("2026-10-15T00:00:00Z", "2026-10-15T06:00:00Z", -1)]
    public void Days_left_are_whole_days_rounded_down(string notAfter, string now, int days) =>
        Assert.Equal(days, HourlyReadings.DaysLeft(DateTimeOffset.Parse(notAfter), DateTimeOffset.Parse(now)));

    [Fact]
    public async Task With_no_public_address_there_is_no_certificate_and_the_storage_is_a_share_of_its_ceiling()
    {
        var readings = new HourlyReadings(null, _ => Task.FromResult<StorageReading?>(new StorageReading(512, 2048)), TimeProvider.System);

        await readings.PassAsync(CancellationToken.None);

        var view = JsonSerializer.SerializeToElement(readings.View);
        Assert.Equal(JsonValueKind.Null, view.GetProperty("certificate").ValueKind);
        Assert.Equal(0.25, view.GetProperty("sql_storage").GetProperty("share").GetDouble());
    }

    [Fact]
    public async Task A_store_with_no_reading_is_no_storage_and_never_an_error()
    {
        var readings = new HourlyReadings(null, _ => Task.FromResult<StorageReading?>(null), TimeProvider.System);

        await readings.PassAsync(CancellationToken.None);

        Assert.Equal(JsonValueKind.Null, JsonSerializer.SerializeToElement(readings.View).GetProperty("sql_storage").ValueKind);
        Assert.Null(await StorageStats.ReadAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task The_azure_answer_carries_the_hourly_readings()
    {
        var azure = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/admin/azure");

        Assert.True(azure.TryGetProperty("hourly", out _));
    }
    // #endregion hourly-readings-tests
}
