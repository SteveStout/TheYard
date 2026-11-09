using System.Text;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// What the campaign and the network log share since the network log arrived: the former employers, the company key
/// and the personal domains under Shared/Companies, the tells and the stop phrases under Shared/Writing, the MIME
/// parts under Shared/Google and the command builder under Shared/Sql. Each moved without a change of behaviour; these
/// hold the campaign's answers to what they were, and the network log's slice to using them only from Shared.
/// </summary>
public sealed class NetworkSharedTests
{
    [Fact]
    public void The_campaign_s_message_is_the_same_bytes_through_the_shared_parts()
    {
        var mail = new OutgoingMail("pat@acme.com", "Rémote .NET", "Hi\nthere", "<div>Hi</div>");
        string mime = GmailSender.Mime(mail, "b1");
        string expected = "To: pat@acme.com\r\nSubject: =?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes("Rémote .NET")) + "?=\r\n"
            + "MIME-Version: 1.0\r\nContent-Type: multipart/alternative; boundary=\"b1\"\r\n\r\n"
            + "--b1\r\nContent-Type: text/plain; charset=\"UTF-8\"\r\nContent-Transfer-Encoding: base64\r\n\r\n" + Convert.ToBase64String(Encoding.UTF8.GetBytes("Hi\r\nthere")) + "\r\n"
            + "--b1\r\nContent-Type: text/html; charset=\"UTF-8\"\r\nContent-Transfer-Encoding: base64\r\n\r\n" + Convert.ToBase64String(Encoding.UTF8.GetBytes("<div>Hi</div>")) + "\r\n"
            + "--b1--\r\n";
        Assert.Equal(expected, mime);
        Assert.Equal("YQ", MimeText.Base64Url([0x61]));
        Assert.Equal("-_8", MimeText.Base64Url([0xfb, 0xff]));
    }

    [Fact]
    public void A_long_body_is_cut_into_lines_of_76()
    {
        string part = MimeText.Part("text/plain", new string('x', 300));
        var lines = part.Split("\r\n").Skip(3).Where(l => l.Length > 0).ToList();
        Assert.All(lines.SkipLast(1), l => Assert.Equal(76, l.Length));
        Assert.True(lines[^1].Length <= 76);
    }

    [Fact]
    public void The_personal_domains_are_one_list_for_both_features()
    {
        Assert.Same(PersonalDomains.All, RecipientDomainRule.Personal);
        Assert.Contains("gmail.com", PersonalDomains.All);
        Assert.Contains("icloud.com", PersonalDomains.All);
        Assert.DoesNotContain("acme.com", PersonalDomains.All);
        Assert.Equal(CampaignVerdict.Hold("personal domain (gmail.com)"), RecipientDomainRule.Check("pat@gmail.com", true));
    }

    [Fact]
    public void The_stop_phrases_and_the_tells_are_read_from_Shared_by_both_features()
    {
        Assert.Equal("not interested", StopPhrases.Find("Not interested, thanks"));
        Assert.Null(StopPhrases.Find("Sounds great\nOn Tue, Pat wrote:\n> not interested"));
        Assert.Contains("leverage", Tells.Find("we can leverage it"));
        Assert.Equal("JobHunt.Domain.Shared.Writing", typeof(StopPhrases).Namespace);
        Assert.Equal("JobHunt.Domain.Shared.Writing", typeof(Tells).Namespace);
        Assert.Equal("JobHunt.Domain.Shared.Companies", typeof(FormerEmployers).Namespace);
        Assert.Equal("JobHunt.Domain.Shared.Companies", typeof(CompanyKey).Namespace);
    }

    [Fact]
    public void The_network_log_names_no_campaign_type()
    {
        string root = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(root, "functions", "JobHunt.Domain")))
        {
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("functions/ not found");
        }
        var files = Directory.EnumerateFiles(Path.Combine(root, "functions"), "*.cs", SearchOption.AllDirectories)
            .Where(p => p.Contains($"{Path.DirectorySeparatorChar}NetworkLog{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.True(files.Count >= 14, $"{files.Count} files under NetworkLog");
        foreach (string path in files)
        {
            string code = File.ReadAllText(path);
            Assert.DoesNotContain("Features.Campaign", code, StringComparison.Ordinal);
            Assert.DoesNotContain("CampaignRules", code, StringComparison.Ordinal);
            Assert.DoesNotContain("SqlCampaignStore", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_day_goes_to_SQL_as_a_date_through_the_shared_builder()
    {
        using var command = SqlCommands.Build(null, "SELECT 1", ("@day", new DateOnly(2026, 11, 9)), ("@none", null));
        Assert.Equal(System.Data.SqlDbType.Date, command.Parameters["@day"].SqlDbType);
        Assert.Equal(DBNull.Value, command.Parameters["@none"].Value);
        Assert.Equal(30, command.CommandTimeout);
    }
}
