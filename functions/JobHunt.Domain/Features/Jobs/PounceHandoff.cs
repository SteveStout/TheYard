using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace JobHunt.Domain.Features.Jobs;

/// <summary>A role the pounce passed, as its hand-off carries it to the box.</summary>
/// <param name="Key">jobhunt.Postings.Id, which names the box's slot.</param>
/// <param name="Company">The company.</param>
/// <param name="Title">The posting's title.</param>
/// <param name="Url">The posting's address.</param>
/// <param name="BandFlag">PASS, FLAG, SKIP or NONE.</param>
/// <param name="LiveAt">When it went live.</param>
/// <param name="LiveKnown">True when the board gave that instant.</param>
/// <param name="Budget">Went live in the last 21 days of a year not yet ended.</param>
/// <param name="PassedAt">When the tick passed it.</param>
/// <param name="CompanyKey">The company's registrable domain when the scan knows it, which the email leg looks the leader up on.</param>
public sealed record CaughtRole(long Key, string Company, string Title, string Url, string BandFlag, DateTimeOffset LiveAt, bool LiveKnown, bool Budget, DateTimeOffset PassedAt, string? CompanyKey = null);

/// <summary>
/// What the pounce hands the box for a role it passed, so the program runs for that one role within the hour instead of
/// at 06:00 the next day: a JSON file under nightly/pounce/ that the box's slot run reads, and one dispatcher item in
/// dispatch/ready/ that starts the program once for it. The item is a wrapper: it holds no browser and no lock, because a
/// wrapper that holds the browser holds the very program it starts; the program's own gates and run lock do the rest.
/// The switch POUNCE_RUN picks the mode: "off" hands off nothing, "live" starts a live run, anything else, unset
/// included, a dry run, so nothing new sends under his name until the setting says so.
/// </summary>
public static class PounceHandoff
{
    /// <summary>No hand-off is written.</summary>
    public const string Off = "off";

    /// <summary>The item starts a dry run: pages are read and worklists written, nothing is sent.</summary>
    public const string Dry = "dry";

    /// <summary>The item starts a live run behind every gate the 6 AM run has.</summary>
    public const string Live = "live";

    /// <summary>
    /// Where the item waits for 07:00 Central: the box's run of feed, recount, invites and apply can take 40, 8, 40 and 40
    /// minutes and five more for its records, so one started after about 03:17 could still hold the run lock at 05:30,
    /// and the box refuses it. An item passed from 03:15 on carries Not_before 07:00, so the dispatcher holds it and no
    /// wrapper sleeps through the morning. The box's SlotTests read this line and hold it to the box's own cutoff.
    /// </summary>
    public static readonly (TimeSpan From, TimeSpan To) Morning = (new(3, 15, 0), new(7, 0, 0));

    /// <summary>The share folder the hand-offs go to.</summary>
    public const string Folder = "nightly/pounce";

    /// <summary>The dispatcher's ready folder on the share.</summary>
    public const string Ready = "dispatch/ready";

    /// <summary>The mode a POUNCE_RUN value names: off, live, or dry for anything else.</summary>
    public static string Mode(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        Off => Off,
        Live => Live,
        _ => Dry,
    };

    /// <summary>The hand-off's path on the share: the instant it passed, UTC, then the posting's key.</summary>
    public static string Path(CaughtRole role) => $"{Folder}/{Stamp(role)}-{role.Key}.json";

    /// <summary>The dispatcher item's path; 10 puts it ahead of the other items waiting in ready/.</summary>
    public static string ReadyPath(CaughtRole role) => $"{Ready}/10-pounce-{role.Key}.md";

    /// <summary>
    /// The hand-off's text, the contract the box's RoleHandoff reads. It goes to a file and to no page, so the relaxed
    /// escaping keeps a title's dash and an instant's plus sign as they are.
    /// </summary>
    public static string Json(CaughtRole role)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteNumber("key", role.Key);
            json.WriteString("company", role.Company);
            json.WriteString("title", role.Title);
            json.WriteString("url", role.Url);
            json.WriteString("band", role.BandFlag);
            json.WriteString("live_at", Iso(role.LiveAt));
            json.WriteBoolean("live_known", role.LiveKnown);
            json.WriteBoolean("budget", role.Budget);
            json.WriteString("passed_at", Iso(role.PassedAt));
            if (role.CompanyKey is { Length: > 0 } domain)
            {
                json.WriteString("company_key", domain);
            }
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// The dispatcher item: a header the dispatcher reads, then the steps a wrapper lane follows. It waits out the
    /// morning's window and a running 6 AM program, starts the program once through systemd-run, polls, and writes its
    /// DONE note and CLOSED row. The header's lane, entered and approved lines go into registry rows, so none names the
    /// browser or the network.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="mode">Dry or live.</param>
    public static string Item(CaughtRole role, string mode)
    {
        string lane = $"pounce-{role.Key}";
        string handoff = Path(role);
        string done = $@"J:\nightly\pounce\{Stamp(role)}-{role.Key}-DONE.md";
        bool live = mode == Live;
        var item = new StringBuilder();
        item.Append($"Lane: {lane}\nModel: claude-sonnet-5-5\nEffort: low\nHolds:\nNeeds: chrome, jdrive\nStall_minutes: 20\nMax_hours: 2\nScope: jobhunt\n");
        item.Append($"Entered_by: the Function's pounce tick, which passed posting {role.Key} at {Central(role.PassedAt)} Central\n");
        item.Append($"Approved_by: the pounce plan, step 4 ({(live ? "live, POUNCE_RUN is live" : "dry run, POUNCE_RUN is not live")})\n");
        item.Append($"Done_note: {done}\n");
        DateTime passed = CentralClock.Local(role.PassedAt);
        if (passed.TimeOfDay >= Morning.From && passed.TimeOfDay < Morning.To)
        {
            item.Append(CultureInfo.InvariantCulture, $"Not_before: {passed:yyyy-MM-dd}T07:00 CST\n");
        }
        item.Append('\n');
        item.Append($"# {lane}: run the program once for one caught role\n\n");
        item.Append("> RULES FIRST: read systemRules.md before acting on this file (MASTER RULE: if stuck or a tech glitch happens, retry, then reread the .md rules).\n\n");
        item.Append("## The role\n\n");
        item.Append($"{role.Company}, {role.Title}, {role.Url}. Live {Central(role.LiveAt)} Central{(role.LiveKnown ? string.Empty : " (first seen)")}, passed the pounce gate {Central(role.PassedAt)}. Band {role.BandFlag}.");
        item.Append(role.Budget ? " It carries the budget flag: its note may ask about the timeline in place of the no-oriented question.\n\n" : "\n\n");
        item.Append("## What you are\n\n");
        item.Append("A wrapper. You open no browser, send nothing and write no message. You edit no file but your DONE note and your CLOSED row. The program does the work behind its own gates: the ceilings and the pause at 90, Rule 25, FEED-HOLD, the closed-req gate, check_aiisms.py, verified sends and the run lock.");
        item.Append(live ? "\n\n" : " This is a DRY RUN: the program reads pages and writes its worklists and sends nothing.\n\n");
        item.Append("## Steps (on this box J:\\ is /mnt/j)\n\n");
        item.Append($"1. Run `date`. From {Morning.From:hh\\:mm} to 07:00 Central the program refuses a slot run, because one started then could still hold the run lock when the 6 AM program needs it: until 07:00, wait with one `sleep 240` call at a time.\n");
        item.Append("2. Run `systemctl --user is-active jobhunt-6am.service`. While it prints active or activating, wait with one `sleep 240` call at a time, for at most 60 minutes; if it still runs then, write the DONE note saying so and stop.\n");
        item.Append("3. Start the program once, and only once:\n\n```\n");
        item.Append($"systemd-run --user --unit={lane} --collect -p WorkingDirectory=$HOME/.local/share/jobhunt-server -E DISPLAY=:0 -E XAUTHORITY=$HOME/.Xauthority -E TZ=America/Chicago -E DOTNET_ROOT=/usr/lib/dotnet -E DOTNET_CLI_TELEMETRY_OPTOUT=1 -E PATH=$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin $HOME/.local/share/jobhunt-server/JobHunt.Server run --slot {lane} --role {handoff}{(live ? string.Empty : " --dry-run")}\n");
        item.Append("```\n\n   Read `date` right after; that is the start time. If systemd-run is refused, write the DONE note with its words and stop: never start the program another way.\n");
        item.Append($"4. Poll: one `sleep 240` call, then `systemctl --user is-active {lane}.service`, for at most 90 minutes. Never start it a second time, whatever it prints.\n");
        item.Append($"5. When it has ended, read `journalctl --user -u {lane}.service --no-pager | tail -60` and the card /mnt/j/nightly/<the Central day it started>/{lane}-card.md.\n");
        item.Append($"6. Write the DONE note at {done}, the RULES FIRST line on top: the start and end times, the card word for word, the minutes from the role going live ({Iso(role.LiveAt)}) to the program's end, and a `## LEFT FOR STEVE` section holding `- none`, or the one stop condition the card names.\n");
        item.Append($"7. Write your CLOSED row through the state.md spool: a new file /mnt/j/inbox/append-inbox/state.md/<UTC yyyyMMdd-HHmmss>Z-{lane}-close.md holding one line that begins `## LANE: {lane} CLOSED <HH:MM> CST`, says `Holds: none` and the DONE note's path, and ends ` | CLOSED |`. Name no browser, no network and no request kind in it. Then stop.\n");
        return item.ToString();
    }

    /// <summary>The instant the role passed, UTC, as the hand-off's file and every file beside it begin.</summary>
    public static string Stamp(CaughtRole role) => role.PassedAt.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "Z";

    private static string Iso(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>An instant on the Central clock, to the minute, as the items print it.</summary>
    public static string Central(DateTimeOffset at) => CentralClock.Local(at).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
