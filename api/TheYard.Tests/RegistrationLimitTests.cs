using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// The hour's allowance of new accounts (ADR: The one write a stranger can
/// make).
///
/// <para>Registration is the only thing an anonymous caller can do that leaves
/// a durable row, and every request through it pays for a deliberately
/// expensive password hash. Both of those were unbounded, so the cheap attack
/// on this site was a loop against one endpoint.</para>
/// </summary>
public class RegistrationLimitTests
{
    // #region the window itself
    /// <summary>A clock the test moves, so an hour costs no seconds.</summary>
    private sealed class Clock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset Read() => Now;
    }

    [Fact]
    public void The_allowance_runs_out()
    {
        var clock = new Clock();
        var limit = new RegistrationLimit(3, clock.Read);

        Assert.True(limit.TryTake());
        Assert.True(limit.TryTake());
        Assert.True(limit.TryTake());
        Assert.False(limit.TryTake());
    }

    [Fact]
    public void It_slides_rather_than_resetting_on_the_hour()
    {
        var clock = new Clock();
        var limit = new RegistrationLimit(2, clock.Read);

        Assert.True(limit.TryTake());
        clock.Now = clock.Now.AddMinutes(30);
        Assert.True(limit.TryTake());
        Assert.False(limit.TryTake());

        // Thirty-one minutes later the first one has aged out and the second
        // has not, so exactly one slot is back. A bucket that reset on the hour
        // would have handed back both, which is how an attacker takes a whole
        // allowance twice in two minutes by arriving either side of a reset.
        clock.Now = clock.Now.AddMinutes(31);
        Assert.True(limit.TryTake());
        Assert.False(limit.TryTake());
    }

    [Fact]
    public void A_refusal_costs_the_refused_caller_nothing_and_extends_nothing()
    {
        var clock = new Clock();
        var limit = new RegistrationLimit(1, clock.Read);

        Assert.True(limit.TryTake());
        clock.Now = clock.Now.AddMinutes(59);
        for (int attempt = 0; attempt < 50; attempt++)
        {
            Assert.False(limit.TryTake());
        }

        // If a refusal were recorded, fifty of them at minute 59 would hold the
        // window open for another hour and the allowance would never come back
        // while anybody kept trying, which is the failure that turns a limiter
        // into the outage it was meant to prevent.
        clock.Now = clock.Now.AddMinutes(2);
        Assert.True(limit.TryTake());
    }
    // #endregion the window itself

    // #region the endpoint
    private static WebApplicationFactory<Program> ApiAllowing(int perHour) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Accounts:RegistrationsPerHour", perHour.ToString());
            builder.UseSetting("Auth:SigningKey", "a-signing-key-for-tests-only-not-a-secret");
        });

    private static Task<HttpResponseMessage> Register(HttpClient client) =>
        client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = $"limit-{Guid.NewGuid():N}@example.com", password = "correct horse" });

    [Fact]
    public async Task Past_the_allowance_the_endpoint_answers_429_and_says_so()
    {
        await using var api = ApiAllowing(2);
        var client = api.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await Register(client)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Register(client)).StatusCode);

        var refused = await Register(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);

        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        string title = problem.GetProperty("title").GetString()!;
        string detail = problem.GetProperty("detail").GetString()!;

        Assert.Contains("Try again in an hour", detail, StringComparison.Ordinal);
        // It says what happened. It does not say how many accounts exist or how
        // much of the allowance is left, either of which would turn the refusal
        // into a counter for anybody willing to ask it twice. No digit reaches
        // the words, which is a cheaper rule to keep than a careful sentence.
        Assert.DoesNotContain(title, c => char.IsDigit(c));
        Assert.DoesNotContain(detail, c => char.IsDigit(c));
    }

    [Fact]
    public async Task The_rest_of_the_site_is_untouched_when_registration_is_closed()
    {
        await using var api = ApiAllowing(1);
        var client = api.CreateClient();

        string email = $"limit-{Guid.NewGuid():N}@example.com";
        var created = await client.PostAsJsonAsync(
            "/api/auth/register", new { email, password = "correct horse" });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Register(client)).StatusCode);

        // A closed door for new accounts is not a closed site. Somebody who
        // already has one signs in, and a visitor with none still browses.
        var stranger = api.CreateClient();
        var signedIn = await stranger.PostAsJsonAsync(
            "/api/auth/login", new { email, password = "correct horse" });
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync("/api/facets")).StatusCode);
    }
    // #endregion the endpoint
}
