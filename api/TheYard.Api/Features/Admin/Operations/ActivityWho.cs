// Who a visitor-day was: a person, a scanner or crawler, or the site reading
// itself, and the summaries built on that split (the path to the resume, the
// referring hosts, the top paths). Its own file because it is the one rule the
// report, the days and the card all count by; Activity.cs lists the other parts.
using Kind = TheYard.Domain.Shared.Visitors.VisitorKind;

namespace TheYard.Api.Features.Admin.Operations;

// #region report
/// <summary>
/// Who a visitor-day was. Three kinds, each visitor-day exactly one of them,
/// so the three add up to the day's total:
///
/// <para>The site's own reads: a row kept under the self mark (one of the
/// site's tools, or App Service asking after the container), or a row from
/// the loopback address, which is this machine talking to itself. Every
/// loopback row on a day is one visitor-day, the machine, because older rows
/// kept the port each of App Service's requests came from in the token, which
/// made every one of them a new visitor: hundreds a day, one request each,
/// all of /index.html. Read from the network the row already keeps, so the
/// older rows read the same way as the rows written now.</para>
///
/// <para>Scanners and crawlers: a token whose every request looked like a
/// bot, by its agent or by what it asked for (Hits.LooksLikeABot).</para>
///
/// <para>People: everybody else. A token is the site's own if any of its
/// rows is, and a person if any store saw a request of theirs that did not
/// look like a bot, the rule the day's humans have always used.</para>
/// </summary>
public static class ActivityWho
{
    /// <summary>The name every loopback row on a day is counted under: one machine, one visitor-day.</summary>
    public const string Loopback = "loopback";

    /// <summary>How many visitor-days in these rows were people, scanners and the site itself.</summary>
    public static (int People, int Scanners, int Self) Count(IEnumerable<ActivityVisitor> rows)
    {
        var kinds = Tokens(rows);
        return (
            kinds.Count(entry => entry.Value == Kind.People),
            kinds.Count(entry => entry.Value == Kind.Scanners),
            kinds.Count(entry => entry.Value == Kind.Self));
    }

    /// <summary>Each visitor-day's name and kind, within one day's rows: the loopback rows folded into one name.</summary>
    public static Dictionary<string, Kind> Tokens(IEnumerable<ActivityVisitor> rows)
    {
        var kinds = new Dictionary<string, Kind>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            string name = NameOf(row);
            var kind = KindOf(row);
            kinds[name] = kinds.TryGetValue(name, out var seen) ? Stronger(seen, kind) : kind;
        }

        return kinds;
    }

    /// <summary>The day and name a row is counted under: the loopback rows on a day are one name.</summary>
    public static string NameOf(ActivityVisitor row) =>
        row.Day + "|" + (Hits.IsLoopbackNetwork(row.Network) ? Loopback : row.Visitor);

    /// <summary>The kind one row alone says it is: the site's own by its network, a scanner if every request looked like a bot, a person otherwise.</summary>
    public static Kind KindOf(ActivityVisitor row) => VisitorKinds.Of(Hits.IsSelfNetwork(row.Network), row.Requests, row.Bots);

    /// <summary>Which of two kinds a visitor-day keeps when its rows disagree: the site's own over everything, then a person over a scanner.</summary>
    private static Kind Stronger(Kind a, Kind b) => VisitorKinds.Stronger(a, b);

    /// <summary>
    /// The window, by kind and all together: visitor-days (summed over the
    /// days, as the graph sums them), requests, the paths asked for most, and
    /// the same per store. What the card shows under Visitors only is the
    /// people entry; under All traffic, the all entry.
    /// </summary>
    public static object Summary(IReadOnlyList<ActivityVisitor> rows, IReadOnlyList<string> stores)
    {
        var kinds = new Dictionary<string, Kind>(StringComparer.Ordinal);
        foreach (var day in rows.GroupBy(row => row.Day, StringComparer.Ordinal))
        {
            foreach (var (name, kind) in Tokens(day))
            {
                kinds[name] = kind;
            }
        }

        object Entry(Func<Kind, bool> chosen)
        {
            var theirs = rows.Where(row => chosen(kinds[NameOf(row)])).ToList();
            return new
            {
                visitor_days = theirs.Select(NameOf).Distinct(StringComparer.Ordinal).Count(),
                requests = theirs.Sum(row => row.Requests),
                top_paths = TopPaths(theirs),
                path = Path(theirs),
                sources = Sources(theirs),
                by_store = stores.Select(store =>
                {
                    var here = theirs.Where(row => row.Store == store).ToList();
                    return new
                    {
                        store,
                        visitor_days = here.Select(NameOf).Distinct(StringComparer.Ordinal).Count(),
                        requests = here.Sum(row => row.Requests),
                    };
                }).ToList(),
            };
        }

        return new
        {
            people = Entry(kind => kind == Kind.People),
            scanners = Entry(kind => kind == Kind.Scanners),
            self = Entry(kind => kind == Kind.Self),
            all = Entry(_ => true),
        };
    }

    /// <summary>
    /// The path to the resume, the four steps the site exists for, and
    /// the paths each is asked for by: the page itself (every address the site
    /// serves is the one document, kept as /index.html), the inventory's
    /// listing, About Steven, and the resume, which the page links as
    /// /api/docs/resume, the repository serves as /docs/resume.pdf, and a
    /// link the owner hands out opens at /resume, recorded by its door.
    /// </summary>
    public static readonly (string Step, string[] Paths)[] Steps =
    [
        ("site", ["/", "/index.html"]),
        ("inventory", ["/api/vehicles"]),
        ("author", ["/api/docs/author"]),
        ("resume", ["/api/docs/resume", "/docs/resume.pdf", "/resume.pdf", ResumeDoor.PathPrefix]),
    ];

    /// <summary>Whether a visitor-day asked for one of a step's paths; a path longer than / that ends in a slash counts everything under it.</summary>
    private static bool Asked(ActivityVisitor row, string[] paths) =>
        paths.Any(path => path.Length > 1 && path.EndsWith('/')
            ? row.Paths.Keys.Any(asked => asked.StartsWith(path, StringComparison.Ordinal))
            : row.Paths.ContainsKey(path));

    /// <summary>How many of these visitor-days asked for each step, in the order the steps are walked.</summary>
    public static List<object> Path(IReadOnlyList<ActivityVisitor> rows) =>
        Steps.Select(step => (object)new
        {
            step = step.Step,
            visitor_days = rows
                .Where(row => Asked(row, step.Paths))
                .Select(NameOf)
                .Distinct(StringComparer.Ordinal)
                .Count(),
        }).ToList();

    /// <summary>
    /// The hosts that linked here, each counted in visitor-days: a
    /// visitor-day that arrived from two places counts under both, once each.
    /// Older rows, written before sources were kept, have none and add nothing.
    /// </summary>
    public static List<object> Sources(IReadOnlyList<ActivityVisitor> rows)
    {
        var days = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            foreach (var host in (row.Sources ?? new Dictionary<string, int>()).Keys)
            {
                if (!days.TryGetValue(host, out var names))
                {
                    days[host] = names = new HashSet<string>(StringComparer.Ordinal);
                }

                names.Add(NameOf(row));
            }
        }

        return days.OrderByDescending(entry => entry.Value.Count).ThenBy(entry => entry.Key, StringComparer.Ordinal).Take(12)
            .Select(entry => (object)new { host = entry.Key, visitor_days = entry.Value.Count }).ToList();
    }

    // #region doors
    /// <summary>
    /// The resume's opens by the door the link named (ADR: The resume has an address): for
    /// each door, and for "site", the resume opened from the site itself with no door, how
    /// many visitor-days opened it and how many requests that was, people only. A door is
    /// one of a handful of words, so nothing here says anything about who opened it.
    /// </summary>
    public static List<object> Doors(IReadOnlyList<ActivityVisitor> rows)
    {
        var kinds = Tokens(rows);
        var people = rows.Where(row => kinds[NameOf(row)] == Kind.People).ToList();
        var doors = ResumeDoor.Doors.Order(StringComparer.Ordinal).Append("other")
            .Select(door => (door, paths: new[] { ResumeDoor.PathPrefix + door }))
            .Append((door: "site", paths: new[] { "/api/docs/resume", "/docs/resume.pdf", "/resume.pdf" }));
        return doors.Select(entry =>
        {
            var opened = people.Where(row => entry.paths.Any(row.Paths.ContainsKey)).ToList();
            return (object)new
            {
                door = entry.door,
                visitor_days = opened.Select(NameOf).Distinct(StringComparer.Ordinal).Count(),
                requests = opened.Sum(row => entry.paths.Sum(path => row.Paths.GetValueOrDefault(path))),
            };
        }).ToList();
    }
    // #endregion doors

    /// <summary>The twelve paths these rows asked for most, with their request counts, ties broken by path.</summary>
    private static List<object> TopPaths(IEnumerable<ActivityVisitor> rows)
    {
        var paths = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            foreach (var (path, count) in row.Paths)
            {
                paths[path] = paths.GetValueOrDefault(path) + count;
            }
        }

        return paths.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key, StringComparer.Ordinal).Take(12)
            .Select(entry => (object)new { path = entry.Key, requests = entry.Value }).ToList();
    }
}
// #endregion report
