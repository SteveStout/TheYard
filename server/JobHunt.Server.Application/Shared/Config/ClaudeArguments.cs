namespace JobHunt.Server.Application.Shared.Config;

/// <summary>
/// The CLI's arguments for a lane. The allowed list is the safety: in print mode a tool not on it is refused, and that
/// is the safe failure, so nothing here ever skips the permission check. Write is scoped to the run folder and Bash is
/// not listed at all. A dry run's list holds only reading tools: without `computer` or `form_input` the model has no
/// way to click, so a dry run cannot send whatever its prompt says. The live list leaves out `browser_batch` and
/// `javascript_tool`, because a batch or a script against linkedin.com is the loop the human pace rule forbids.
/// --strict-mcp-config leaves the extension as the only server, so no mail or calendar connector is even loaded, and
/// the refused list names what the allowed list already leaves out, so nothing rests on one list alone. With a second
/// computer's Chrome connected the extension answers nothing until a browser is selected, so every run may select one;
/// the lane doc says it is this box's and no other. switch_browser stays refused: it pages every Chrome for a click.
/// The apply lane may write anywhere in its run folder but the essay desk's verdicts, which only the program writes.
/// The feed reads the share's inputs it ranks from and LinkedIn, and live it gets `computer` for scrolling and
/// screenshots only; no form tool, so it can fill nothing and send nothing. The network lane reads LinkedIn Messaging
/// the same way, with nothing of the share's but its run folder and its lane doc. A slot run for one caught role tells
/// the feed so in the prompt and lets it read that role's hand-off and nothing more; the lane doc is unchanged.
/// </summary>
public static class ClaudeArguments
{
    private const string Chrome = "mcp__claude-in-chrome__";

    private const string RecountTurns = "80";

    private static readonly string[] Reading = ["list_connected_browsers", "select_browser", "tabs_context_mcp", "tabs_create_mcp", "tabs_close_mcp", "navigate", "read_page", "get_page_text", "find"];

    private static readonly string[] Acting = ["computer", "form_input"];

    private static readonly string[] FeedReads =
    [
        "PRIORITY-BOARD.md", "Job_Hunt_Tracker.csv", "inbox/tracker-inbox/**", "staging/apply-*/OUTREACH-DRAFTS.md", "MANAGER-DIRECT-LANE.md",
        "INBOUND-HIDDEN-2026-10-08.md", "linkedin-proof/QUEUE-*.md", "linkedin-proof/FEED-HOLD.md", "CONVENTIONS.md", "targeting-criteria.md",
    ];

    private static readonly string[] Refused =
    [
        "Bash", "Task", "Agent", "WebFetch", "WebSearch", "CronCreate", "CronDelete", "CronList", "RemoteTrigger", "Workflow", "Skill", "SendMessage", "PushNotification", "ScheduleWakeup",
        "NotebookEdit", "EnterWorktree", "ExitWorktree", "DesignSync", Chrome + "browser_batch", Chrome + "javascript_tool", Chrome + "shortcuts_execute", Chrome + "shortcuts_list", Chrome + "gif_creator",
        Chrome + "upload_image", Chrome + "read_network_requests", Chrome + "read_console_messages", Chrome + "switch_browser",
    ];

    /// <summary>The argument list.</summary>
    /// <param name="config">The lane's settings.</param>
    /// <param name="lane">The lane.</param>
    /// <param name="share">The share's real path.</param>
    /// <param name="runFolder">The run folder's real path.</param>
    /// <param name="dry">True for a dry run.</param>
    /// <param name="role">The caught role a slot run's feed works, or null.</param>
    public static IReadOnlyList<string> For(LaneConfig config, Lane lane, string share, string runFolder, bool dry, RoleHandoff? role = null)
    {
        var arguments = new List<string>
        {
            "-p", Prompt(lane, share, runFolder, dry, role),
            "--model", config.Model,
            "--effort", config.Effort,
            "--max-turns", config.MaxTurns.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--output-format", "stream-json",
            "--verbose",
            "--json-schema", LaneResultSchema.Json,
            "--chrome",
            "--strict-mcp-config",
            "--add-dir", share,
            "--allowedTools",
        };
        arguments.AddRange(Allowed(lane, share, runFolder, dry, role));
        arguments.Add("--disallowedTools");
        arguments.AddRange(Refused);
        if (lane == Lane.Apply)
        {
            arguments.AddRange(new[] { "Write", "Edit" }.Select(tool => $"{tool}({Rooted(runFolder)}/essays/*.verdict)"));
        }
        return arguments;
    }

    /// <summary>
    /// The argument list for the recount, the short run before the invite lane that gives the program the count from the
    /// sent page. It holds no form tool, and it scrolls with `computer`, which a dry run does not get.
    /// </summary>
    /// <param name="config">The lane's settings.</param>
    /// <param name="share">The share's real path.</param>
    /// <param name="runFolder">The run folder's real path.</param>
    /// <param name="dry">True for a dry run.</param>
    public static IReadOnlyList<string> Recount(LaneConfig config, string share, string runFolder, bool dry)
    {
        var arguments = new List<string>
        {
            "-p", $"Read {share}/nightly/{RunSlot.Doc(Lane.Invites)} and do its section Recount and nothing else. JOBHUNT={share}. DRY_RUN={dry.ToString().ToLowerInvariant()}. Send nothing, click no button, return rows empty.",
            "--model", config.Model,
            "--effort", config.Effort,
            "--max-turns", RecountTurns,
            "--output-format", "stream-json",
            "--verbose",
            "--json-schema", LaneResultSchema.Json,
            "--chrome",
            "--strict-mcp-config",
            "--add-dir", share,
            "--allowedTools",
            $"Read({Rooted(share)}/nightly/{RunSlot.Doc(Lane.Invites)})",
            $"Read({Rooted(share)}/systemRules.md)",
        };
        arguments.AddRange(Reading.Select(tool => Chrome + tool));
        if (!dry)
        {
            arguments.Add(Chrome + "computer");
        }
        arguments.Add("--disallowedTools");
        arguments.AddRange(Refused);
        return arguments;
    }

    /// <summary>The allowed tools: exactly the browser tools the lane needs, plus Read and Write under the run folder.</summary>
    /// <param name="lane">The lane.</param>
    /// <param name="share">The share's real path.</param>
    /// <param name="runFolder">The run folder's real path.</param>
    /// <param name="dry">True for a dry run.</param>
    /// <param name="role">The caught role a slot run's feed works, whose hand-off it may read, or null.</param>
    public static IReadOnlyList<string> Allowed(Lane lane, string share, string runFolder, bool dry, RoleHandoff? role = null)
    {
        var tools = new List<string>
        {
            $"Read({Rooted(runFolder)}/**)",
            $"Read({Rooted(share)}/nightly/{RunSlot.Doc(lane)})",
            $"Read({Rooted(share)}/systemRules.md)",
            $"Edit({Rooted(runFolder)}/**)",
            $"Write({Rooted(runFolder)}/**)",
        };
        if (lane == Lane.Apply)
        {
            tools.Add($"Read({Rooted(share)}/standard-answers.md)");
            tools.Add($"Read({Rooted(share)}/targeting-criteria.md)");
            tools.Add($"Read({Rooted(share)}/EVIDENCE-BANK.md)");
            tools.Add($"Read({Rooted(share)}/writing-samples.md)");
        }
        if (lane == Lane.Feed)
        {
            tools.AddRange(FeedReads.Select(path => $"Read({Rooted(share)}/{path})"));
            tools.Add("Glob");
            tools.Add("Grep");
            if (role is not null)
            {
                tools.Add($"Read({Rooted(share)}/{role.Path})");
            }
        }
        tools.AddRange(Reading.Select(tool => Chrome + tool));
        if (!dry && lane is Lane.Feed or Lane.Network)
        {
            tools.Add(Chrome + "computer");
        }
        else if (!dry)
        {
            tools.AddRange(Acting.Select(tool => Chrome + tool));
            if (lane == Lane.Apply)
            {
                tools.Add(Chrome + "file_upload");
            }
        }
        return tools;
    }

    /// <summary>The prompt: the lane's doc on the share does the talking.</summary>
    /// <param name="lane">The lane.</param>
    /// <param name="share">The share's real path.</param>
    /// <param name="runFolder">The run folder's real path.</param>
    /// <param name="dry">True for a dry run.</param>
    /// <param name="role">The caught role a slot run's feed works, or null.</param>
    public static string Prompt(Lane lane, string share, string runFolder, bool dry, RoleHandoff? role = null) =>
        $"Read {share}/nightly/{RunSlot.Doc(lane)} and follow it. JOBHUNT={share}. RUN_DIR={runFolder}. "
        + (lane switch
        {
            Lane.Feed when role is not null => $"Candidates go to {runFolder}/candidates.json. {RolePrompt(share, role)}",
            Lane.Feed => $"Candidates go to {runFolder}/candidates.json.",
            Lane.Network => $"High-water mark in {runFolder}/high-water.txt. Threads go to {runFolder}/network-threads.tsv and {runFolder}/threads/.",
            Lane.Apply when role is not null => $"Worklist at {runFolder}/worklist.tsv. {ApplyRolePrompt(role)}",
            _ => $"Worklist at {runFolder}/worklist.tsv.",
        })
        + $" DRY_RUN={dry.ToString().ToLowerInvariant()}."
        + (dry ? " Dry run: read pages only, no click, no fill, no send; your tool list holds no click." : string.Empty);

    /// <summary>
    /// What a slot run tells the feed: the one role, read from its hand-off, in place of the ranked inputs; one person at
    /// that company, bucket A; and for a budget-flagged role the timeline question in place of the no-oriented one.
    /// </summary>
    private static string RolePrompt(string share, RoleHandoff role) =>
        $"POUNCE_ROLE={share}/{role.Path}. This run is for that one caught role only: {role.Company}, {role.Title}, {role.Url}. "
        + $"The inputs in rank order do not apply: find the one person who owns headcount for that seat at {role.Company}, read the seat live, and write at most one candidate, at {role.Company} and nowhere else, hot A."
        + (role.Budget ? $" The role went live in the last 21 days of the year: its one question is \"{TextCheck.Timeline}\" in place of the no-oriented ask." : string.Empty);

    /// <summary>
    /// What a slot run tells the apply lane: the one role, and that a form needing Steve is a blocked row whose reason
    /// names the control he has to answer, word for word, so the blockers list can say exactly what is waiting on him.
    /// </summary>
    private static string ApplyRolePrompt(RoleHandoff role) =>
        $"This run is for one caught role only: {role.Company}, {role.Title}, {role.Url}. Submit only when nothing on the form needs Steve. "
        + "A sign-in, an account, a signature, a covenant, an arbitration page or a required question no banked answer covers is a blocked row, never a guess: "
        + "its reason begins \"needs Steve:\" and names the exact control as the page labels it, such as the field \"Desired start date\" or the checkbox \"I agree to the arbitration terms\".";

    private static string Rooted(string absolute) => "//" + absolute.TrimStart('/');
}
