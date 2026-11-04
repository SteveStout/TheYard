using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

/// <summary>
/// The review route's short door (ADR: Ten minutes, seven stops): /review answers a permanent
/// redirect to the view, /?view=review, carrying whatever query the link carried, so a code on a
/// business card or in a message (/review?r=zzrev1) reaches the page that reports the open.
/// </summary>
public class ReviewAddressTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Theory]
    [InlineData("/review", "/?view=review")]
    [InlineData("/review?r=zzrev1", "/?view=review&r=zzrev1")]
    [InlineData("/review?r=zzrev1&utm_source=card", "/?view=review&r=zzrev1&utm_source=card")]
    [InlineData("/review?view=admin&r=zzrev1", "/?view=review&r=zzrev1")]
    public async Task Review_answers_a_permanent_redirect_to_the_view_with_the_query_carried(string asked, string location)
    {
        var response = await _client.GetAsync(asked);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(location, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public void The_page_sweep_checks_the_door_as_a_page_wherever_the_app_is_served()
    {
        var door = ServedAddresses.FromTheBuild.Single(address => address.Address == "/review");
        Assert.Equal("page", door.Kind);
        Assert.Contains(ServedAddresses.All(frontendServed: true), address => address.Address == "/review");
    }
}
