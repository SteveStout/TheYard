using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Api.Features.Links;
using TheYard.Application.Features.Links;

namespace TheYard.Tests;

/// <summary>
/// The seam every slice asks the document store through. <c>StoreRegistration</c> registers one
/// <see cref="DocumentStore"/> as a value, holding the account's store or null, and five places read
/// it to choose their adapter: the tagged links, the nightly run log, the plan history, the usage log
/// and the resource histories. Registering a value rather than the store itself is what lets those
/// five be registered unconditionally on a container that has no account at all, which is every
/// developer's machine and every run of this suite on SQLite.
///
/// What is held here is the null side of that choice, because it is the side a missing registration
/// would turn into an exception at startup instead of a card that says why it is empty. The store
/// side is held by the Cosmos tests against the real account, under their own trait.
/// </summary>
public sealed class DocumentStoreSeamTests
{
    /// <summary>A slice's services, with the seam already in them, as the host registers it before any module runs.</summary>
    private static ServiceProvider Wired(CosmosStore? document)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new DocumentStore(document));
        new TheYard.Api.Features.Links.Module().Register(services, new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    // #region no-account
    /// <summary>
    /// A container with no account: the seam carries null, every port behind it is still registered,
    /// and the one that reads it comes back as the in-memory form rather than throwing or being absent.
    /// </summary>
    [Fact]
    public void A_container_with_no_account_still_registers_the_seam_and_every_port_behind_it()
    {
        using var provider = Wired(null);

        Assert.Null(provider.GetRequiredService<DocumentStore>().Store);
        Assert.IsType<MemoryLinkOpens>(provider.GetRequiredService<ILinkOpens>());
        // The collector and the offers port are the same object, so a hit offered off the request
        // path reaches the keeper the module chose and nothing else.
        Assert.Same(provider.GetRequiredService<LinkOpenCollector>(), provider.GetRequiredService<ILinkOpenOffers>());
    }

    /// <summary>
    /// The seam is a value and nothing more: two seams holding the same store are the same seam, which
    /// is what makes it safe for five slices to read it rather than pass the store between them.
    /// </summary>
    [Fact]
    public void The_seam_is_a_value_so_two_of_them_over_the_same_store_are_equal()
    {
        var first = new DocumentStore(null);
        var second = new DocumentStore(null);

        Assert.Equal(first, second);
        Assert.NotSame(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }
    // #endregion no-account
}
