using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

/// <summary>
/// The files the blog offers to save (ADR-093, the addendum on downloads): the keynote transcript
/// answers as plain text in UTF-8 with an attachment header naming it, byte for byte as it was handed over, and a file
/// outside /downloads/ keeps the headers it had, with no attachment header.
/// </summary>
public class DownloadsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Transcript = "microsoft-ignite-2026-opening-keynote-transcript.txt";

    /// <summary>The SHA-256 of the transcript as it was handed over, CRLF line endings and all.</summary>
    private const string TranscriptSha256 = "6444caae140861408f0023b4860a445d10b93152d62f3bb21ecc8e865acfec22";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task The_transcript_downloads_as_utf8_plain_text_under_its_own_name_byte_for_byte()
    {
        var response = await _client.GetAsync($"/downloads/{Transcript}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal($"attachment; filename=\"{Transcript}\"", response.Content.Headers.ContentDisposition?.ToString());
        byte[] body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(TranscriptSha256, Convert.ToHexStringLower(SHA256.HashData(body)));
    }

    [Fact]
    public async Task A_file_outside_downloads_keeps_its_own_type_and_no_attachment_header()
    {
        var response = await _client.GetAsync("/images/coupe-01-480.avif");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/avif", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Content.Headers.ContentDisposition);
    }

    [Fact]
    public async Task The_file_in_the_repository_is_the_one_handed_over()
    {
        byte[] file = await File.ReadAllBytesAsync(Path.Combine(Repo.Root(), "api", "TheYard.Api", "wwwroot", "downloads", Transcript));

        Assert.Equal(TranscriptSha256, Convert.ToHexStringLower(SHA256.HashData(file)));
        Assert.Contains("\r\n", System.Text.Encoding.UTF8.GetString(file), StringComparison.Ordinal);
    }
}
