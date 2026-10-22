using System.Net;
using TheYard.Api;
using TheYard.Api.Features.Admin.Cloud;
using TheYard.Application;

namespace TheYard.Tests;

/// <summary>
/// The catalogue selects by the configured names, held by what the cards ask Azure for rather than
/// by the text of the catalogue (ADR: The onion cut into vertical slices, the addendum of 1.0.4.14):
/// a group with two resources of every type, the configured one second in the alphabet, and every
/// card's minutes and hour asked against it.
/// </summary>
public partial class CloudViewTests
{
    private const string Group = "/subscriptions/sub-1/resourceGroups/RG-TEST";

    /// <summary>The names the cards are configured with: each the second of its type in the group.</summary>
    private static readonly AzureResources Names = new("RG-TEST", "app-test", "sqldb-test", "cosmos-test", "crtest", "log-test", "acs-test", "edge-test", new Uri("https://yard.example"), new DateOnly(2026, 10, 30));

    /// <summary>The group's list, the instance metadata token, two component records, and an empty answer to everything else; every address asked is kept.</summary>
    private sealed class TwoOfEach : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        private static string Entry(string type, string name) => $$"""{"id":"{{Group}}/providers/{{type}}/{{name}}","name":"{{name}}","type":"{{type}}"}""";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string address = Uri.UnescapeDataString(request.RequestUri!.ToString());
            lock (Asked)
            {
                Asked.Add(address);
            }
            string path = request.RequestUri.AbsolutePath;
            string body = request.RequestUri.Host == "169.254.169.254" ? """{"access_token":"e30.e30.sig"}"""
                : path.EndsWith("/RG-TEST/resources", StringComparison.Ordinal) ? "{\"value\":[" + string.Join(',',
                    Entry(ResourceCatalog.SqlType, "sql-srv/sqldb-aaa"), Entry(ResourceCatalog.SqlType, "sql-srv/sqldb-test"),
                    Entry(ResourceCatalog.CosmosType, "cosmos-aaa"), Entry(ResourceCatalog.CosmosType, "cosmos-test"),
                    Entry(ResourceCatalog.AcrType, "craaa"), Entry(ResourceCatalog.AcrType, "crtest"),
                    Entry(ResourceCatalog.InsightsType, "appi-aaa"), Entry(ResourceCatalog.InsightsType, "appi-test"),
                    Entry(ResourceCatalog.WorkspaceType, "log-aaa"), Entry(ResourceCatalog.WorkspaceType, "log-test"),
                    Entry(ResourceCatalog.MailType, "acs-aaa"), Entry(ResourceCatalog.MailType, "acs-test")) + "]}"
                : path.EndsWith("/appi-aaa", StringComparison.Ordinal) ? $$$"""{"properties":{"WorkspaceResourceId":"{{{Group}}}/providers/{{{ResourceCatalog.WorkspaceType}}}/log-aaa"}}"""
                : path.EndsWith("/appi-test", StringComparison.Ordinal) ? $$$"""{"properties":{"WorkspaceResourceId":"{{{Group}}}/providers/{{{ResourceCatalog.WorkspaceType}}}/log-test"}}"""
                : """{"value":[]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task Every_card_reads_the_configured_one_of_two_resources_of_its_type()
    {
        var handler = new TwoOfEach();
        using var route = OutboundHttp.RouteTo(handler);
        var azure = new AzureMetrics("client-1", configured: true);
        var directory = new ResourceDirectory(azure, Group, TimeProvider.System);
        var cards = ResourceCatalog.Cards(azure, directory, new NetlifyEdge(null, "edge-test", new Uri("https://yard.example")), (3, "m1"), Names);
        foreach (var card in cards.Where(card => card.Key != "edge"))
        {
            // What a card makes of an empty answer is not this test's question; which resource it asked for is.
            try
            {
                await card.Minutes(Now.AddMinutes(-5), Now, "test", CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
            }
            try
            {
                await card.Hour(Now, Now.AddHours(-1), CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
            }
        }
        var asked = handler.Asked.Where(address => address.Contains("/providers/", StringComparison.Ordinal)).ToList();
        foreach (string name in new[] { "sqldb-test", "cosmos-test", "crtest", "appi-test", "log-test", "acs-test" })
        {
            Assert.Contains(asked, address => address.Contains("/" + name, StringComparison.Ordinal));
        }
        foreach (string decoy in new[] { "sqldb-aaa", "cosmos-aaa", "craaa", "log-aaa", "acs-aaa" })
        {
            Assert.DoesNotContain(asked, address => address.Contains("/" + decoy, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_configured_name_the_group_does_not_hold_is_the_card_s_note_not_an_exception()
    {
        using var route = OutboundHttp.RouteTo(new TwoOfEach());
        var azure = new AzureMetrics("client-1", configured: true);
        var directory = new ResourceDirectory(azure, Group, TimeProvider.System);
        var cards = ResourceCatalog.Cards(azure, directory, new NetlifyEdge(null, "edge-test", new Uri("https://yard.example")), (3, "m1"), Names with { Mail = "acs-gone", Insights = "log-gone" });
        foreach (string key in new[] { "mail", "insights" })
        {
            var card = cards.Single(c => c.Key == key);
            Assert.Empty(await card.Minutes(Now.AddMinutes(-5), Now, "test", CancellationToken.None));
            var hour = await card.Hour(Now, Now.AddHours(-1), CancellationToken.None);
            var inputs = new ResourceCardInputs("24h", TimeSpan.FromDays(1), Now, new PlanHistoryAvailability(true, "kept in the test"), [], [], [], [hour], null);
            Assert.StartsWith("no resource named ", card.View(inputs).Note, StringComparison.Ordinal);
        }
    }
}
