using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// A marker an earlier build wrote under one code's name stands for its person and role: every other code that resolves
/// to the same person and role gets no follow-up either, in the live pass and in the dry run alike, whichever code the
/// marker names and in whatever order the codes come.
/// </summary>
public sealed partial class PounceFollowUpTests
{
    [Theory]
    [InlineData("h7m3zt")]
    [InlineData("j8c4wp")]
    public async Task A_code_marker_from_the_last_build_holds_every_code_for_its_person_and_role_live_and_dry(string marked)
    {
        using var scratch = new Scratch();
        scratch.Write(PounceFollowUp.Tokens, """
            code,person,company,channel,role,thread,sent,lane
            h7m3zt,"Leader at Corvid Health, name found at send",Corvid Health,LinkedIn,,,2026-11-11T13:02:00Z,pounce-12801
            j8c4wp,"Leader at Corvid Health, name found at send",Corvid Health,LinkedIn,,,2026-11-11T13:04:00Z,pounce-12801
            """);
        scratch.Write($"nightly/pounce/followups/12801-{marked}.md", $"{marked} dispatch/ready/15-pouncefollow-{marked}.md 2026-11-11T15:50:00.0000000+00:00\n");
        var visits = new Visits
        {
            Links = """
                {"available":true,"note":null,"window":"7d","codes":[
                {"code":"h7m3zt","first_ms":1794410400000,"last_ms":1794411060000,"opens":2,"days":1,"paths":{"/":2}},
                {"code":"j8c4wp","first_ms":1794411000000,"last_ms":1794411300000,"opens":2,"days":1,"paths":{"/":2}}],"keys":[]}
                """,
        };
        var tracker = new Tracker { Since = { Sent("Mara Lind", "Corvid Health", "h7m3zt"), Sent("Mara Lind", "Corvid Health", "j8c4wp") } };
        var follow = new PounceFollowUps(scratch.Store, visits, tracker, new Fixed(Now));

        var lines = await follow.DryRunAsync(CancellationToken.None);
        var pass = await follow.RunAsync(PounceHandoff.Live, CancellationToken.None);

        Assert.Equal(2, lines.Count(line => line.Contains("already has a follow-up for this role", StringComparison.Ordinal)));
        Assert.DoesNotContain(lines, line => line.Contains("dry follow-up, nothing queued", StringComparison.Ordinal));
        Assert.Equal(new FollowUpPass(2, 2, 2, 0, 0, 2), pass);
        Assert.False(scratch.Has("dispatch/ready/15-pouncefollow-h7m3zt.md"));
        Assert.False(scratch.Has("dispatch/ready/15-pouncefollow-j8c4wp.md"));
    }
}
