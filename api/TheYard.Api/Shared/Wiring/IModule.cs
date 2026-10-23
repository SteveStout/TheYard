namespace TheYard.Api.Shared.Wiring;

// #region module
/// <summary>
/// One slice of the host (ADR: The onion cut into vertical slices): what it registers and what it
/// maps, each from the slice's own folder. Program.cs finds every module in this assembly, registers
/// them before the host is built and maps them after the shared pipeline is in place, so adding a
/// slice is a folder and nothing else.
/// </summary>
public interface IModule
{
    /// <summary>Registers what the slice's endpoints and services need.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration, which a module may read while registering.</param>
    void Register(IServiceCollection services, IConfiguration configuration);

    /// <summary>Maps the slice's routes.</summary>
    /// <param name="app">The built host's routes.</param>
    void Map(IEndpointRouteBuilder app);
}
// #endregion module
