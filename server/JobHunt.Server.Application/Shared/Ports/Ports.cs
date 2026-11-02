namespace JobHunt.Server.Application.Shared.Ports;

/// <summary>A file on the share, with when it last changed.</summary>
/// <param name="Path">The path relative to the share's root.</param>
/// <param name="Changed">The last write.</param>
public sealed record ShareFile(string Path, DateTimeOffset Changed);

/// <summary>The job-hunt share. Every write goes through the write policy before it reaches the disk.</summary>
public interface IShare
{
    /// <summary>The share's real path, with any link resolved.</summary>
    string Root { get; }

    /// <summary>The real path of a relative one.</summary>
    /// <param name="relative">The path relative to the root.</param>
    string Full(string relative);

    /// <summary>A file's text, with any byte order mark gone.</summary>
    /// <param name="relative">The path.</param>
    /// <param name="ct">Stops the read.</param>
    Task<string> ReadAsync(string relative, CancellationToken ct);

    /// <summary>The end of a file.</summary>
    /// <param name="relative">The path.</param>
    /// <param name="bytes">How much of the end.</param>
    /// <param name="ct">Stops the read.</param>
    Task<string> TailAsync(string relative, int bytes, CancellationToken ct);

    /// <summary>The files in a folder matching a pattern, not recursive; empty when the folder is missing.</summary>
    /// <param name="folder">The folder.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="ct">Stops the read.</param>
    Task<IReadOnlyList<ShareFile>> ListAsync(string folder, string pattern, CancellationToken ct);

    /// <summary>The folders in a folder matching a pattern, as relative paths; empty when the folder is missing.</summary>
    /// <param name="folder">The folder.</param>
    /// <param name="pattern">The pattern.</param>
    /// <param name="ct">Stops the read.</param>
    Task<IReadOnlyList<string>> FoldersAsync(string folder, string pattern, CancellationToken ct);

    /// <summary>True when the file exists.</summary>
    /// <param name="relative">The path.</param>
    /// <param name="ct">Stops the read.</param>
    Task<bool> ExistsAsync(string relative, CancellationToken ct);

    /// <summary>Writes a file in a run folder, replacing what is there.</summary>
    /// <param name="relative">The path.</param>
    /// <param name="text">The text.</param>
    /// <param name="ct">Stops the write.</param>
    Task WriteAsync(string relative, string text, CancellationToken ct);

    /// <summary>Creates a file that must not exist and reads it back; a spool file is written once.</summary>
    /// <param name="relative">The path.</param>
    /// <param name="text">The text.</param>
    /// <param name="ct">Stops the write.</param>
    Task WriteNewAsync(string relative, string text, CancellationToken ct);

    /// <summary>Adds to the end of a file, making it if it is not there.</summary>
    /// <param name="relative">The path.</param>
    /// <param name="text">The text.</param>
    /// <param name="ct">Stops the write.</param>
    Task AppendAsync(string relative, string text, CancellationToken ct);

    /// <summary>Copies a local file into a run folder.</summary>
    /// <param name="from">The local path.</param>
    /// <param name="relative">The path on the share.</param>
    /// <param name="ct">Stops the copy.</param>
    Task CopyInAsync(string from, string relative, CancellationToken ct);
}

/// <summary>The mount the share is read through.</summary>
public interface IMount
{
    /// <summary>Null when the share reads fresh, repairing it once if it has to; otherwise the line saying why it does not.</summary>
    /// <param name="ct">Stops the check.</param>
    Task<string?> FreshAsync(CancellationToken ct);
}

/// <summary>The one-run-at-a-time lock.</summary>
public interface IRunLock
{
    /// <summary>The held lock, or null when another run holds it inside the ceiling.</summary>
    /// <param name="dayFolder">The day's folder, the parent of the run folders.</param>
    /// <param name="ct">Stops the attempt.</param>
    Task<IAsyncDisposable?> TakeAsync(string dayFolder, CancellationToken ct);
}

/// <summary>The desktop the browser runs on.</summary>
public interface IDesktop
{
    /// <summary>Null when Chrome is running or was started; otherwise the line saying why it is not.</summary>
    /// <param name="ct">Stops the check.</param>
    Task<string?> EnsureChromeAsync(CancellationToken ct);
}

/// <summary>One headless model run.</summary>
/// <param name="Arguments">The CLI's arguments.</param>
/// <param name="WorkingFolder">Where it runs.</param>
/// <param name="StreamFile">The real path the stream is kept at.</param>
/// <param name="Timeout">The wall-clock limit.</param>
public sealed record ClaudeRequest(IReadOnlyList<string> Arguments, string WorkingFolder, string StreamFile, TimeSpan Timeout);

/// <summary>How a model run ended.</summary>
/// <param name="ExitCode">The process's exit code.</param>
/// <param name="TimedOut">True when the program killed it at the limit.</param>
/// <param name="ResultLine">The stream's result line, or null.</param>
public sealed record ClaudeRun(int ExitCode, bool TimedOut, string? ResultLine);

/// <summary>The Claude CLI.</summary>
public interface IClaudeCli
{
    /// <summary>Runs the CLI to its end or the limit.</summary>
    /// <param name="request">The run.</param>
    /// <param name="ct">Stops the run.</param>
    Task<ClaudeRun> RunAsync(ClaudeRequest request, CancellationToken ct);
}

/// <summary>make_ref.py, the one place a link code is minted.</summary>
public interface ICodeMinter
{
    /// <summary>A new code, or null when the helper failed.</summary>
    /// <param name="person">The person, or the role when no name is known yet.</param>
    /// <param name="company">The company.</param>
    /// <param name="ct">Stops the mint.</param>
    Task<string?> MintAsync(string person, string company, CancellationToken ct);
}

/// <summary>check_aiisms.py, the proofread gate.</summary>
public interface IProseCheck
{
    /// <summary>True when the text comes back clean.</summary>
    /// <param name="text">The text.</param>
    /// <param name="ct">Stops the check.</param>
    Task<bool> CleanAsync(string text, CancellationToken ct);
}

/// <summary>Where a lane's usage is reported.</summary>
public interface IUsageSink
{
    /// <summary>Posts the document; the answer is the line for the DONE note, and a failure never fails the run.</summary>
    /// <param name="document">The usage document.</param>
    /// <param name="ct">Stops the post.</param>
    Task<string> ReportAsync(string document, CancellationToken ct);
}
