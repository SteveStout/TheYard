using TheYard.Api.Features.Docs;

namespace TheYard.Tests;

/// <summary>
/// The Quality page's readouts and tiles (QualityReadouts, and the History it reads them from): each number joins
/// the ratchet at the first row whose limits name it, so its join is its own; a tile's note and bar follow from which
/// way the number may move and where it stood at its join; the readouts count what is held and when it was measured;
/// and CodeRabbit's counts never read an empty cell as a zero. Held on a small repository written for the test and
/// thrown away with it, never the real data (RULE 79).
/// </summary>
public sealed class QualityReadoutsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"quality-readouts-{Guid.NewGuid():N}");

    public QualityReadoutsTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "data"));
        // Three scanned rows and one pre-scan row: the ratchet goes in at 1.0.4.115, the headers join at 1.0.5.1.
        File.WriteAllText(Path.Combine(root, "data", "quality-history.json"), """
            {"first_ratchet_version": "1.0.4.115", "rows": [
              {"version": "", "date": "2026-10-10", "source": "pre-scan", "numbers": {"async_without_cancellation": 40, "blocking_waits": 5}, "limits": null},
              {"version": "1.0.3.139", "date": "2026-10-15", "source": "quality.py",
               "numbers": {"async_without_cancellation": 51, "blocking_waits": 2, "files_over_300": 0, "react_files_without_header": 300, "dotnet_tests_higher_is_better": 950, "history_or_name_comments": 4},
               "limits": null},
              {"version": "1.0.4.115", "date": "2026-10-29", "source": "quality.py",
               "numbers": {"async_without_cancellation": 107, "blocking_waits": 0, "files_over_300": 0, "react_files_without_header": 280, "dotnet_tests_higher_is_better": 2255, "history_or_name_comments": 27},
               "limits": {"async_without_cancellation": 107, "blocking_waits": 0, "files_over_300": 0, "dotnet_tests_higher_is_better": 2255, "history_or_name_comments": 27}},
              {"version": "1.0.5.1", "date": "2026-11-05", "source": "quality.py",
               "numbers": {"async_without_cancellation": 20, "blocking_waits": 0, "files_over_300": 0, "react_files_without_header": 241, "dotnet_tests_higher_is_better": 3000, "history_or_name_comments": 3},
               "limits": {"async_without_cancellation": 20, "blocking_waits": 0, "files_over_300": 0, "react_files_without_header": 241, "dotnet_tests_higher_is_better": 3000, "history_or_name_comments": 3}},
              {"version": "1.0.5.121", "date": "2026-11-20", "source": "quality.py",
               "numbers": {"async_without_cancellation": 1, "blocking_waits": 0, "files_over_300": 0, "react_files_without_header": 240, "dotnet_tests_higher_is_better": 4554, "history_or_name_comments": 0},
               "limits": {"async_without_cancellation": 1, "blocking_waits": 0, "files_over_300": 0, "react_files_without_header": 239, "dotnet_tests_higher_is_better": 4554, "history_or_name_comments": 0}}
            ]}
            """);
        File.WriteAllText(Path.Combine(root, "data", "coderabbit-findings.json"), """
            {"rows": [
              {"version": "1.0.4.176", "reviewed": "full", "findings": {"critical": 0, "major": 1, "minor": 2}, "fixed": 2, "answered_as_wrong": 1},
              {"version": "1.0.4.177", "reviewed": "none", "findings": {"critical": 0, "major": 0, "minor": 0}, "fixed": 0, "answered_as_wrong": 0},
              {"version": "1.0.5.121", "reviewed": "full", "findings": {"critical": 0, "major": 0, "minor": 4}, "fixed": null, "answered_as_wrong": null}
            ]}
            """);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Measure(string kind, string? argument = null) => QualityReadouts.Measure(kind, argument, root);

    [Fact]
    public void Each_number_joins_at_the_first_row_whose_limits_name_it()
    {
        Assert.Equal("2,255", Measure("quality-join", "dotnet_tests_higher_is_better"));
        Assert.Equal("241", Measure("quality-join", "react_files_without_header"));
        Assert.Equal("107", Measure("quality-join", "async_without_cancellation"));
    }

    [Fact]
    public void A_falling_number_s_note_names_its_own_join_and_its_bar_is_now_against_the_join()
    {
        Assert.Equal("1 | fall | 2 | 107 at the ratchet, may only fall", Measure("quality-tile", "async_without_cancellation"));
        Assert.Equal("240 | fall | 100 | 241 when it joined at 1.0.5.1, may only fall", Measure("quality-tile", "react_files_without_header"));
        // At zero now, but above zero at the join: still a falling number, its bar empty.
        Assert.Equal("0 | fall | 0 | 27 at the ratchet, may only fall", Measure("quality-tile", "history_or_name_comments"));
    }

    [Fact]
    public void A_rising_number_s_bar_is_its_join_against_now_and_the_rest_is_its_gain()
    {
        Assert.Equal("4,554 | rise | 50 | 2,255 at the ratchet, may only rise", Measure("quality-tile", "dotnet_tests_higher_is_better"));
    }

    [Fact]
    public void A_number_zero_at_its_join_and_today_is_held_and_says_whether_it_was_ever_above_zero()
    {
        Assert.Equal("0 | zero | 0 | zero at the ratchet, zero today", Measure("quality-tile", "files_over_300"));
        // The pre-scan's 5 counts as a reading the number had, so the peak is 5, before the ratchet.
        Assert.Equal("0 | zero | 0 | peaked at 5 before the ratchet; zero since", Measure("quality-tile", "blocking_waits"));
    }

    [Fact]
    public void The_readouts_count_what_is_held_and_when_it_was_measured()
    {
        // The headers sit at 240 over a limit of 239: five of six held.
        Assert.Equal("5", Measure("quality-held"));
        Assert.Equal("6", Measure("quality-count"));
        Assert.Equal("1.0.5.121", Measure("quality-version"));
        Assert.Equal("Oct 15 to Nov 20", Measure("quality-span"));
    }

    [Fact]
    public void CodeRabbit_s_counts_read_the_log_and_an_empty_cell_is_not_read_yet_never_a_zero()
    {
        Assert.Equal("2", Measure("coderabbit-reviewed"));
        Assert.Equal("3", Measure("coderabbit-logged"));
        Assert.Equal("1.0.4.176", Measure("coderabbit-first"));
        Assert.Equal("7", Measure("coderabbit-findings"));
        Assert.Equal("2", Measure("coderabbit-fixed"));
        Assert.Equal("1", Measure("coderabbit-wrong"));
        Assert.Equal("1", Measure("coderabbit-unread"));
    }

    [Fact]
    public void A_number_the_history_does_not_hold_is_said_in_words()
    {
        Assert.Equal("(the data for 'quality-tile nobody_measures_this' is not in this build)", Measure("quality-tile", "nobody_measures_this"));
        Assert.Equal("(no live measure named 'quality-nothing')", Measure("quality-nothing"));
    }
}
