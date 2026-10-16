using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TestProject.Controllers;

namespace TestProject.Tests;

/// <summary>
/// Checks the refusals that come from the web server rather than from the app or the disk. A
/// request body past the server's own limit throws BadHttpRequestException, which is a kind of
/// IOException; without its own rule it would be answered as a locked file. The form reader throws
/// InvalidDataException both for a form past its limit (a 413) and for a body it could not read, cut
/// short or missing its boundary (a 400); either would otherwise be a 500. The handler runs with
/// the app's real problem-document writer. (more in docs/ADR-004-the-wire.md)
/// </summary>
public sealed class ProblemResponseHandlerTests : IDisposable
{
    private readonly TempHome _home = new();
    private readonly WebApplicationFactory<Program> _factory;

    public ProblemResponseHandlerTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Files:Home", _home.Root);
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _home.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task A_request_the_server_cut_off_keeps_the_servers_status_and_message()
    {
        (int status, JsonElement problem) = await Handled(new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge));
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, status);
        Assert.Equal("The request was refused", problem.GetProperty("title").GetString());
        Assert.Equal("Request body too large.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_form_past_the_form_readers_limit_is_a_413_not_a_500()
    {
        (int status, JsonElement problem) = await Handled(new InvalidDataException("Multipart body length limit exceeded."));
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, status);
        Assert.Equal("The upload is too large", problem.GetProperty("title").GetString());
        Assert.Equal("The upload is larger than the server accepts.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_form_the_reader_could_not_read_is_a_400_not_a_413()
    {
        (int status, JsonElement problem) = await Handled(new InvalidDataException("Unexpected end of Stream, the content may have already been read by another component."));
        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal("The upload could not be read", problem.GetProperty("title").GetString());
        Assert.Equal("The upload's body was cut short or is not a form the server can read.", problem.GetProperty("detail").GetString());
    }

    /// <summary>Runs the handler on one exception and reads back the status and the problem document it wrote.</summary>
    private async Task<(int Status, JsonElement Problem)> Handled(Exception exception)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DefaultHttpContext context = new() { RequestServices = scope.ServiceProvider };
        using MemoryStream body = new();
        context.Response.Body = body;
        ProblemResponseHandler handler = new(scope.ServiceProvider.GetRequiredService<IProblemDetailsService>());

        Assert.True(await handler.TryHandleAsync(context, exception, CancellationToken.None));

        body.Position = 0;
        JsonElement problem = await JsonSerializer.DeserializeAsync<JsonElement>(body);
        return (context.Response.StatusCode, problem);
    }
}
