using Microsoft.Extensions.Configuration;

string local = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? string.Empty, ".config/jobhunt-server/appsettings.local.json");
ServerConfig config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile(local, optional: true)
    .Build()
    .Get<ServerConfig>() ?? new ServerConfig();

TimeProvider time = TimeProvider.System;
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var share = new ShareFolder(config.ShareRoot);
var claude = new ClaudeCli(config.ClaudePath);
var helpers = new ShareHelpers(config.Python, share);
var desktop = new ChromeDesktop();

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, press) =>
{
    press.Cancel = true;
    stop.Cancel();
};

// The service's stop and its four-hour limit arrive as SIGTERM: the run kills the model, writes its records and closes.
using System.Runtime.InteropServices.PosixSignalRegistration term = System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, signal =>
{
    signal.Cancel = true;
    stop.Cancel();
});

switch (args.FirstOrDefault())
{
    case "run":
        if (RunOptions.Parse(args) is not { } options)
        {
            Console.Error.WriteLine("run takes --dry-run and --lane feed|invites|messages|apply, and nothing else");
            return 2;
        }
        var siteUsage = new SiteUsage(config, http);
        var lanes = new RunLanes(
            share,
            new RunGuards(new JDriveMount(share), new FileRunLock(share, time), desktop, time),
            new LaneWork(new DayReader(share), new WorklistBuilder(share, helpers, helpers, helpers, config), new LaneRunner(share, new EssayDesk(share, helpers, claude), siteUsage, config, time), new InviteRecount(share, claude, config), new FeedLane(share, new FeedTools(claude, helpers, new UsageReport(share, siteUsage, config, time)), config, time)),
            config);
        (int exit, string output) = await lanes.ExecuteAsync(options, stop.Token);
        Console.WriteLine(output);
        return exit;
    case "probe":
        (int probeExit, string probed) = await new BrowserProbe(claude, desktop, Path.Combine(Path.GetTempPath(), "jobhunt-server-probe")).ExecuteAsync(stop.Token);
        Console.WriteLine(probed);
        return probeExit;
    case "status":
        Console.WriteLine(await new LastCard(share, time).ExecuteAsync(stop.Token));
        return 0;
    default:
        Console.Error.WriteLine("usage: JobHunt.Server run [--dry-run] [--lane feed|invites|messages|apply] | probe | status");
        return 2;
}
