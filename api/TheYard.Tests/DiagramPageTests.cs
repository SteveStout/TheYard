using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// Diagram pages (ADR-020): every drawing in the catalog opens as an HTML page
/// with its SVG inlined and its title in the tab; an unknown name is a 404.
/// </summary>
public class DiagramPageTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    // #region page-tests
    [Fact]
    public async Task Every_diagram_in_the_catalog_opens_as_a_page_with_its_svg_inlined()
    {
        Assert.NotEmpty(DocsCatalog.Diagrams);
        foreach (var (name, diagram) in DocsCatalog.Diagrams)
        {
            var response = await _client.GetAsync($"/api/docs/diagrams/{name}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            string body = await response.Content.ReadAsStringAsync();
            // The title goes through HtmlEncode on the way into the tab, so this
            // asks for the encoded form. Asserting the raw string only ever passed
            // because the first two diagram titles had nothing in them to encode:
            // the ERD arrived with an apostrophe and the assertion fell over.
            Assert.Contains($"<title>{WebUtility.HtmlEncode(diagram.Title)}</title>", body);
            Assert.Contains("name=\"viewport\"", body);
            Assert.Contains("<svg", body);
            Assert.Contains("</svg>", body);
            Assert.DoesNotContain("<?xml", body);
        }
    }

    [Theory]
    [InlineData("/api/docs/diagrams/nope")]
    [InlineData("/api/docs/diagrams/infrastructure.svg")]
    [InlineData("/api/docs/diagrams")]
    public async Task An_unknown_diagram_is_a_404_not_a_file_read(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void The_page_drops_an_xml_prolog_and_keeps_the_svg()
    {
        string page = DiagramPage.Render("A & B", "<?xml version=\"1.0\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\"><title>t</title></svg>", "docs/images/x.svg");

        Assert.Contains("<title>A &amp; B</title>", page);
        Assert.DoesNotContain("<?xml", page);
        Assert.Contains("<svg xmlns=\"http://www.w3.org/2000/svg\"><title>t</title></svg>", page);
        Assert.Contains("blob/main/docs/images/x.svg", page);
    }

    [Fact]
    public void An_apostrophe_in_a_title_reaches_the_tab_encoded()
    {
        string page = DiagramPage.Render("TheYard's database", "<svg></svg>", "docs/images/erd.svg");

        Assert.Contains("<title>TheYard&#39;s database</title>", page);
        Assert.DoesNotContain("<title>TheYard's database</title>", page);
    }
    // #endregion page-tests

    // #region as-drawn
    /// <summary>
    /// A drawing's source is what its author drew and nothing else (ADR-006,
    /// the addendum on the provenance stamp). One of the tools that carries a
    /// file from the assistant's workspace to the developer's machine writes a
    /// signed content credential into every image it touches: a base64
    /// manifest in a metadata element and a namespace on the root. In a
    /// photograph that is a few kilobytes nobody sees; in an SVG it was half
    /// the file, and the diagram page inlines the whole file into its HTML on
    /// every visit. Every SVG the repository keeps is read for it here, so the
    /// next stamped copy is caught by the gate and not by a file size that
    /// looked wrong.
    /// </summary>
    [Fact]
    public void Every_drawing_is_the_source_as_drawn_with_no_stamp_written_into_it()
    {
        string images = Path.Combine(Repo.Root(), "docs", "images");
        var drawings = Directory.EnumerateFiles(images, "*.svg").OrderBy(path => path).ToList();
        Assert.NotEmpty(drawings);

        var stamped = drawings
            .Where(path =>
            {
                string source = File.ReadAllText(path);
                return source.Contains("<metadata", StringComparison.Ordinal)
                    || source.Contains("c2pa", StringComparison.OrdinalIgnoreCase);
            })
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(
            stamped.Count == 0,
            $"stamped on the way in, strip and re-copy: {string.Join(", ", stamped)}");
    }
    // #endregion as-drawn
}
