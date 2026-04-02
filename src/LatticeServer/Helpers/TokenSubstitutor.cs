using System.Text.RegularExpressions;

namespace LatticeServer.Helpers;

/// <summary>
/// Performs token substitution on raw entity JSON strings before protobuf parsing.
/// All tokens are replaced with their string values in-place using regex.
/// </summary>
public static class TokenSubstitutor
{
    // Server-scoped counter for <unique_number> tokens
    private static int _counter = 0;

    private static readonly Regex UuidRegex =
        new(@"<new_uuid>", RegexOptions.Compiled);

    private static readonly Regex UniqueNumberRegex =
        new(@"<unique_number>", RegexOptions.Compiled);

    private static readonly Regex NowRegex =
        new(@"<now>", RegexOptions.Compiled);

    private static readonly Regex NowPlusRegex =
        new(@"<now\+(\d+)s>", RegexOptions.Compiled);

    /// <summary>
    /// Substitutes all tokens in <paramref name="json"/> and returns the result.
    /// All <c>&lt;new_uuid&gt;</c> tokens get independent UUIDs.
    /// All <c>&lt;unique_number&gt;</c> tokens get independent incremented values.
    /// </summary>
    public static string Substitute(string json)
    {
        // Replace <now+Xs> first so <now> doesn't partially match
        json = NowPlusRegex.Replace(json, m =>
        {
            var seconds = int.Parse(m.Groups[1].Value);
            return FormatTimestamp(DateTime.UtcNow.AddSeconds(seconds));
        });

        json = NowRegex.Replace(json, _ => FormatTimestamp(DateTime.UtcNow));

        json = UuidRegex.Replace(json, _ => Guid.NewGuid().ToString());

        json = UniqueNumberRegex.Replace(json, _ =>
        {
            var n = Interlocked.Increment(ref _counter);
            return n.ToString("D6");
        });

        return json;
    }

    /// <summary>Formats a UTC DateTime as a protobuf-JSON timestamp string (RFC 3339).</summary>
    private static string FormatTimestamp(DateTime utc)
    {
        return utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    }
}
