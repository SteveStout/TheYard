using System.Globalization;
using System.Text;

namespace JobHunt.Domain.Features.Jobs;

/// <summary>
/// The dispatcher item for one follow-up the pass found due. What it may do follows POUNCE_RUN and the channel: a dry run
/// opens no browser and writes the follow-up and its minutes in the DONE note; a live run on LinkedIn sends it only to a
/// leader who accepted the request and has not written back, under the standing permission for follow-ups to accepted
/// connections; an email follow-up stays in shadow whatever the switch says, because the email ruling is unanswered. A
/// leader who opened his link and has not accepted gets nothing until he does.
/// </summary>
public static class FollowUpItem
{
    /// <summary>The item's lane, which names its dispatcher folder.</summary>
    /// <param name="followUp">The follow-up.</param>
    public static string Lane(FollowUp followUp) => $"pouncefollow-{followUp.Code.Code}";

    /// <summary>The item's path; 15 puts it after the wrappers that start first touches and before the pages.</summary>
    /// <param name="followUp">The follow-up.</param>
    public static string ReadyPath(FollowUp followUp) => $"{PounceHandoff.Ready}/15-{Lane(followUp)}.md";

    /// <summary>True when the item may send: the switch says live and the first touch was on LinkedIn.</summary>
    /// <param name="followUp">The follow-up.</param>
    /// <param name="mode">Dry or live.</param>
    public static bool Sends(FollowUp followUp, string mode) =>
        mode == PounceHandoff.Live && string.Equals(followUp.Code.Channel, "LinkedIn", StringComparison.OrdinalIgnoreCase);

    /// <summary>The item: a header the dispatcher reads, then the steps a Sonnet lane follows.</summary>
    /// <param name="followUp">The follow-up.</param>
    /// <param name="mode">Dry or live.</param>
    /// <param name="at">When the pass found it due.</param>
    public static string Item(FollowUp followUp, string mode, DateTimeOffset at)
    {
        PounceCode code = followUp.Code;
        string lane = Lane(followUp);
        string stamp = at.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "Z";
        string done = $@"J:\nightly\pounce\followups\{stamp}-{code.Code}-DONE.md";
        string first = followUp.Visit.FirstOpen.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        bool sends = Sends(followUp, mode);
        bool email = !string.Equals(code.Channel, "LinkedIn", StringComparison.OrdinalIgnoreCase);
        var item = new StringBuilder();
        item.Append($"Lane: {lane}\nModel: claude-sonnet-5-5\nEffort: low\nHolds:{(sends ? " chrome, linkedin" : string.Empty)}\nNeeds: {(sends ? "chrome, jdrive" : "jdrive")}\nStall_minutes: 20\nMax_hours: 1\nScope: jobhunt\n");
        item.Append($"Entered_by: the Function's follow-up pass, which read code {code.Code} at {PounceHandoff.Central(at)} Central\n");
        item.Append($"Approved_by: the pounce plan, the follow-up after an open ({Approval(sends, email)})\n");
        item.Append($"Done_note: {done}\n\n");
        item.Append($"# {lane}: one follow-up after a pounce note was opened\n\n");
        item.Append("> RULES FIRST: read systemRules.md before acting on this file (MASTER RULE: if stuck or a tech glitch happens, retry, then reread the .md rules).\n\n");
        item.Append("## Who and why\n\n");
        item.Append($"{code.Person}, {code.Company}, the caught role {code.Key} (its hand-off is /mnt/j/nightly/pounce/*-{code.Key}.json). The first touch went on {code.Channel} with code {code.Code} at {PounceHandoff.Central(code.Sent)} Central. ");
        item.Append($"The site says: {followUp.Visit.Opens} opens, first {PounceHandoff.Central(followUp.Visit.FirstOpen)} Central, last {PounceHandoff.Central(followUp.Visit.LastOpen)} Central, review stop {followUp.Visit.Stop}; rung {followUp.Rung} of the ladder.\n\n");
        item.Append("## The follow-up, word for word\n\n```\n");
        item.Append(followUp.Text);
        item.Append("\n```\n\n");
        item.Append("It is the only follow-up this person gets for this role from the pass. Never change a word, never add a link, and never link the Admin tab.\n\n");
        item.Append("## Steps (on this box J:\\ is /mnt/j)\n\n");
        if (!sends)
        {
            item.Append(email
                ? "This is SHADOW: the email leg waits on Steve's email ruling in J:\\dispatch\\DECISION.md. Open no browser, open no mailbox, send nothing and draft nothing.\n\n"
                : "This is a DRY RUN: POUNCE_RUN is not live. Open no browser, send nothing and draft nothing.\n\n");
            item.Append($"1. Run `date`. Write the DONE note at {done}, the RULES FIRST line on top: the follow-up word for word, its word count, the start time, and the whole minutes from the first open ({first}) to that time, on a line that begins `Minutes from open to dry follow-up:`.\n");
            item.Append("2. End the DONE note with `## LEFT FOR STEVE` and `- none`.\n");
        }
        else
        {
            item.Append("This is LIVE, on LinkedIn, inside Steve's standing permission of 22 October for follow-ups to accepted connections. One send at most.\n\n");
            item.Append($"1. Read his profile address from the tracker's row for {code.Person} at {code.Company} that records the request with code {code.Code}. Open it in a new tab.\n");
            item.Append("2. Not accepted: if the profile does not show him as a 1st-degree connection, send nothing, write the DONE note saying he has not accepted, and stop. A leader who opened his link and has not accepted gets nothing until he does.\n");
            item.Append("3. Written back: open the message thread with him. If he has written anything to Steve, send nothing, write the DONE note saying so, and stop: a reply is answered by a draft for Steve, never from here.\n");
            item.Append("4. Type the follow-up in pieces of 40 to 135 characters, keeping its blank lines; if a screenshot fails, open a fresh tab and go on from the thread. Send it once. Read the thread back and confirm the text is there word for word.\n");
            item.Append($"5. Write one tracker row through the tracker spool: Person {code.Person}, Company {code.Company}, LinkedIn, today, the profile address, and Notes beginning `FOLLOW-UP MESSAGE SENT by Claude ({lane})` with the time, the code and the text.\n");
            item.Append($"6. Write the DONE note at {done}, the RULES FIRST line on top: the send time, the minutes from the first open ({first}) to it, the thread read-back, and `## LEFT FOR STEVE` with `- none`.\n");
        }
        item.Append($"Last, write your CLOSED row through the state.md spool: a new file /mnt/j/inbox/append-inbox/state.md/<UTC yyyyMMdd-HHmmss>Z-{lane}-close.md holding one line that begins `## LANE: {lane} CLOSED <HH:MM> CST`, says `Holds: {(sends ? "chrome, linkedin" : "none")}` and the DONE note's path, and ends ` | CLOSED |`. Then stop.\n");
        return item.ToString();
    }

    private static string Approval(bool sends, bool email) =>
        sends ? "live, POUNCE_RUN is live, inside the standing permission of 22 October for follow-ups to accepted connections"
        : email ? "shadow, the email ruling is unanswered"
        : "dry run, POUNCE_RUN is not live";
}
