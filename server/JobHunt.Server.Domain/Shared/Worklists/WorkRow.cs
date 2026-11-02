using System.Text;

namespace JobHunt.Server.Domain.Shared.Worklists;

/// <summary>One application's resume: the send copy, the headline it carries and its SHA-256, or why it did not build.</summary>
/// <param name="Path">The send copy's real path.</param>
/// <param name="Headline">The headline used.</param>
/// <param name="Sha">The send copy's SHA-256.</param>
/// <param name="Failure">Why the build stopped, or null.</param>
public sealed record ResumeBuild(string Path, string Headline, string Sha, string? Failure = null);

/// <summary>What a worklist row asks a lane to do.</summary>
public enum WorkAction
{
    /// <summary>A connection request with a note.</summary>
    Invite,

    /// <summary>A message to a first-degree connection.</summary>
    Message,

    /// <summary>A job application.</summary>
    Apply,
}

/// <summary>One line of a lane's worklist, everything the model needs and nothing it must decide.</summary>
/// <param name="Row">The row's number in the worklist, which the result answers by.</param>
/// <param name="Person">The person, empty when the lane finds the name.</param>
/// <param name="Company">The company.</param>
/// <param name="Action">The action.</param>
/// <param name="TextFile">The file holding the drafted text, under the run folder; empty for an application.</param>
/// <param name="Code">The link code.</param>
/// <param name="Url">The profile or posting URL, empty when the lane finds it.</param>
/// <param name="Source">file:line or Count.</param>
/// <param name="Avoid">People already on the tracker at this company, whom a lane picking a name must not pick.</param>
/// <param name="Role">An application's posting title as the source has it.</param>
/// <param name="Resume">An application's tailored resume.</param>
public sealed record WorkRow(int Row, string Person, string Company, WorkAction Action, string TextFile, string Code, string Url, string Source, string Avoid = "", string Role = "", ResumeBuild? Resume = null);

/// <summary>A candidate left out of a worklist, and why.</summary>
/// <param name="What">The person or company.</param>
/// <param name="Reason">The gate's words.</param>
public sealed record Dropped(string What, string Reason);

/// <summary>The worklist file: tab-separated, one header, in the order the lane works it.</summary>
public static class WorklistTsv
{
    /// <summary>The header line.</summary>
    public const string Header = "row\tperson\tcompany\taction\ttext_file\tcode\turl\tsource\tavoid\trole\tresume";

    /// <summary>The whole file.</summary>
    /// <param name="rows">The rows.</param>
    public static string Render(IEnumerable<WorkRow> rows)
    {
        var text = new StringBuilder(Header).Append('\n');
        foreach (WorkRow row in rows)
        {
            text.AppendJoin('\t', row.Row, Cell(row.Person), Cell(row.Company), row.Action.ToString().ToLowerInvariant(), Cell(row.TextFile), Cell(row.Code), Cell(row.Url), Cell(row.Source), Cell(row.Avoid), Cell(row.Role), Cell(row.Resume?.Path ?? string.Empty)).Append('\n');
        }
        return text.ToString();
    }

    private static string Cell(string value) => value.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
}
