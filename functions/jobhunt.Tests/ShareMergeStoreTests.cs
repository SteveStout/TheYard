using System.Net;
using Azure.Core.Pipeline;
using Azure.Storage.Files.Shares;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The merger's append on the real share adapter, answered by a recording handler in place of Azure Files. Growing a
/// file sets its size through Set File Properties, and a request that names only the size leaves every other property
/// alone, the stored Content-MD5 with it: a file first uploaded whole (which stores its hash) and then appended to keeps
/// the hash of its older bytes, and every whole read that checks the hash fails. The resize therefore carries the
/// file's own content headers, which the service sets together, so the hash is cleared rather than left stale.
/// </summary>
public sealed class ShareMergeStoreTests
{
    private static readonly byte[] StaleHash = Convert.FromBase64String("eC54DVMDcTkShgCFyFR3kQ==");

    [Fact]
    public async Task An_append_clears_the_stored_hash_and_keeps_the_content_type()
    {
        var share = new RecordingShare(size: 10, contentType: "text/markdown", hash: StaleHash);

        await share.Store.AppendAsync("writing-samples.md", "more\n"u8.ToArray(), CancellationToken.None);

        HttpRequestMessage resize = share.Requests.Single(r => r.Method == HttpMethod.Put && r.RequestUri!.Query.Contains("comp=properties", StringComparison.Ordinal));
        Assert.Equal("15", Header(resize, "x-ms-content-length"));
        Assert.Equal("text/markdown", Header(resize, "x-ms-content-type"));
        Assert.Null(Header(resize, "x-ms-content-md5"));
        Assert.Single(share.Requests, r => r.RequestUri!.Query.Contains("comp=range", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_append_to_a_file_with_no_stored_type_still_names_one_so_the_hash_goes()
    {
        var share = new RecordingShare(size: 3, contentType: null, hash: StaleHash);

        await share.Store.AppendAsync("nightly/resume-drift.tsv", "x\n"u8.ToArray(), CancellationToken.None);

        HttpRequestMessage resize = share.Requests.Single(r => r.Method == HttpMethod.Put && r.RequestUri!.Query.Contains("comp=properties", StringComparison.Ordinal));
        Assert.Equal("application/octet-stream", Header(resize, "x-ms-content-type"));
        Assert.Null(Header(resize, "x-ms-content-md5"));
    }

    private static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? string.Join(",", values)
        : request.Content?.Headers.TryGetValues(name, out var content) == true ? string.Join(",", content!) : null;

    /// <summary>A share whose one file exists at a size, a stored type and a stored hash, recording every request.</summary>
    private sealed class RecordingShare
    {
        public RecordingShare(long size, string? contentType, byte[] hash)
        {
            var handler = new Answer(this, size, contentType, hash);
            var options = new ShareClientOptions { Transport = new HttpClientTransport(handler) };
            options.Retry.MaxRetries = 0;
            Store = new ShareMergeStore(new ShareClient(new Uri("https://test.file.core.windows.net/jobhunt?sv=2025-01-05&sig=x"), options), string.Empty);
        }

        public ShareMergeStore Store { get; }

        public List<HttpRequestMessage> Requests { get; } = [];

        private sealed class Answer(RecordingShare share, long size, string? contentType, byte[] hash) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                share.Requests.Add(request);
                var response = new HttpResponseMessage(request.Method == HttpMethod.Head ? HttpStatusCode.OK : request.RequestUri!.Query.Contains("comp=range", StringComparison.Ordinal) ? HttpStatusCode.Created : HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([]),
                };
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"0x1\"");
                response.Headers.Add("x-ms-request-id", Guid.NewGuid().ToString());
                response.Content.Headers.LastModified = new DateTimeOffset(2026, 11, 9, 14, 0, 0, TimeSpan.Zero);
                if (request.Method == HttpMethod.Head)
                {
                    response.Headers.Add("x-ms-type", "File");
                    response.Content.Headers.ContentLength = size;
                    response.Content.Headers.ContentMD5 = hash;
                    if (contentType is not null)
                    {
                        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                    }
                }
                return Task.FromResult(response);
            }
        }
    }
}
