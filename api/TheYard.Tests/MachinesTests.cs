using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api;
using TheYard.Application;

namespace TheYard.Tests;

/// <summary>
/// What the machines are doing (ADR: What the machines are doing): the
/// container's own memory and processor share, the relational store's reading
/// of itself, and what the document store charged. Three readings with three
/// different honesties, which is most of what these tests hold.
/// </summary>
public class MachinesTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void A_sample_reports_memory_and_leaves_the_first_processor_share_empty()
    {
        var sampler = new MachineSampler(4);

        var first = sampler.Sample();
        var second = sampler.Sample();

        Assert.True(first.WorkingSetMb > 0, "a process with no working set is not a process");
        Assert.True(first.ManagedMb > 0);
        Assert.True(first.Threads > 0);
        // Nothing to subtract from on the first reading, so it reports nothing
        // rather than the whole life of the process wearing an interval's label.
        Assert.Null(first.CpuPercent);
        Assert.NotNull(second.CpuPercent);
        Assert.True(second.CpuPercent >= 0);
        Assert.True(MachineSampler.MemoryLimitMb > 0);
        Assert.True(MachineSampler.Processors > 0);
    }

    [Fact]
    public void The_ring_keeps_the_last_samples_in_the_order_they_were_taken()
    {
        var sampler = new MachineSampler(3);

        for (int i = 0; i < 5; i++)
        {
            sampler.Sample();
        }
        var kept = sampler.Snapshot();

        Assert.Equal(3, kept.Count);
        Assert.Equal(kept.OrderBy(sample => sample.At).ToList(), kept);
    }

    [Fact]
    public void The_document_reading_folds_the_operations_ring_into_minutes_against_the_free_allowance()
    {
        var at = new DateTimeOffset(2026, 9, 19, 11, 30, 0, TimeSpan.Zero);
        var operations = new List<StoreOperation>
        {
            Operation(at, 2.5, 4),
            Operation(at.AddSeconds(20), 3.5, 6),
            Operation(at.AddMinutes(1), 6, 10),
        };

        var view = DocumentLoad.From(operations, "Azure Cosmos DB");

        Assert.True(view.Available);
        Assert.Equal(12, view.RequestUnits);
        Assert.Equal(3, view.Operations);
        Assert.Equal(2, view.Minutes.Count);
        Assert.Equal(6, view.Minutes[0].RequestUnits);
        Assert.Equal(2, view.Minutes[0].Operations);
        // Six request units in a minute is a hundredth of one second's free
        // allowance, which is the comparison the card makes.
        Assert.Equal(Math.Round(6d / 60 / DocumentLoad.FreeRequestUnitsPerSecond * 100, 3), view.Minutes[0].ShareOfFreePercent);
        // Nearest rank over three samples: the median is the second of them.
        Assert.Equal(6, view.P50Ms);
        Assert.Equal(10, view.P95Ms);
    }

    [Fact]
    public void An_empty_operations_ring_says_so_rather_than_drawing_a_zero()
    {
        var view = DocumentLoad.From([], "Azure Cosmos DB");

        Assert.False(view.Available);
        Assert.NotNull(view.Note);
        Assert.Empty(view.Minutes);
    }

    [Fact]
    public async Task The_resource_view_is_absent_and_says_why_where_there_is_no_azure_sql_to_read_it_from()
    {
        var load = await ResourceStats.ReadAsync(null, 10, CancellationToken.None);

        Assert.False(load.Available);
        Assert.NotNull(load.Note);
        Assert.Empty(load.Rows);
    }

    /// <summary>
    /// The endpoint answers with all three readings whatever this container is
    /// running: the suite runs on SQLite, where the relational reading is
    /// absent with its reason, and that shape is the one a reader meets on a
    /// developer's machine.
    /// </summary>
    [Fact]
    public async Task The_endpoint_answers_with_the_container_and_both_stores()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/admin/machines");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var container = body.RootElement.GetProperty("container");
        Assert.True(container.GetProperty("memory_limit_mb").GetDouble() > 0);
        Assert.True(container.GetProperty("processors").GetInt32() > 0);
        Assert.True(container.GetProperty("samples").GetArrayLength() > 0, "the sampler takes one reading as it starts");

        var relational = body.RootElement.GetProperty("relational");
        Assert.False(relational.GetProperty("available").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(relational.GetProperty("note").GetString()));

        var document = body.RootElement.GetProperty("document");
        Assert.True(document.TryGetProperty("free_request_units_per_second", out var allowance));
        Assert.Equal(DocumentLoad.FreeRequestUnitsPerSecond, allowance.GetInt32());
    }

    private static StoreOperation Operation(DateTimeOffset at, double charge, long ms) =>
        new(at, "vehicles", StoreOperationKind.PointRead, "read", [], "one", 1, charge, ms, "ok", "/api/vehicles", null);
}
