using TheYard.Application;
using TheYard.Application.Features.Admin.Plan;

namespace TheYard.Tests;

// #region plan-handler-tests
/// <summary>The plan slice's handler with a fake card at its port and no host (ADR: The onion cut into vertical slices, rule 7).</summary>
public sealed class PlanHandlerTests
{
    private sealed class FakeCard : IPlanCard
    {
        public List<string?> Asked { get; } = [];

        public Task<CloudReport> ReadAsync(string? window, CancellationToken cancellation)
        {
            Asked.Add(window);
            return Task.FromResult(new CloudReport(window ?? "", ["24h", "7d", "30d"], false, "fake", 0, null, "The App Service plan", null, null, [], [], "", null!, [], null!));
        }
    }

    [Theory]
    [InlineData("24h", "24h")]
    [InlineData("7d", "7d")]
    [InlineData("30d", "30d")]
    [InlineData(null, "24h")]
    [InlineData("sideways", "24h")]
    [InlineData("1h", "24h")]
    public async Task A_window_the_card_offers_is_asked_for_and_anything_else_is_read_as_the_day(string? asked, string read)
    {
        var card = new FakeCard();
        var report = await new ReadPlanHandler(card).HandleAsync(asked, CancellationToken.None);

        Assert.Equal([read], card.Asked);
        Assert.Equal(read, report.Window);
    }
}
// #endregion plan-handler-tests
