// What GitHub's registry holds of the site's images (ADR-009, the addendum on GitHub's registry): each image's tags,
// read the way an anonymous docker pull reads them, a pull token for the repository and then its tag list. A public
// package answers both; a private one, or one never pushed, refuses the list, and the card says which and why. No
// credential is sent or held, so this can read nothing a stranger could not.
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TheYard.Api.Features.Admin.Cloud;

// #region ghcr-packages
/// <summary>One image on ghcr.io as an anonymous pull sees it.</summary>
/// <param name="Name">The image's name under the owner.</param>
/// <param name="Readable">True when an anonymous pull may list it, which is a public package.</param>
/// <param name="Tags">How many tags it carries; 0 when it cannot be listed.</param>
/// <param name="Newest">The highest v-numbered tag, or null.</param>
public sealed record GhcrImage(string Name, bool Readable, int Tags, string? Newest);

/// <summary>Reads GitHub's registry anonymously.</summary>
public static class GhcrPackages
{
    /// <summary>The registry's host.</summary>
    public const string Host = "ghcr.io";

    /// <summary>The images the deploy workflows push, deploy.yml's and deploy-render.yml's.</summary>
    public static readonly IReadOnlyList<string> Images = ["theyard", "theyard-render"];

    /// <summary>The fact the registry card keeps them under.</summary>
    public const string Fact = "ghcr";

    private static Outbound Http => OutboundHttp.Client(TimeSpan.FromSeconds(15));

    /// <summary>Each image's tags, or that it could not be listed.</summary>
    /// <param name="owner">The package owner, lower case, as the image's path names it.</param>
    /// <param name="images">The images under it.</param>
    /// <param name="cancellation">The read's cancellation.</param>
    public static async Task<IReadOnlyList<GhcrImage>> ReadAsync(string owner, IReadOnlyList<string> images, CancellationToken cancellation)
    {
        var read = new List<GhcrImage>();
        foreach (string image in images)
        {
            try
            {
                using var tokenAnswer = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"https://{Host}/token?scope=repository:{owner}/{image}:pull"), cancellation);
                string? token = null;
                if (tokenAnswer.IsSuccessStatusCode)
                {
                    using var granted = JsonDocument.Parse(await tokenAnswer.Content.ReadAsStringAsync(cancellation));
                    token = granted.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;
                }
                using var list = new HttpRequestMessage(HttpMethod.Get, $"https://{Host}/v2/{owner}/{image}/tags/list?n=1000");
                if (token is not null)
                {
                    list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
                using var answer = await Http.SendAsync(list, cancellation);
                if (!answer.IsSuccessStatusCode)
                {
                    read.Add(new GhcrImage(image, false, 0, null));
                    continue;
                }
                using var tags = JsonDocument.Parse(await answer.Content.ReadAsStringAsync(cancellation));
                read.Add(Image(image, tags.RootElement));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellation.IsCancellationRequested)
            {
                read.Add(new GhcrImage(image, false, 0, null));
            }
        }
        return read;
    }

    /// <summary>An image from its tag list: how many tags, and the highest v-numbered one.</summary>
    /// <param name="name">The image's name.</param>
    /// <param name="answer">The registry's /v2/{owner}/{name}/tags/list answer.</param>
    public static GhcrImage Image(string name, JsonElement answer)
    {
        var tags = answer.TryGetProperty("tags", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(tag => tag.GetString() ?? "").Where(tag => tag.Length > 0).ToList()
            : [];
        string? newest = tags
            .Where(tag => tag.StartsWith('v') && Version.TryParse(tag[1..], out _))
            .OrderByDescending(tag => Version.Parse(tag[1..]))
            .FirstOrDefault();
        return new GhcrImage(name, true, tags.Count, newest);
    }

    /// <summary>The facts the registry card keeps with its hour: one line an image.</summary>
    /// <param name="images">The images as read.</param>
    public static string Words(IReadOnlyList<GhcrImage> images) => images.Count == 0
        ? "not read"
        : string.Join(" · ", images.Select(image => image.Readable
            ? $"{image.Name} {image.Newest ?? "no v tag"}, {image.Tags.ToString(CultureInfo.InvariantCulture)} tag{(image.Tags == 1 ? "" : "s")}"
            : $"{image.Name} refused to an anonymous pull: private, or never pushed"));
}
// #endregion ghcr-packages
