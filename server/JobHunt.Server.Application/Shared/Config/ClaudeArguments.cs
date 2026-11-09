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
/// screenshots only; no form tool, so it can fill nothing and send nothing.
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
    public static IReadOnlyList<string> For(LaneConfig config, Lane lane, string share, string runFolder, bool dry)
    {
        var arguments = new List<string>
        {
            "-p", Prompt(lane, share, runFolder, dry),
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
        arguments.AddRange(Allowed(lane, share, runFolder, dry));
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
    public static IReadOnlyList<string> Allowed(Lane lane, string share, string runFolder, bool dry)
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
        }
        tools.AddRange(Reading.Select(tool => Chrome + tool));
        if (!dry && lane == Lane.Feed)
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
    public static string Prompt(Lane lane, string share, string runFolder, bool dry) =>
        $"Read {share}/nightly/{RunSlot.Doc(lane)} and follow it. JOBHUNT={share}. RUN_DIR={runFolder}. "
        + (lane == Lane.Feed ? $"Candidates go to {runFolder}/candidates.json." : $"Worklist at {runFolder}/worklist.tsv.")
        + $" DRY_RUN={dry.ToString().ToLowerInvariant()}."
        + (dry ? " Dry run: read pages only, no click, no fill, no send; your tool list holds no click." : string.Empty);

    private static string Rooted(string absolute) => "//" + absolute.TrimStart('/');
}
