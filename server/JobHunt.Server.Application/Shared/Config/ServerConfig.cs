namespace JobHunt.Server.Application.Shared.Config;

/// <summary>One lane's settings: appsettings.json ships them, and the local config overrides them line by line.</summary>
public sealed class LaneConfig
{
    /// <summary>The model id the lane runs on.</summary>
    public string Model { get; set; } = "claude-sonnet-5-5";

    /// <summary>The CLI's effort level.</summary>
    public string Effort { get; set; } = "low";

    /// <summary>The CLI's turn ceiling.</summary>
    public int MaxTurns { get; set; } = 150;

    /// <summary>The wall-clock limit the program enforces, in minutes.</summary>
    public int TimeoutMinutes { get; set; } = 40;

    /// <summary>False leaves the lane out of the run.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The model the lane may move to after five clean runs, empty when it stays where it is.</summary>
    public string TargetModel { get; set; } = string.Empty;
}

/// <summary>The program's settings.</summary>
public sealed class ServerConfig
{
    /// <summary>The share's mount on this box.</summary>
    public string ShareRoot { get; set; } = "/home/vincent/J";

    /// <summary>The Claude CLI.</summary>
    public string ClaudePath { get; set; } = "/home/vincent/.local/bin/claude";

    /// <summary>The Python the share's helpers run under.</summary>
    public string Python { get; set; } = "/usr/bin/python3";

    /// <summary>True makes every run a dry run, the switch the service unit's proof uses.</summary>
    public bool DryRun { get; set; }

    /// <summary>The most applications one morning, Rule 58's ten a night.</summary>
    public int MostApplications { get; set; } = 10;

    /// <summary>Rows per lane a dry run works.</summary>
    public int DryRows { get; set; } = 2;

    /// <summary>The site the usage goes to.</summary>
    public string Site { get; set; } = "https://theyard.stevenstout.biz";

    /// <summary>The file holding the admin key, mode 600, never in git.</summary>
    public string SecretsFile { get; set; } = "/home/vincent/.config/jobhunt-server/secrets.json";

    /// <summary>Settings per lane, by the lane's word.</summary>
    public Dictionary<string, LaneConfig> Lanes { get; set; } = [];

    /// <summary>A lane's settings.</summary>
    /// <param name="lane">The lane.</param>
    public LaneConfig For(Lane lane) => Lanes.TryGetValue(RunSlot.Word(lane), out var config) ? config : new LaneConfig();
}
