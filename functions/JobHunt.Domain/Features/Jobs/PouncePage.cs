using System.Text;

namespace JobHunt.Domain.Features.Jobs;

/// <summary>
/// The 90-day page for a role the pounce passed, queued beside the wrapper that starts its first touch: one dispatcher
/// item that writes the role's spec with its red flag scan first, runs gen90.py once for the leader the feed named, and
/// logs what came of it on nightly/pounce/pages.log. The page is built in shadow whatever POUNCE_RUN says: it is never
/// attached and never linked in any message until Steve answers the ruling on the 90-day page, so the item reads the
/// same in a dry run and a live one. It runs on Sonnet, which the dispatcher's Opus hours never hold.
/// </summary>
/// <remarks>
/// The item never holds up a first touch. Its 20 sorts it after every wrapper (10), and it needs what a wrapper needs,
/// so it is never eligible while a wrapper waits on the browser; a wrapper that lands while the page is being built
/// makes the item put a copy of itself back as a follow-on and stop, at any of its four checks, the last right before
/// gen90.py runs. A page that fails a gen90 check, a company the
/// scan flags, or a role whose feed named no leader gets no page and a log line saying which and why.
/// </remarks>
public static class PouncePage
{
    /// <summary>The page log on the share, one line a role.</summary>
    public const string Log = "nightly/pounce/pages.log";

    private const string Template = "/mnt/j/mentor/90day-template";

    /// <summary>The page item's lane, which names its dispatcher folder and the code gen90.py mints.</summary>
    public static string Lane(CaughtRole role) => $"pounce90-{role.Key}";

    /// <summary>The page item's path; 20 sorts it after the role's wrapper.</summary>
    public static string ReadyPath(CaughtRole role) => $"{PounceHandoff.Ready}/20-{Lane(role)}.md";

    /// <summary>The folder on the box the role's posting, spec and page are written to.</summary>
    public static string Folder(CaughtRole role) => $"/mnt/j/{PounceHandoff.Folder}/{PounceHandoff.Stamp(role)}-{role.Key}-90day";

    /// <summary>The page item: a header the dispatcher reads, then the steps a Sonnet lane follows.</summary>
    /// <param name="role">The role.</param>
    public static string Item(CaughtRole role)
    {
        string lane = Lane(role);
        string folder = Folder(role);
        string done = $@"J:\nightly\pounce\{PounceHandoff.Stamp(role)}-{role.Key}-90day-DONE.md";
        string requeue = $"/mnt/j/{PounceHandoff.Folder}/requeue/20-{lane}.md";
        string log = $"/mnt/j/{Log}";
        string line = $"<yyyy-MM-dd HH:mm> Central {role.Key} {role.Company} | <the leader>";
        var item = new StringBuilder();
        item.Append($"Lane: {lane}\nModel: claude-sonnet-5-5\nEffort: medium\nHolds:\nNeeds: chrome, jdrive\nStall_minutes: 20\nMax_hours: 1\nScope: jobhunt\n");
        item.Append($"Entered_by: the Function's pounce tick, which passed posting {role.Key} at {PounceHandoff.Central(role.PassedAt)} Central\n");
        item.Append("Approved_by: the pounce plan, the 90-day page in shadow (built and logged, never attached)\n");
        item.Append($"Done_note: {done}\n\n");
        item.Append($"# {lane}: the 90-day page for one caught role, in shadow\n\n");
        item.Append("> RULES FIRST: read systemRules.md before acting on this file (MASTER RULE: if stuck or a tech glitch happens, retry, then reread the .md rules).\n\n");
        item.Append("## The role\n\n");
        item.Append($"{role.Company}, {role.Title}, {role.Url}. Passed the pounce gate {PounceHandoff.Central(role.PassedAt)} Central. Its first touch is the item 10-pounce-{role.Key}.md.\n\n");
        item.Append("## What you are\n\n");
        item.Append("A page builder in shadow. The page is built and logged, never attached to anything and never linked in any message, until Steve answers the ruling on the 90-day page in J:\\dispatch\\DECISION.md. ");
        item.Append($"You send nothing and open no browser. You write only in {folder}/, one line on {log}, your DONE note and your CLOSED row. You never hold up a first touch: step 1 says how.\n\n");
        item.Append("## Steps (on this box J:\\ is /mnt/j)\n\n");
        item.Append($"1. A first touch comes first. Run `ls /mnt/j/dispatch/ready/10-pounce-*.md`. If it lists a file, run `mkdir -p {Path(requeue)}`, copy /mnt/j/dispatch/running/{lane}/prompt.md to {requeue}, write the DONE note saying you yielded to that first touch with the line `FOLLOW-ON: J:\\nightly\\pounce\\requeue\\20-{lane}.md`, write the CLOSED row (step 10) and stop. Repeat this check before steps 3, 6 and 7.\n");
        item.Append($"2. The leader the feed named. Read the newest of `ls -t /mnt/j/linkedin-proof/QUEUE-pounce-*-{role.Key}.md /mnt/j/nightly/*/pounce-{role.Key}-feed-*/QUEUE-pounce-*-{role.Key}.md`; the leader is the name in its first block's heading, `### 1. <Name>, <Company> (<Seat>)`. ");
        item.Append($"With no file, or a first block that names no one, append `{line} | NO PAGE no leader: <what you found>` to {log}, write the DONE note and the CLOSED row, and stop.\n");
        item.Append($"3. The posting. Run `mkdir -p {folder}`, read {role.Url} and save its text word for word as {folder}/posting.txt.\n");
        item.Append($"4. The red flag scan, before anything else goes in the spec (RULE 64 and CONVENTIONS, THE 90-DAY PAGE): the company's careers board for where the engineering team sits, offshore or contractor build-outs, headcount by region, layoffs in the last year and recent interview reviews. Write {folder}/spec.json holding only `redflag_scan`: date, team_location, flags, probes, sources and verdict, as Billtrust-spec.json in {Template} has them. `flags` is `none` when nothing was found.\n");
        item.Append($"5. A flag is the end. If `flags` is anything but `none`, append `{line} | NO PAGE red flag: <the flags on one line>` to {log}, put the flags in the DONE note's LEFT FOR STEVE section, write the CLOSED row and stop. A flagged company gets no page.\n");
        item.Append($"6. The spec. Fill the rest of {folder}/spec.json in the shape of {Template}/EXAMPLE-spec.json: role, company, manager (the leader), date, posting_url, lede, four stats from PROOF-BANK.json, four points each with a `jd` line copied word for word from posting.txt, a PROOF-BANK quote and its proof links, and `posting_text_file` set to {folder}/posting.txt. Every fact comes from PROOF-BANK.json and PROOF-INDEX.json; never a number they do not hold, and no em dash.\n");
        item.Append("7. Do step 1's check once more, then run gen90.py once:\n\n```\n");
        item.Append($"cd {Template} && LANE={lane} python3 -I {Template}/gen90.py {folder}/spec.json {folder}/page --person \"<the leader>\" --channel LinkedIn --role \"{Quoted(role.Title)}, 90-day page, shadow, never attached\"\n");
        item.Append("```\n\n   Never run gen90.py a second time and never change the spec to pass a check: a page that fails a check is a page not built.\n");
        item.Append($"8. The log line. On `GEN90 PASS`, append `{line} | PAGE {folder}/page.pdf, code <the code it printed>, never attached` to {log}. On `GEN90 FAIL`, append `{line} | NO PAGE gen90: <each failing check word for word, joined by \"; \">`. Attach the page to nothing and name it in no message.\n");
        item.Append($"9. Write the DONE note at {done}, the RULES FIRST line on top: the start and end times, the leader, the red flag scan's verdict, gen90.py's output word for word, the line written to pages.log, and a `## LEFT FOR STEVE` section holding `- none` or the red flags.\n");
        item.Append($"10. Write your CLOSED row through the state.md spool: a new file /mnt/j/inbox/append-inbox/state.md/<UTC yyyyMMdd-HHmmss>Z-{lane}-close.md holding one line that begins `## LANE: {lane} CLOSED <HH:MM> CST`, says `Holds: none` and the DONE note's path, and ends ` | CLOSED |`. Then stop.\n");
        return item.ToString();
    }

    private static string Path(string file) => file[..file.LastIndexOf('/')];

    private static string Quoted(string title) => title.Replace("\"", "'", StringComparison.Ordinal).Replace("`", "'", StringComparison.Ordinal);
}
