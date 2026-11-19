// The image registry's card (ADR: One frame for every resource), retired from Azure's registry by ADR-009's addendum
// on retiring it: GitHub's registry, where both images are pushed and every roll pulls from, read once an hour the way an
// anonymous docker pull reads it, beside the tag each web app runs from the plan card's own hour.
using System.Globalization;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The registry's card: GitHub's registry and what the web apps run from it.</summary>
public static class RegistryCard
{
    /// <summary>Where each row and readout line of this card is read, by row key or line label (CardFrame.Sourced).</summary>
    private static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["holds"] = Anonymous,
        ["public"] = Anonymous,
        ["same"] = "the plan card's hour",
        ["cost"] = "GitHub's registry: a public package is not billed",
        ["Images"] = Anonymous,
        ["Tags"] = Anonymous,
        ["Live tags"] = "the plan card's hour",
    };

    /// <summary>Where the registry is read from, as each source line says it.</summary>
    public const string Anonymous = "ghcr.io, read as an anonymous pull";

    /// <summary>The package owner the images live under on ghcr.io, as their path names it.</summary>
    public const string Owner = "stevestout";

    /// <summary>
    /// What each image is, in a sentence, by name: no record states it, so it is typed here and only here, read
    /// against deploy.yml (the API's image, serving both sites and The Shed from one web app) and deploy-render.yml
    /// (the renderer's). An image this map does not name shows "no description", as information.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["theyard"] = ".NET API serving TheYard, TheYard on Cosmos and The Shed from one web app",
        ["theyard-render"] = "Node.js server that renders the React pages before they reach the browser",
    };

    /// <summary>The two images of one build: the renderer draws the API's pages, so the row warns while their live tags differ.</summary>
    public static readonly (string Api, string Renderer) SameBuild = ("theyard", "theyard-render");

    /// <summary>The image table's columns.</summary>
    public static readonly IReadOnlyList<string> ImageColumns = ["Image", "What it is", "Runs on", "Live tag", "Newest tag", "Tags"];

    /// <summary>The hour's facts: each image as the anonymous read found it and which web apps run it at which tag.</summary>
    /// <param name="images">GitHub's registry as an anonymous pull read it.</param>
    /// <param name="plan">The plan card's newest kept hour, whose apps carry their image; null when none is kept.</param>
    public static IReadOnlyDictionary<string, string?> Facts(IReadOnlyList<GhcrImage> images, PlanHour? plan) =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [GhcrPackages.Fact] = GhcrPackages.Words(images),
            ["images"] = images.Count.ToString(CultureInfo.InvariantCulture),
            ["public"] = images.Count(image => image.Readable).ToString(CultureInfo.InvariantCulture),
            ["tags"] = images.Sum(image => image.Tags).ToString(CultureInfo.InvariantCulture),
            ["apps_read"] = plan is null ? null : plan.Apps.Count.ToString(CultureInfo.InvariantCulture),
            ["image_rows"] = CloudTable.ToFact(images.Select(image => new[]
            {
                image.Name,
                image.Readable ? "yes" : "no",
                image.Tags.ToString(CultureInfo.InvariantCulture),
                image.Newest ?? "",
                string.Join('\u001E', (plan?.Apps ?? Array.Empty<PlanAppState>()).Select(app => (app, named: ImageOf(app.Image)))
                    .Where(pair => pair.named is { } n && string.Equals(n.Host, GhcrPackages.Host, StringComparison.OrdinalIgnoreCase) && n.Repository == $"{Owner}/{image.Name}")
                    .Select(pair => $"{pair.app.Name}\u001F{PlanView.Portal(plan!.TenantId, pair.app.ResourceId)}\u001F{pair.named!.Value.Tag}")),
            })),
            ["spec"] = "GHCR.IO PUBLIC PACKAGES",
        };

    public static CloudReport View(ResourceCardInputs inputs)
    {
        var images = Images(inputs);
        int count = images.Count, open = images.Count(image => image.Public);
        int apps = images.Sum(image => image.Apps.Count), current = images.Sum(image => image.Apps.Count(app => app.Tag.Length > 0 && app.Tag == image.Newest));
        double? publicShare = count == 0 ? null : 100.0 * open / count;
        double? currentShare = apps == 0 ? null : 100.0 * current / apps;
        var dials = new[]
        {
            new CloudDial("Images an anonymous pull reads", publicShare, count == 0 ? "not read" : $"{open} of {count}",
                "a roll pulls with no credential, so an image GitHub holds private cannot roll · red below all", 100, 100, null, null,
                publicShare is not { } p ? "info" : p >= 100 ? "good" : "alarm"),
            new CloudDial("Web apps on the newest tag", currentShare, apps == 0 ? "not read" : $"{current} of {apps}",
                "each web app's image against the newest v tag on ghcr.io · information while a roll is under way", 100, 100, null, null,
                currentShare is not { } c ? "info" : c >= 100 ? "good" : "info"),
        };
        var rows = new List<CloudRow> { Holds(inputs, images), Public(images), Same(images), Cost() };
        var readout = new CloudReadout(
            "GitHub's registry",
            ResourceView.Uptime(inputs),
            $"read hourly, {CloudTones.WindowWords(inputs.Window)}",
            [
                new("Images", count == 0 ? "not read" : $"{count}, {open} public", "info"),
                new("Tags", ResourceView.Fact(inputs, "tags"), "info"),
                new("Live tags", apps == 0 ? "not read" : string.Join(" · ", images.SelectMany(image => image.Apps.Select(app => $"{app.Name} {app.Tag}"))), "info"),
                new("GitHub's registry", ResourceView.Fact(inputs, GhcrPackages.Fact, "not read yet"), "info") { Source = Anonymous },
                ResourceView.HourRead(inputs),
            ],
            "every image is pushed here by the deploy workflows and pulled from here by every roll; the Azure registry it replaced is retired");
        return CardFrame.Sourced(new CloudReport(
            inputs.Window, PlanView.Windows, inputs.Availability.Available && inputs.Hours.Count > 0,
            ResourceView.Note(inputs, ResourceView.FirstRead(inputs, "hour"), hoursOnly: true),
            inputs.Now.ToUnixTimeMilliseconds(), ResourceView.HourAge(inputs),
            "Container registry",
            ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("spec"),
            new CloudLink($"{GhcrPackages.Host}/{Owner}", $"https://github.com/{Owner}?tab=packages"),
            dials,
            [],
            new CloudChart("Nothing it reports moves inside an hour", [], [], 100, "the registry is read hourly and keeps no minute", (inputs.Now - inputs.Length).ToUnixTimeMilliseconds()),
            rows,
            readout), Sources);
    }

    /// <summary>One image as the hour's facts hold it.</summary>
    /// <param name="Name">The image's name.</param>
    /// <param name="Public">True when an anonymous pull could list it.</param>
    /// <param name="Tags">How many tags it holds.</param>
    /// <param name="Newest">Its highest v-numbered tag, empty when none.</param>
    /// <param name="Apps">Each web app that runs it, with its portal link and live tag.</param>
    public sealed record HeldImage(string Name, bool Public, string Tags, string Newest, IReadOnlyList<(string Name, string Portal, string Tag)> Apps);

    /// <summary>The images in the newest hour's facts, empty before the first read.</summary>
    /// <param name="inputs">The card's inputs.</param>
    public static IReadOnlyList<HeldImage> Images(ResourceCardInputs inputs) =>
        ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("image_rows") is not { } fact ? []
        : [.. fact.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('\t')).Where(cells => cells.Length >= 5)
            .Select(cells => new HeldImage(cells[0], cells[1] == "yes", cells[2], cells[3],
                [.. cells[4].Split('\u001E', StringSplitOptions.RemoveEmptyEntries).Select(app => app.Split('\u001F')).Where(app => app.Length == 3).Select(app => (app[0], app[1], app[2]))]))];

    /// <summary>The holds row: one table row an image, what it is, which web apps run it at which tag, and its newest tag.</summary>
    private static CloudRow Holds(ResourceCardInputs inputs, IReadOnlyList<HeldImage> images)
    {
        if (images.Count == 0)
        {
            return new CloudRow("holds", "What does it hold?", "not read yet", [], "info");
        }
        var notes = images.Where(image => !Descriptions.ContainsKey(image.Name))
            .Select(image => new CloudLink($"{image.Name} has no description: say what it is in RegistryCard.Descriptions", null)).ToList();
        if (ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("apps_read") is null)
        {
            notes.Add(new("which web app runs each image is read from the plan card's hour; none is kept yet", null));
        }
        var table = new CloudTable(ImageColumns, [.. images.Select(image => (IReadOnlyList<CloudLink>)
        [
            new(image.Name, $"https://github.com/{Owner}/TheYard/pkgs/container/{image.Name}"),
            new(Descriptions.GetValueOrDefault(image.Name, "no description"), null),
            image.Apps.Count == 0 ? new("no web app runs it", null) : new(string.Join(", ", image.Apps.Select(app => app.Name)), image.Apps[0].Portal.Length > 0 ? image.Apps[0].Portal : null),
            new(image.Apps.Count == 0 ? "none" : string.Join(", ", image.Apps.Select(app => app.Tag).Distinct(StringComparer.Ordinal)), null),
            new(image.Newest.Length > 0 ? image.Newest : "no v tag", null),
            new(image.Tags, null),
        ])]);
        string tags = ResourceView.Fact(inputs, "tags");
        return new CloudRow("holds", "What does it hold?", $"{images.Count} images, {tags} tags", notes, "info") { Table = table };
    }

    /// <summary>Whether every image is public, which a roll with no credential needs.</summary>
    private static CloudRow Public(IReadOnlyList<HeldImage> images)
    {
        var closed = images.Where(image => !image.Public).Select(image => image.Name).ToList();
        return images.Count == 0
            ? new CloudRow("public", "Can a roll pull them?", "not read yet", [], "info")
            : closed.Count == 0
            ? new CloudRow("public", "Can a roll pull them?", "yes, every image is public", [new("App Service pulls with no credential, as an anonymous pull does", null)], "good")
            : new CloudRow("public", "Can a roll pull them?", $"no: {string.Join(", ", closed)} refused an anonymous pull",
                [new("a package is made public in its settings on GitHub (Package settings, Change visibility); until then a roll of it fails", null)], "alarm");
    }

    /// <summary>Whether the API's image and the renderer's run the same build's tag.</summary>
    private static CloudRow Same(IReadOnlyList<HeldImage> images)
    {
        string Tag(string name) => images.FirstOrDefault(image => image.Name == name)?.Apps.FirstOrDefault().Tag ?? "";
        string api = Tag(SameBuild.Api), render = Tag(SameBuild.Renderer);
        return api.Length == 0 || render.Length == 0
            ? new CloudRow("same", "Do the two images run one build?", "not read yet", [], "info")
            : api == render
            ? new CloudRow("same", "Do the two images run one build?", $"yes, {api}", [new("the renderer draws the pages of the API's build", null)], "good")
            : new CloudRow("same", "Do the two images run one build?", $"no: {SameBuild.Api} runs {api} and {SameBuild.Renderer} runs {render}",
                [new("until they match, pages are drawn in the browser; Deploy Render can land after Deploy", null)], "warning");
    }

    /// <summary>What it costs: nothing, because GitHub does not bill a public package's storage or its pulls.</summary>
    private static CloudRow Cost() =>
        new("cost", "What does it cost?", "nothing on the Azure bill", [new("a public package on GitHub's registry is not billed, its storage or its pulls", null)], "good");

    /// <summary>A web app's linuxFxVersion as host, repository and tag: "DOCKER|ghcr.io/stevestout/theyard:v1" is (ghcr.io, stevestout/theyard, v1); null when it names no image.</summary>
    /// <param name="linuxFxVersion">The app's setting, as the plan card's hour kept it.</param>
    public static (string Host, string Repository, string Tag)? ImageOf(string? linuxFxVersion)
    {
        if (string.IsNullOrWhiteSpace(linuxFxVersion))
        {
            return null;
        }
        string reference = linuxFxVersion[(linuxFxVersion.IndexOf('|', StringComparison.Ordinal) + 1)..].Trim();
        int slash = reference.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0)
        {
            return null;
        }
        string path = reference[(slash + 1)..];
        int colon = path.LastIndexOf(':');
        return (reference[..slash], colon < 0 ? path : path[..colon], colon < 0 ? "" : path[(colon + 1)..]);
    }
}
