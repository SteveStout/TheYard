using System.Net;
using System.Text;
using System.Text.Json;
using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// GitHub's registry on the registry card (ADR-009, the addendum on GitHub's registry): each image read the way an
/// anonymous docker pull reads it, a pull token and then the tag list, with no credential sent; a public package
/// lists its tags and the highest v-numbered one, a private one is said to be refused, never shown as empty.
/// </summary>
public sealed class GhcrPackagesTests
{
    /// <summary>ghcr.io as recorded: theyard public with three tags, theyard-render refusing an anonymous list.</summary>
    private sealed class RecordedGhcr : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.ToString();
            Asked.Add($"{url} auth={request.Headers.Authorization?.Scheme ?? "none"}");
            HttpResponseMessage Json(HttpStatusCode code, string body) => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            return Task.FromResult(url switch
            {
                _ when url.StartsWith("https://ghcr.io/token?scope=repository:stevestout/theyard:pull", StringComparison.Ordinal) => Json(HttpStatusCode.OK, """{"token":"anon"}"""),
                _ when url.StartsWith("https://ghcr.io/v2/stevestout/theyard/tags/list", StringComparison.Ordinal) => Json(HttpStatusCode.OK, """{"name":"stevestout/theyard","tags":["v1.0.4.77","v1.0.4.100","v1.0.4.9"]}"""),
                _ when url.StartsWith("https://ghcr.io/token?scope=repository:stevestout/theyard-render:pull", StringComparison.Ordinal) => Json(HttpStatusCode.OK, """{"token":"anon"}"""),
                _ => Json(HttpStatusCode.Unauthorized, """{"errors":[{"code":"UNAUTHORIZED"}]}"""),
            });
        }
    }

    [Fact]
    public async Task A_public_image_lists_its_tags_and_a_private_one_is_refused_and_no_credential_is_sent()
    {
        var ghcr = new RecordedGhcr();
        using var route = OutboundHttp.RouteTo(ghcr);

        var read = await GhcrPackages.ReadAsync("stevestout", GhcrPackages.Images, CancellationToken.None);

        Assert.Equal([new GhcrImage("theyard", true, 3, "v1.0.4.100"), new GhcrImage("theyard-render", false, 0, null)], read);
        Assert.Equal("theyard v1.0.4.100, 3 tags · theyard-render refused to an anonymous pull: private, or never pushed", GhcrPackages.Words(read));
        // The only credential is the anonymous pull token the registry itself handed out.
        Assert.All(ghcr.Asked.Where(asked => asked.Contains("/token?", StringComparison.Ordinal)), asked => Assert.EndsWith("auth=none", asked, StringComparison.Ordinal));
        Assert.All(ghcr.Asked.Where(asked => asked.Contains("/tags/list", StringComparison.Ordinal)), asked => Assert.EndsWith("auth=Bearer", asked, StringComparison.Ordinal));
    }

    [Fact]
    public void The_newest_tag_is_the_highest_version_not_the_last_listed_and_an_unnumbered_tag_is_counted_only()
    {
        using var answer = JsonDocument.Parse("""{"tags":["latest","v1.0.4.99","sha-abc","v1.0.4.100"]}""");
        Assert.Equal(new GhcrImage("theyard", true, 4, "v1.0.4.100"), GhcrPackages.Image("theyard", answer.RootElement));
        using var none = JsonDocument.Parse("""{"tags":null}""");
        Assert.Equal(new GhcrImage("theyard", true, 0, null), GhcrPackages.Image("theyard", none.RootElement));
        Assert.Equal("not read", GhcrPackages.Words([]));
    }

    [Fact]
    public void The_registry_card_keeps_the_words_with_its_hour()
    {
        using var record = JsonDocument.Parse("""{"sku":{"name":"Basic"},"location":"westus3","properties":{"loginServer":"registry.example"}}""");
        var facts = AcrCard.Facts(record.RootElement, null, ghcr: "theyard v1.0.4.83, 7 tags");
        Assert.Equal("theyard v1.0.4.83, 7 tags", facts[GhcrPackages.Fact]);
        Assert.Null(AcrCard.Facts(record.RootElement, null)[GhcrPackages.Fact]);
    }
}
