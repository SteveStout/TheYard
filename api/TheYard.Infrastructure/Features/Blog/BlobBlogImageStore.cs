// The blog's pictures in a blob container (ADR-113, the addendum on where the pictures live): the two ports
// over the blob service's REST interface, through the one connection pool, with a token the host hands in
// (the site's managed identity, Storage Blob Data Reader and Contributor on the container). Nothing here is
// anonymous: the container is private, and the host serves its pictures from the site's own address.
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
using TheYard.Application.Features.Blog;
using TheYard.Domain.Features.Blog;
using TheYard.Infrastructure.Shared.Http;

namespace TheYard.Infrastructure.Features.Blog;

// #region blob-blog-images
/// <summary>The pictures kept as blobs under their hashed names.</summary>
/// <param name="account">The storage account.</param>
/// <param name="container">The container.</param>
/// <param name="token">A bearer token for https://storage.azure.com/, from the host's identity.</param>
public sealed class BlobBlogImageStore(string account, string container, Func<CancellationToken, Task<string>> token) : IBlogImageReader, IBlogImageWriter
{
    /// <summary>The blob service's API version every request names.</summary>
    public const string Version = "2023-11-03";

    private static Outbound Http => OutboundHttp.Client(TimeSpan.FromSeconds(20));

    /// <summary>A picture's address in the container.</summary>
    /// <param name="name">Its kept name.</param>
    public Uri Address(string name) => new($"https://{account}.blob.core.windows.net/{Uri.EscapeDataString(container)}/{Uri.EscapeDataString(name)}");

    public async Task<BlogImageFile?> OpenAsync(string name, CancellationToken cancellation)
    {
        using var request = await RequestAsync(HttpMethod.Get, name, cancellation);
        using var response = await Http.SendAsync(request, cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        string type = BlogImageNames.ContentType(name) ?? response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        return new BlogImageFile(await response.Content.ReadAsByteArrayAsync(cancellation), type);
    }

    public async Task<BlogImageTally> TallyAsync(CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{account}.blob.core.windows.net/{Uri.EscapeDataString(container)}?restype=container&comp=list&maxresults=5000");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await token(cancellation));
        request.Headers.Add("x-ms-version", Version);
        using var response = await Http.SendAsync(request, cancellation);
        if (!response.IsSuccessStatusCode)
        {
            return new BlogImageTally(null, 0, $"the container's listing answered {(int)response.StatusCode}");
        }
        var sizes = XDocument.Parse(await response.Content.ReadAsStringAsync(cancellation)).Descendants("Content-Length")
            .Select(e => long.TryParse(e.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? n : 0).ToList();
        return new BlogImageTally(sizes.Count, sizes.Sum(), $"the {container} container's listing on {account}");
    }

    public async Task PutAsync(string name, BlogImageFile file, CancellationToken cancellation)
    {
        using var request = await RequestAsync(HttpMethod.Put, name, cancellation);
        request.Headers.Add("x-ms-blob-type", "BlockBlob");
        // The name is the bytes' own hash, so a name already kept is these bytes: never overwrite it.
        request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
        request.Content = new ByteArrayContent(file.Bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
        using var response = await Http.SendAsync(request, cancellation);
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
        {
            return;
        }
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string name, CancellationToken cancellation)
    {
        var request = new HttpRequestMessage(method, Address(name));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await token(cancellation));
        request.Headers.Add("x-ms-version", Version);
        return request;
    }
}
// #endregion blob-blog-images
