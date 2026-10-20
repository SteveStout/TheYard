// The Function host for the job hunt's 4:00 AM bookkeeping (infra/jobhunt.bicep).
// No model runs here; the model runs in .github/workflows/nightly.yml.
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
    })
    .Build();

host.Run();
