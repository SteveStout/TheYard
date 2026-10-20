using TheYard.Application;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Tests;

// #region cloud-card-handler-tests
/// <summary>The resource cards' handler with fake cards at its port and no host (ADR: The onion cut into vertical slices, rule 7).</summary>
public sealed class CloudCardHandlerTests
{
    private sealed class FakeCards(params string[] keys) : ICloudCards
    {
        public List<(string Key, string? Window)> Asked { get; } = [];

        public bool Knows(string key) => keys.Contains(key);

        public Task<CloudReport> ReadAsync(string key, string? window, CancellationToken cancellation)
        {
            Asked.Add((key, window));
            return Task.FromResult(new CloudReport(window ?? "", ["24h", "7d", "30d"], false, key, 0, null, key, null, null, [], [], "", null!, [], null!));
        }
    }

    [Fact]
    public async Task A_known_card_is_read_for_its_window_and_an_unknown_window_is_the_day()
    {
        var cards = new FakeCards("sqldb", "edge");
        var handler = new ReadCloudCardHandler(cards);

        Assert.Equal("7d", (await handler.HandleAsync("sqldb", "7d", CancellationToken.None))!.Window);
        Assert.Equal("24h", (await handler.HandleAsync("edge", "sideways", CancellationToken.None))!.Window);
        Assert.Equal([("sqldb", "7d"), ("edge", "24h")], cards.Asked);
    }

    [Fact]
    public async Task An_unknown_key_is_no_card_and_reads_nothing()
    {
        var cards = new FakeCards("sqldb");

        Assert.Null(await new ReadCloudCardHandler(cards).HandleAsync("vault", "24h", CancellationToken.None));
        Assert.Empty(cards.Asked);
    }
}
// #endregion cloud-card-handler-tests
