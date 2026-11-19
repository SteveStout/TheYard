namespace TheYard.Tests;

/// <summary>
/// The fail proof of ADR-137, One traffic cop for main: one test that fails on purpose, pushed on a ready branch seconds
/// before a good one, so the cop's red path is seen on a real run. The branch is skipped with a failing status naming
/// this test, never touches main, and the good branch behind it still lands. It is kept as evidence and never merged.
/// </summary>
public sealed class CopFailProofTests
{
    [Fact]
    public void The_fail_proof_fails_on_purpose() => Assert.Fail("cop1118 fail proof: this branch is meant to be red and never to land");
}
