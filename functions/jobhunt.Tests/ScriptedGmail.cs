using System.Net;
using System.Text;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// Gmail as a script: each answer is queued under a method and a path prefix and handed out in order, the token
/// trade always answers, and anything unscripted fails the test with the request it was.
/// </summary>
internal sealed class ScriptedGmail : IHttpClientFactory
{
    private readonly List<(string Method, string Prefix, string Json, HttpStatusCode Status)> _answers = [];

    public List<string> Requests { get; } = [];

    public List<string> Bodies { get; } = [];

    public void On(string method, string prefix, string json, HttpStatusCode status = HttpStatusCode.OK) => _answers.Add((method, prefix, json, status));

    public HttpClient CreateClient(string name) => new(new Handler(this));

    private async Task<HttpResponseMessage> AnswerAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string url = request.RequestUri!.AbsoluteUri;
        Requests.Add($"{request.Method} {url}");
        if (request.Content is not null)
        {
            Bodies.Add(await request.Content.ReadAsStringAsync(ct));
        }
        if (url.StartsWith("https://oauth2.googleapis.com/token", StringComparison.Ordinal))
        {
            return Json("""{"access_token":"t"}""", HttpStatusCode.OK);
        }
        string path = url["https://gmail.googleapis.com/gmail/v1/users/me".Length..];
        int at = _answers.FindIndex(a => a.Method == request.Method.Method && path.StartsWith(a.Prefix, StringComparison.Ordinal));
        Assert.True(at >= 0, "unscripted request: " + Requests[^1]);
        var answer = _answers[at];
        _answers.RemoveAt(at);
        return Json(answer.Json, answer.Status);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class Handler(ScriptedGmail gmail) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => gmail.AnswerAsync(request, cancellationToken);
    }
}
