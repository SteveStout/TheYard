using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

/// <summary>
/// The resume at /resume (ADR: The resume has an address): the locked resume,
/// byte for byte, inline in the browser under his name and the lock's date, and
/// the door it was opened through recorded as one allowed word, never the query.
/// </summary>
public class ResumeAddressTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    // #region resume-address
    [Fact]
    public async Task Resume_answers_200_application_pdf_inline_with_his_name_and_the_lock_date()
    {
        var response = await _client.GetAsync("/resume");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("inline", disposition.DispositionType);
        Assert.Equal("StevenStoutResume_2026-10-24.pdf", disposition.FileName?.Trim('"'));
    }

    [Fact]
    public async Task Resume_bytes_equal_the_lock_sha256()
    {
        byte[] body = await _client.GetByteArrayAsync("/resume");
        string served = Convert.ToHexStringLower(SHA256.HashData(body));
        string locked = File.ReadAllText(Path.Combine(Repo.Root(), "api", "TheYard.Api", "wwwroot", "docs", "resume.sha256")).Trim();

        Assert.Equal(locked, served);
    }

    [Fact]
    public async Task Resume_and_api_docs_resume_serve_the_same_bytes()
    {
        byte[] address = await _client.GetByteArrayAsync("/resume?src=linkedin");
        byte[] document = await _client.GetByteArrayAsync("/api/docs/resume");

        Assert.Equal(document, address);
    }

    [Theory]
    [InlineData("linkedin", "linkedin")]
    [InlineData("LinkedIn", "linkedin")]
    [InlineData(" post ", "post")]
    [InlineData("application", "application")]
    [InlineData("x", "other")]
    [InlineData("", "other")]
    [InlineData(null, "other")]
    public void A_known_src_becomes_its_door_and_an_unknown_src_is_other(string? src, string door)
    {
        Assert.Equal(door, ResumeDoor.Of(src));
        Assert.Equal("/resume/" + door, ResumeDoor.PathFor(src));
    }

    [Fact]
    public async Task The_query_string_never_reaches_the_activity_row()
    {
        await _client.GetAsync("/resume?src=linkedin&email=someone@example.com");

        // The request ring records the same path the activity hook does.
        string metrics = await _client.GetStringAsync("/api/admin/metrics");
        Assert.Contains("\"/resume/linkedin\"", metrics);
        Assert.DoesNotContain("@", metrics);
        Assert.DoesNotContain("src=", metrics);
    }
    // #endregion resume-address
}
