// One quality number's readings, version by version (ADR-130, Quality you can watch), as the Quality page's trend
// fence draws them: shared because the Docs slice reads the history for its page and the review slice serves the
// same readings to the review route's short path, and neither may reach into the other's folder for it.
namespace TheYard.Application.Shared.History;

// #region quality-trend-port
/// <summary>Port: one quality number's readings, one line a reading, in the trend fence's own shape.</summary>
public interface IQualityTrend
{
    /// <summary>
    /// The readings of one number, oldest first, each "date | version | value | source" as the Quality page's fence
    /// holds them; empty when the history has no such number.
    /// </summary>
    /// <param name="number">The number's key in the history, such as async_without_cancellation.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    Task<IReadOnlyList<string>> ReadingsAsync(string number, CancellationToken cancellation);
}
// #endregion quality-trend-port
