// The Function host for the job hunt's clock and its tracker merger (infra/jobhunt.bicep).
// No model runs here; the model runs in .github/workflows/nightly.yml.
// #region what-it-is
// The job hunt's clock and its hands. One Function App on Azure runs six processes against the job-hunt share so
// the laptop is never needed for the grunt work: the inbox is swept every half hour, the tracker is merged every
// minute, the Sheet is mirrored every quarter hour, the morning card is written, and the two GitHub runs (the reply
// lane at 4 AM, the records run every four hours) are dispatched on the hour. Each process has one switch (on,
// shadow or off) read from the app's settings; shadow plans and writes the plan, on acts. Its identity holds one
// role on one share and may send one kind of dispatch to one repository, nothing else.
// #endregion what-it-is
using Azure.Storage.Files.Shares;
using JobHunt.Functions;
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
