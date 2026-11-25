namespace JobHunt.Server.Domain.Shared.Records;

/// <summary>
/// The claude CLI's sign-in on WickedAwsome. A model step that answers "Failed to authenticate" never ran, so it is not
/// run, sign-in expired: never a lane failing, never an empty run, never a heal (audit1124 follow-ups 1 and 2). The card
/// says so in these words, and check_empty_runs.py and canary.py read them as not run.
/// </summary>
public static class SignIn
{
    /// <summary>What the CLI's result line says when its sign-in has expired.</summary>
    public const string Refused = "Failed to authenticate";

    /// <summary>A lane's words when its model step met an expired sign-in.</summary>
    public const string NotRun = "not run, sign-in expired: the claude CLI on WickedAwsome answered \"Failed to authenticate: OAuth session expired\"; "
        + "double-click Sign in to Claude on its desktop";

    /// <summary>True when a CLI result line is the sign-in refusing, not the model answering.</summary>
    /// <param name="resultLine">The stream's result line, or null.</param>
    public static bool Expired(string? resultLine) => resultLine?.Contains(Refused, StringComparison.Ordinal) == true;

    /// <summary>True when a lane's report is the sign-in refusing: its model step said so, or its recount held it so.</summary>
    /// <param name="report">The lane's report.</param>
    public static bool Out(LaneReport report) => report.SignedOut || report.Held == NotRun;
}
