using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using TheBlock.Api;

namespace TheBlock.Tests;

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
            Assert.Contains($"<title>{diagram.Title}</title>", body);
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
    // #endregion page-tests
}
