using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using TheBlock.Api;

namespace TheBlock.Tests;

/// <summary>
/// The API as production runs it. The developer exception page is a
/// Development-only middleware that sits in front of everything else and
/// answers with an HTML stack trace, so asking what a caller sees means asking
/// the environment a caller actually reaches.
/// </summary>
public sealed class ProductionApi : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        return base.CreateHost(builder);
    }
}

/// <summary>
/// The failure nobody wrote code for (ADR: The exception handler). Every other
/// test in this suite exercises a failure an endpoint returns on purpose. This
/// one exercises the path taken when an endpoint throws, which until this file
/// existed was the only part of the error handling that nothing proved.
/// </summary>
public class ExceptionHandlerTests(ProductionApi factory) : IClassFixture<ProductionApi>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string SelfTest = "/api/admin/selftest/exception";

    // #region exception-tests
    [Fact]
    public async Task An_unhandled_exception_answers_problem_details_carrying_a_trace_id()
    {
        var response = await _client.GetAsync(SelfTest);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(500, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("The request could not be completed", json.RootElement.GetProperty("title").GetString());
        Assert.Equal(ProblemHandler.ServerDetail, json.RootElement.GetProperty("detail").GetString());
        // The trace id is the entire value of a response that says nothing
        // else, so an empty one would make the rest of this shape pointless.
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task The_exception_never_reaches_the_caller()
    {
        string body = await (await _client.GetAsync(SelfTest)).Content.ReadAsStringAsync();

        // The three things a 500 leaks when nobody stops it: the message, the
        // type, and the stack. The endpoint throws with a distinctive sentence
        // precisely so this assertion can be about a real string.
        Assert.DoesNotContain("self-test", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(InvalidOperationException), body, StringComparison.Ordinal);
        Assert.DoesNotContain("at TheBlock", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_thrown_request_reaches_the_list_the_admin_tab_reads()
    {
        await _client.GetAsync(SelfTest);

        string errors = await _client.GetStringAsync("/api/errors");
        Assert.Contains(SelfTest, errors);
    }

    [Fact]
    public async Task A_malformed_body_is_the_callers_fault_and_says_which_part()
    {
        var response = await _client.PostAsync(
            "/api/errors/client",
            new StringContent("{ this is not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("The request could not be read", json.RootElement.GetProperty("title").GetString());
        // A caller who sent bad JSON is entitled to know that, which is the
        // one case where the exception's own message is the right answer.
        Assert.NotEqual(ProblemHandler.ServerDetail, json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task The_shape_of_a_crash_and_the_shape_of_a_rejected_query_are_the_same()
    {
        using var crash = JsonDocument.Parse(
            await (await _client.GetAsync(SelfTest)).Content.ReadAsStringAsync());
        using var rejected = JsonDocument.Parse(
            await (await _client.GetAsync("/api/vehicles?sort=alphabetical")).Content.ReadAsStringAsync());

        // One caller, one parser: whatever field it reads for a rejected query
        // has to be there when the server falls over, or the client needs two.
        var crashFields = crash.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();
        var rejectedFields = rejected.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(rejectedFields, crashFields);
    }
    // #endregion exception-tests
}
