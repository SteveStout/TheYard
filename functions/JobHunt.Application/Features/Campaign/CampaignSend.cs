using System.Globalization;

namespace JobHunt.Application.Features.Campaign;

/// <summary>The campaign's switch, EMAIL_CAMPAIGN: absent or anything but these two is off.</summary>
public enum CampaignMode
{
    /// <summary>Nothing runs.</summary>
    Off,

    /// <summary>Every rule runs and the would-sends are written with Shadow = 1; Gmail is never called.</summary>
    Shadow,

    /// <summary>The emails go out from Steve's Gmail, the cap and the ramp holding the count.</summary>
    On,
}

/// <summary>One email for the mail port to send.</summary>
public sealed record OutgoingMail(string To, string Subject, string Text, string Html);

/// <summary>What Gmail answered for a sent email: its ids, and the Message-Id a delivery failure quotes back.</summary>
public sealed record SentMail(string MessageId, string ThreadId, string? RfcMessageId);

/// <summary>Sends one email from Steve's Gmail. Only the campaign's `on` path reaches it; a test holds shadow away from it.</summary>
public interface IMailSender
{
    /// <summary>Sends the message and answers its ids.</summary>
    Task<SentMail> SendAsync(OutgoingMail mail, CancellationToken ct);
}

/// <summary>A pass's outcome: what it came to, its counts and its line for campaign/pass.log.</summary>
public sealed record CampaignPassResult(string Came, int Open, int Eligible, int Written, IReadOnlyDictionary<string, int> Held, string Line);

/// <summary>
/// The send (ADR-118): every open lead through the rules in the review's order, the ones that pass ranked, and
/// as many as today's cap allows written (shadow) or sent (on). The order: the former employer, do-not-email,
/// an address with its public source, MX found, a business domain, the do-not-apply list and the 30 days, a
/// live thread, Track A within seven days of the application, no blank, WhatTheyDo, no tell in the rendered
/// email; then the cap and the ramp, the LinkedIn order and Strength. Idempotent by the log: a second firing
/// on the same Central day finds the day's count already at the cap. A held lead's reason is counted; a lead
/// whose own words fail (a blank, WhatTheyDo, a tell) is marked Blocked for the next lead run.
/// </summary>
public sealed class CampaignSend(ILeadStore leads, IEmailLog log, IMailSender mail, IMergeStore store, TimeProvider time)
{
    /// <summary>One line a pass on the share.</summary>
    public const string PassLog = "campaign/pass.log";

    /// <summary>The wait between two real sends; the tests set it to nothing.</summary>
    public TimeSpan Spacing { get; init; } = CampaignRules.Spacing;

    /// <summary>Runs one pass in a mode.</summary>
    public async Task<CampaignPassResult> RunAsync(CampaignMode mode, CancellationToken ct)
    {
        var empty = new Dictionary<string, int>(StringComparer.Ordinal);
        if (mode == CampaignMode.Off)
        {
            return new CampaignPassResult("unchanged", 0, 0, 0, empty, "off");
        }
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(CentralClock.Local(now));
        bool shadow = mode == CampaignMode.Shadow;
        var logged = (await log.SinceAsync(today.AddDays(-(ThirtyDayRule.WindowDays + 1)), ct)).Where(e => e.Shadow == shadow).ToList();
        int already = logged.Count(e => e.CentralDay == today);
        int? dayOfOn = shadow ? null : (await log.SinceAsync(DateOnly.MinValue, ct)).Where(e => !e.Shadow && e.CentralDay < today).Select(e => e.CentralDay).Distinct().Count() + 1;
        int left = CampaignRules.Left(already, dayOfOn);
        var open = await leads.OpenAsync(ct);
        if (left == 0)
        {
            return new CampaignPassResult("unchanged", open.Count, 0, 0, empty, $"{Word(mode)}: {already} already today, cap {CampaignRules.CapFor(dayOfOn)}");
        }

        var master = TrackerCsvText.Parse(TrackerCsvText.Decode(await store.ReadAsync(MergePaths.Master, ct)))
            .Where(row => row.Length >= 7 && int.TryParse(row[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _)).ToList();
        string rules = await store.ExistsAsync("systemRules.md", ct) ? TrackerCsvText.Decode(await store.ReadAsync("systemRules.md", ct)) : string.Empty;
        var doNotApply = ApplicationCap.DoNotApplyList(rules);
        var sends = logged.Select(e => new PastSend(e.Key, e.ParentKey, e.Company, e.CentralDay)).ToList();

        var held = new Dictionary<string, int>(StringComparer.Ordinal);
        var ready = new List<(LeadRecord Lead, RenderedEmail Email, RankedLead Rank)>();
        foreach (var lead in open)
        {
            var (verdict, email, block) = Check(lead, sends, doNotApply, master, today, now);
            if (!verdict.Allowed)
            {
                string reason = verdict.Reason.Split(':')[0];
                held[reason] = held.GetValueOrDefault(reason) + 1;
                if (block)
                {
                    await leads.BlockAsync(lead.Id, verdict.Reason, ct);
                }
                continue;
            }
            int strength = Strength.Score(Inputs(lead), today, lead.StrengthStep);
            int linkedIn = LinkedInOrder.Rank(lead.LinkedInAcceptedAt, lead.LinkedInInviteSentAt, now);
            ready.Add((lead, email!, new RankedLead(lead.Id, linkedIn, strength, lead.CreatedAt)));
        }

        int written = 0;
        var chosenCompanies = new List<PastSend>();
        foreach (var rank in SendOrder.Sort(ready.Select(r => r.Rank)))
        {
            if (written >= left)
            {
                break;
            }
            var (lead, email, _) = ready.Single(r => r.Rank.LeadId == rank.LeadId);
            if (!ThirtyDayRule.Check(Company(lead), chosenCompanies, today).Allowed)
            {
                held["30-day rule"] = held.GetValueOrDefault("30-day rule") + 1;
                continue;
            }
            if (written > 0 && !shadow)
            {
                await Task.Delay(Spacing, time, ct);
            }
            SentMail? sent = shadow ? null : await mail.SendAsync(new OutgoingMail(lead.ContactEmail!, email.Subject!, email.Text!, email.Html!), ct);
            var template = CampaignTemplate.For(lead.Track);
            await log.WriteAsync(new EmailRecord
            {
                LeadId = lead.Id,
                Key = lead.Company.Key,
                ParentKey = lead.Company.ParentKey,
                Company = lead.CompanyName,
                To = lead.ContactEmail!,
                TemplateId = template.Id,
                TemplateVersion = template.Version,
                Subject = email.Subject!,
                BodyText = email.Text!,
                BodyHtml = email.Html!,
                SentAt = time.GetUtcNow(),
                CentralDay = today,
                Touch = 1,
                Shadow = shadow,
                RefCode = lead.RefCode,
                GmailMessageId = sent?.MessageId,
                GmailThreadId = sent?.ThreadId,
                RfcMessageId = sent?.RfcMessageId,
            }, ct);
            chosenCompanies.Add(new PastSend(lead.Company.Key, lead.Company.ParentKey, lead.CompanyName, today));
            written++;
        }

        string heldWords = held.Count == 0 ? "none" : string.Join(", ", held.OrderByDescending(h => h.Value).ThenBy(h => h.Key, StringComparer.Ordinal).Select(h => $"{h.Value} {h.Key}"));
        string line = $"{Word(mode)}: {open.Count} open, {ready.Count} eligible, {written} {(shadow ? "written" : "sent")} of {left} left today | held: {heldWords}";
        string came = written > 0 ? (shadow ? "planned" : "success") : "unchanged";
        return new CampaignPassResult(came, open.Count, ready.Count, written, held, line);
    }

    /// <summary>The rules for one lead in order: the verdict, the rendered email when it passes, and whether a hold marks the row.</summary>
    public static (CampaignVerdict Verdict, RenderedEmail? Email, bool Block) Check(
        LeadRecord lead, IReadOnlyList<PastSend> sends, IReadOnlyList<DoNotApply> doNotApply, IReadOnlyList<string[]> master, DateOnly today, DateTimeOffset now)
    {
        var company = Company(lead);
        var former = company.Names.Select(n => FormerEmployers.Of(n, null)).FirstOrDefault(e => e is not null)
            ?? FormerEmployers.Of(null, lead.ContactEmail ?? company.Key)
            ?? FormerEmployers.Of(null, company.Key);
        if (former is not null)
        {
            return (CampaignVerdict.Hold($"former employer: {former.Name}"), null, false);
        }
        if (lead.Company.DoNotEmail)
        {
            return (CampaignVerdict.Hold("do not email: the company asked"), null, false);
        }
        if (string.IsNullOrWhiteSpace(lead.ContactEmail))
        {
            return (CampaignVerdict.Hold("no address: no public source yet, finder none"), null, false);
        }
        if (string.IsNullOrWhiteSpace(lead.EmailSource) || !lead.EmailSource.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return (CampaignVerdict.Hold("no source: an address without its public page"), null, false);
        }
        if (lead.Verified == false)
        {
            return (CampaignVerdict.Hold("bounced: the address took no mail"), null, false);
        }
        var recipient = RecipientDomainRule.Check(lead.ContactEmail, lead.MxChecked);
        if (!recipient.Allowed)
        {
            return (recipient, null, false);
        }
        var gate = CompanyGate.Check(company, sends, doNotApply, today);
        if (!gate.Allowed)
        {
            return (gate, null, false);
        }
        var thread = LiveThreadRule.Check(company.Names, master, today);
        if (!thread.Allowed)
        {
            return (thread, null, false);
        }
        if (lead.Track == 'A')
        {
            var applied = AppliedWhen.Check(lead.AppliedAt, now);
            if (!applied.Allowed)
            {
                return (applied, null, false);
            }
        }
        var template = CampaignTemplate.For(lead.Track);
        var values = TemplateRender.Values(lead, now);
        var email = TemplateRender.Render(template, values);
        if (!email.Complete)
        {
            return (CampaignVerdict.Hold("blank: " + string.Join(", ", email.Blanks)), null, true);
        }
        if (WhatTheyDoRule.Check(lead.WhatTheyDo, company.Names) is { } what)
        {
            return (CampaignVerdict.Hold(what), null, true);
        }
        var tells = Tells.Find(email.Subject + "\n" + email.Text);
        if (tells.Count > 0)
        {
            return (CampaignVerdict.Hold("tell in the email: " + string.Join(", ", tells)), null, true);
        }
        // v1 of Template A says "so I applied today" in words: it goes out only when that is true (F8).
        if (lead.Track == 'A' && template.Text.Contains("applied today", StringComparison.Ordinal) && values["AppliedWhen"] != "today")
        {
            return (CampaignVerdict.Hold($"template A {template.Version} says applied today: applied {values["AppliedWhen"]}"), null, false);
        }
        return (CampaignVerdict.Pass, email, false);
    }

    /// <summary>The lead's company as the company rules key it, its email's own name among its names.</summary>
    public static CampaignCompany Company(LeadRecord lead) =>
        new(lead.Company.Key, lead.Company.ParentKey, [.. lead.Company.Names.Append(lead.CompanyName).Distinct(StringComparer.OrdinalIgnoreCase)]);

    /// <summary>The stored facts Strength reads.</summary>
    public static StrengthInputs Inputs(LeadRecord lead) => new(
        Enum.TryParse<RemoteStance>(lead.Company.RemoteStance, out var stance) ? stance : RemoteStance.Unknown,
        lead.Company.DotNetEvidence,
        lead.Track == 'A',
        lead.BandTop,
        lead.GhostScore,
        lead.FirstSeen,
        lead.Company.LastDotNetPostingAt);

    private static string Word(CampaignMode mode) => mode == CampaignMode.On ? "on" : "shadow";
}
