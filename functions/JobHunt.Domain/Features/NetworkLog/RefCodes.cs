using System.Security.Cryptography;

namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>
/// A link code the make_ref.py way (ADR-108): six characters from its alphabet, drawn until none of the codes already
/// in link-tokens.csv or its spool has it, and one spool row a code with make_ref.py's columns, so opens_report.py
/// folds the Function's codes exactly as it folds a lane's.
/// </summary>
public static class RefCodes
{
    /// <summary>make_ref.py's alphabet.</summary>
    public const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>make_ref.py's columns, its header line.</summary>
    public const string Header = "code,person,company,channel,role,thread,sent,lane";

    /// <summary>The folder make_ref.py spools to, which opens_report.py folds into <see cref="Tokens"/>.</summary>
    public const string Spool = "inbox/link-tokens";

    /// <summary>The folded file, which only opens_report.py writes.</summary>
    public const string Tokens = "link-tokens.csv";

    /// <summary>The lane every row the network log spools names.</summary>
    public const string Lane = "NetworkLogPass";

    /// <summary>A new code none of <paramref name="taken"/> holds.</summary>
    public static string New(ISet<string> taken)
    {
        while (true)
        {
            string code = string.Create(6, 0, (span, _) =>
            {
                for (int i = 0; i < span.Length; i++)
                {
                    span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
                }
            });
            if (taken.Add(code))
            {
                return code;
            }
        }
    }

    /// <summary>Every code in a link-tokens CSV's text: its first column, header set aside.</summary>
    public static IEnumerable<string> In(string csv) =>
        csv.Split('\n').Skip(1).Select(line => line.Split(',')[0].Trim().Trim('"', '﻿')).Where(code => code.Length > 0);
}
