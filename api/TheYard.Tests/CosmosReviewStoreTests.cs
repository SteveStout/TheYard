using Microsoft.Azure.Cosmos;
using TheYard.Application.Features.Review;
using TheYard.Infrastructure.Cosmos.Features.Review;

namespace TheYard.Tests;

/// <summary>
/// The review route's reach in the document store (ADR: Ten minutes, seven stops, the addendum on the reach
/// rows), against the real account under the same trait as <c>CosmosStoreTests</c>, so CI and the box's local
/// run filter it out and the gate's live pass runs it as the signed-in principal. As the other activity
/// adapters' tests do, it asks whether the container is there before it asserts anything was kept: where it
/// is not, the only true thing is that nothing is kept and every read is empty. Where it is, the test writes
/// under a day long past and a hash nobody else uses, and takes its document away again.
/// </summary>
[Trait(CosmosLive.Trait, CosmosLive.Value)]
public sealed class CosmosReviewStoreTests
{
    private const string OldDay = "2026-01-03";

    [Fact]
    public async Task A_lower_stop_never_lowers_a_kept_row_and_the_rows_read_back_under_their_kind()
    {
        var (store, _) = CosmosLive.Open();
        var review = new CosmosReviewStore(store);
        string hash = Guid.NewGuid().ToString("N");
        var at = new DateTimeOffset(2026, 1, 3, 12, 0, 0, TimeSpan.Zero);

        if (!await review.AvailableAsync(CancellationToken.None))
        {
            await review.KeepAsync([new ReviewRow(hash, OldDay, 3, "zzrev1", at, at)], CancellationToken.None);
            Assert.Empty(await review.RowsSinceAsync(OldDay, CancellationToken.None));
            Assert.Null(await review.FirstDayAsync(CancellationToken.None));
            return;
        }

        try
        {
            await review.KeepAsync([new ReviewRow(hash, OldDay, 3, "zzrev1", at, at)], CancellationToken.None);
            await review.KeepAsync([new ReviewRow(hash, OldDay, 1, "zzother", at.AddMinutes(5), at.AddMinutes(5))], CancellationToken.None);

            var mine = Assert.Single(await review.RowsSinceAsync(OldDay, CancellationToken.None), row => row.Hash == hash);
            Assert.Equal((3, "zzrev1", at, at.AddMinutes(5)), (mine.Stop, mine.Code, mine.First, mine.Last));
            var first = await review.FirstDayAsync(CancellationToken.None);
            Assert.NotNull(first);
            Assert.True(string.CompareOrdinal(first, OldDay) <= 0, $"the first day kept is {first}, after this test's own row on {OldDay}");
        }
        finally
        {
            try
            {
                await store.ContainerNamed(Containers.Activity).DeleteItemAsync<ReviewDocument>(ReviewDocument.IdFor(OldDay, hash), new PartitionKey(OldDay));
            }
            catch (CosmosException)
            {
                // Nothing was written, or it is gone already.
            }
        }
    }
}
