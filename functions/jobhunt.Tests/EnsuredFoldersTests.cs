using Xunit;

namespace JobHunt.Functions.Tests;

public class EnsuredFoldersTests
{
    [Fact]
    public void A_folder_made_sure_of_is_not_asked_for_again_within_the_hour()
    {
        var clock = new StepTime(new DateTimeOffset(2026, 10, 24, 13, 0, 0, TimeSpan.Zero));
        var ensured = new EnsuredFolders(clock);

        Assert.True(ensured.Needs("inbox/append-inbox"));
        ensured.Seen("inbox/append-inbox");
        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.False(ensured.Needs("inbox/append-inbox"));
        Assert.True(ensured.Needs("inbox/tracker-inbox"));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(ensured.Needs("inbox/append-inbox"));
    }

    [Fact]
    public void A_folder_forgotten_is_made_sure_of_at_the_next_write()
    {
        var ensured = new EnsuredFolders(TimeProvider.System);

        ensured.Seen("nightly");
        ensured.Forget("nightly");

        Assert.True(ensured.Needs("nightly"));
    }

    private sealed class StepTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
