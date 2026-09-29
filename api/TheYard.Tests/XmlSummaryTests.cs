using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// Every public class and record in the solution says what it is for in an XML
/// summary, and a positional record names each of its parameters (ADR: The
/// composition root, split by job). The generated migrations are the one named
/// exception: a tool writes them, and they are read by the tool.
/// </summary>
public class XmlSummaryTests
{
    // #region xml-summary-rule
    /// <summary>Files a tool generates, excused by name.</summary>
    private static readonly HashSet<string> Generated = new(StringComparer.Ordinal)
    {
        "20260903053709_InitialCreate.cs",
        "20260903053709_InitialCreate.Designer.cs",
        "20260903084932_AccountsAndPerUserBids.cs",
        "20260903084932_AccountsAndPerUserBids.Designer.cs",
        "20260903110414_TypesKeysAndConcurrency.cs",
        "20260903110414_TypesKeysAndConcurrency.Designer.cs",
        "20260903112910_NarrowIdentityKeys.cs",
        "20260903112910_NarrowIdentityKeys.Designer.cs",
        "20260913145859_SiteActivity.cs",
        "20260913145859_SiteActivity.Designer.cs",
        "20260924180000_ActivitySources.cs",
        "20260924180000_ActivitySources.Designer.cs",
        "YardDbContextModelSnapshot.cs",
    };

    private static readonly Regex Declaration = new(
        @"^\s*public\s+(?:(?:sealed|static|abstract|partial|readonly|required|file)\s+)*(class|record)(?:\s+(?:class|struct))?\s+(\w+)",
        RegexOptions.Compiled);

    [Fact]
    public void Every_public_class_and_record_has_a_summary_and_every_positional_parameter_is_named()
    {
        string api = Path.Combine(Repo.Root(), "api");
        var wrong = new List<string>();
        int checkedTypes = 0;
        foreach (string path in Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            string name = Path.GetFileName(path);
            bool excused = Generated.Contains(name);
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                Match declared = Declaration.Match(lines[i]);
                if (!declared.Success)
                {
                    continue;
                }

                checkedTypes++;
                if (excused)
                {
                    continue;
                }

                string where = $"{Path.GetRelativePath(api, path).Replace('\\', '/')}:{i + 1} {declared.Groups[2].Value}";
                string doc = DocAbove(lines, i);
                if (!doc.Contains("<summary>", StringComparison.Ordinal))
                {
                    wrong.Add($"{where} has no summary");
                    continue;
                }

                if (declared.Groups[1].Value == "record")
                {
                    foreach (string parameter in PositionalParameters(lines, i, declared.Groups[2].Value))
                    {
                        if (!doc.Contains($"<param name=\"{parameter}\">", StringComparison.Ordinal))
                        {
                            wrong.Add($"{where} does not name its parameter {parameter}");
                        }
                    }
                }
            }
        }

        // A scan that reads nothing passes, which is the failure this exists to catch.
        Assert.True(checkedTypes > 300, $"only {checkedTypes} public classes and records were found");
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    /// <summary>Every excused file still exists, so the list cannot outlive what it excuses.</summary>
    [Fact]
    public void Every_excused_file_exists()
    {
        string migrations = Path.Combine(Repo.Root(), "api", "TheYard.Migrations.Sqlite");
        var missing = Generated.Where(name => !File.Exists(Path.Combine(migrations, name))).ToList();
        Assert.True(missing.Count == 0, "excused but not there: " + string.Join(", ", missing));
    }

    private static string DocAbove(string[] lines, int declaration)
    {
        int j = declaration - 1;
        while (j >= 0 && lines[j].TrimStart().StartsWith('['))
        {
            j--;
        }

        var doc = new List<string>();
        while (j >= 0 && lines[j].TrimStart().StartsWith("///", StringComparison.Ordinal))
        {
            doc.Insert(0, lines[j].Trim());
            j--;
        }

        return string.Join('\n', doc);
    }

    private static List<string> PositionalParameters(string[] lines, int declaration, string typeName)
    {
        string signature = lines[declaration];
        int after = signature.IndexOf(typeName, StringComparison.Ordinal) + typeName.Length;
        if (!signature[after..].TrimStart().StartsWith('('))
        {
            return [];
        }

        int k = declaration;
        while (signature.Count(c => c == '(') > signature.Count(c => c == ')') && k + 1 < lines.Length)
        {
            k++;
            signature += "\n" + lines[k];
        }

        string rest = Regex.Replace(signature[after..], @"\[[^\]]*\]", "");
        int open = rest.IndexOf('(');
        int close = rest.LastIndexOf(')');
        if (open < 0 || close <= open)
        {
            return [];
        }

        var names = new List<string>();
        int depth = 0;
        var current = new System.Text.StringBuilder();
        foreach (char c in rest[(open + 1)..close] + ",")
        {
            if (c is '<' or '(' or '[')
            {
                depth++;
            }
            else if (c is '>' or ')' or ']')
            {
                depth--;
            }

            if (c == ',' && depth == 0)
            {
                string parameter = current.ToString().Split('=')[0].Trim();
                if (parameter.Length > 0)
                {
                    names.Add(parameter.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)[^1]);
                }

                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        return names;
    }
    // #endregion xml-summary-rule
}
