using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace TheYard.Tests;

/// <summary>
/// The container's own fifteen-second sampler (ADR: What the machines are doing), the ring the machines card and
/// the plan's live line draw from: the host builds it with two hundred and forty samples, an hour at fifteen
/// seconds; the oldest sample is the one dropped; a ring asked for none keeps one; the limit and the processors are
/// the runtime's own numbers and the same on every read.
/// </summary>
public sealed class ContainerSamplerTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void The_host_keeps_an_hour_of_samples_at_fifteen_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), MachineSampler.Every);
        Assert.Equal(240, RingSizes.MachineSamples);
        Assert.Equal(TimeSpan.FromHours(1), MachineSampler.Every * RingSizes.MachineSamples);

        var sampler = factory.Services.GetRequiredService<MachineSampler>();
        for (int i = 0; i < RingSizes.MachineSamples + 5; i++)
        {
            sampler.Sample();
        }
        Assert.Equal(RingSizes.MachineSamples, sampler.Snapshot().Count);
    }

    [Fact]
    public void A_full_ring_drops_its_oldest_sample_and_keeps_the_newest()
    {
        var sampler = new MachineSampler(3);
        var first = sampler.Sample();
        var taken = new List<MachineSample> { first };
        for (int i = 0; i < 3; i++)
        {
            taken.Add(sampler.Sample());
        }

        var kept = sampler.Snapshot();
        Assert.DoesNotContain(first, kept);
        Assert.Equal(taken.Skip(1).ToList(), kept);
    }

    [Fact]
    public void A_ring_asked_for_no_samples_keeps_one()
    {
        var sampler = new MachineSampler(0);
        sampler.Sample();
        var newest = sampler.Sample();

        Assert.Equal([newest], sampler.Snapshot());
    }

    [Fact]
    public void The_limit_and_the_processors_are_the_runtime_s_and_read_the_same_each_time()
    {
        Assert.Equal(Environment.ProcessorCount, MachineSampler.Processors);
        Assert.Equal(MachineSampler.MemoryLimitMb, MachineSampler.MemoryLimitMb);
        Assert.Equal(Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024d / 1024d, 1), MachineSampler.MemoryLimitMb);
    }
}
