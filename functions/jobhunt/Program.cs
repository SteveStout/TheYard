// The Function host for the job hunt's clock and its tracker merger (infra/jobhunt.bicep).
// No model runs here; the model runs in .github/workflows/nightly.yml.
using Azure.Storage.Files.Shares;
using JobHunt.Functions;
using JobHunt.Functions.Merge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddSingleton(_ => new ShareClient(
            Environment.GetEnvironmentVariable("JOBHUNT_SHARE_CONNECTION"),
            Environment.GetEnvironmentVariable("JOBHUNT_SHARE_NAME") ?? "jobhunt"));
        services.AddHttpClient();
        services.AddSingleton<ShareSnapshot>();
        services.AddSingleton<Bookend>();
        services.AddSingleton<Dispatch>();
        services.AddSingleton<IMergeStore>(sp => new ShareMergeStore(
            sp.GetRequiredService<ShareClient>(), Environment.GetEnvironmentVariable("MERGER_ROOT") ?? string.Empty));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TrackerMerger>();
    })
    .Build();

host.Run();
