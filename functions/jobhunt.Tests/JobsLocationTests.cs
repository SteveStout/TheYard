using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// RULE 76 check 1 read at the location gate itself (JobsGates.LocationFailure), one case for each way a place is
/// shown to be in the US: a state's code with the arrangement in brackets, a code in lower case, a state's name, the
/// country's own words and the city names the boards write; and one for each way a role stays out: a place abroad, a
/// remote role limited to named states, and an onsite place that names no state or country.
/// </summary>
public sealed class JobsLocationTests
{
    [Theory]
    [InlineData("onsite", "Austin, TX (Hybrid)")]
    [InlineData("onsite", "Denver, co")]
    [InlineData("onsite", "Columbus, Ohio")]
    [InlineData("onsite", "New York City")]
    [InlineData("onsite", "NYC")]
    [InlineData("onsite", "Bay Area")]
    [InlineData("onsite", "Washington, D.C.")]
    [InlineData("onsite", "Chicago, IL, USA")]
    [InlineData("onsite", "Kansas City, Missouri, United States")]
    [InlineData("hybrid outside St. Louis: Chicago, IL", "Chicago, IL")]
    [InlineData("hybrid outside St. Louis: Seattle, WA", "Seattle, Washington")]
    [InlineData("St. Louis hybrid, 1 day", "St. Louis, MO")]
    [InlineData("hybrid, days unknown", "Clayton, MO")]
    [InlineData("remote", "Warsaw, Poland")]
    [InlineData("remote", "")]
    public void A_US_place_or_a_remote_role_passes_the_location_gate(string remote, string location) =>
        Assert.Null(JobsGates.LocationFailure(remote, location));

    [Theory]
    [InlineData("onsite", "London, United Kingdom", "outside the US: London, United Kingdom")]
    [InlineData("onsite", "Toronto, ON, Canada", "outside the US: Toronto, ON, Canada")]
    [InlineData("hybrid outside St. Louis: Berlin, Germany", "Berlin, Germany", "outside the US: Berlin, Germany")]
    [InlineData("onsite", "  Bangalore, Karnataka, India  ", "outside the US: Bangalore, Karnataka, India")]
    [InlineData("onsite", "Paris", "onsite")]
    public void A_place_abroad_or_one_that_names_no_US_place_fails(string remote, string location, string failed) =>
        Assert.Equal(failed, JobsGates.LocationFailure(remote, location));

    [Fact]
    public void A_remote_role_limited_to_named_states_without_Missouri_keeps_its_own_read()
    {
        string read = JobsGates.NamedStates + "CA, NY";
        Assert.Equal(read, JobsGates.LocationFailure(read, "Remote, US"));
    }

    [Fact]
    public void A_bare_country_whose_posting_never_says_remote_fails_with_the_place_named()
    {
        Assert.Equal("not remote: Canada, and the title and text never say remote", JobsGates.LocationFailure(JobsGates.CountryOnly, "Canada"));
        Assert.Null(JobsGates.LocationFailure(JobsGates.CountryOnly, "United States"));
    }
}
