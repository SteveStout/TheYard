// What the nightly card says each process is, read from the Function's own code (ADR-109, the addendum on the
// six processes): each timer file's summary comment between its "#region summary" markers, and the
// Function's own paragraph between Program.cs's "#region what-it-is" markers, through the code-regions port the
// documents' live-sample reader answers (ADR-014), from the repository files this build carries. So the words on the card
// are the words in the code at the version deployed; the files do not change while the process runs, so each
// is read once.
using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;

namespace TheYard.Api.Features.Admin.Cloud;

// #region process-descriptions
/// <summary>The seven processes' summaries and the Function's paragraph, read from the repository files in this build.</summary>
/// <param name="regions">The repository's marked regions, as the live-sample reader renders them.</param>
public sealed partial class ProcessDescriptions(ICodeRegions regions)
{
    /// <summary>The folder the Function's sources are in, relative to the repository's root.</summary>
    public const string Folder = "functions/jobhunt/";

    /// <summary>Each process's file, by its key; the Function's own paragraph is Program.cs's.</summary>
    public static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AtsSweep"] = "AtsSweepTimer.cs",
        ["Merger"] = "Merger.cs",
        ["Nightly"] = "Nightly.cs",
        ["SheetMirror"] = "SheetMirrorTimer.cs",
        ["DailyCard"] = "DailyCardTimer.cs",
        ["CampaignSend"] = "CampaignSendTimer.cs",
        ["MergerHealth"] = "MergerWatch.cs",
    };

    /// <summary>Each file's paragraph once read: eight at most, the seven processes and the Function's own.</summary>
    private readonly ConcurrentDictionary<string, string> _read = new(StringComparer.Ordinal);

    /// <summary>The Function's paragraph: what it is, in one paragraph.</summary>
    public string Function() => _read.GetOrAdd("", _ => Paragraph("Program.cs", "what-it-is"));

    /// <summary>One process's summary, by its key.</summary>
    /// <param name="key">One of the seven.</param>
    public string For(string key) => Files.TryGetValue(key, out string? file) ? _read.GetOrAdd(key, _ => Paragraph(file, "summary")) : "not one of the seven";

    /// <summary>
    /// One region of one file as a paragraph: the live reader's fenced block with the comment marks, the
    /// XML tags and the line breaks taken out. A region the build does not carry says so in place of the words.
    /// </summary>
    private string Paragraph(string file, string region)
    {
        string block = regions.Read(Folder + file, region);
        if (block.StartsWith("*Sample unavailable", StringComparison.Ordinal))
        {
            return $"not in this build: {Folder}{file} has no {region} region here";
        }
        return Fold(block);
    }

    /// <summary>A live block's code lines as one paragraph: "///" and "//" marks, XML tags and the fences gone, the lines joined.</summary>
    /// <param name="block">The live reader's answer.</param>
    public static string Fold(string block)
    {
        var lines = block.Split('\n').Where(line => !line.StartsWith("```", StringComparison.Ordinal) && !line.StartsWith("*Live from", StringComparison.Ordinal))
            .Select(line => Marks().Replace(line.Trim(), ""))
            .Select(line => Tags().Replace(line, ""))
            .Where(line => line.Length > 0);
        return WebUtility.HtmlDecode(string.Join(' ', lines)).Trim();
    }

    [GeneratedRegex(@"^/{2,3}\s?")]
    private static partial Regex Marks();

    [GeneratedRegex(@"</?[a-z]+[^>]*>")]
    private static partial Regex Tags();
}
// #endregion process-descriptions
