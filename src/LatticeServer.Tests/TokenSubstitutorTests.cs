using System.Text.RegularExpressions;
using LatticeServer.Helpers;

namespace LatticeServer.Tests;

public class TokenSubstitutorTests
{
    // UUID v4 pattern (not strict version bits, just GUID format)
    private static readonly Regex GuidPattern =
        new(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase);

    // RFC 3339 UTC timestamp pattern
    private static readonly Regex TimestampPattern =
        new(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$");

    [Fact]
    public void NewUuid_IsReplacedWithGuid()
    {
        var result = TokenSubstitutor.Substitute("<new_uuid>");
        Assert.Matches(GuidPattern, result);
    }

    [Fact]
    public void MultipleNewUuid_AreIndependent()
    {
        var result = TokenSubstitutor.Substitute("<new_uuid> <new_uuid>");
        var parts = result.Split(' ');
        Assert.Equal(2, parts.Length);
        Assert.Matches(GuidPattern, parts[0]);
        Assert.Matches(GuidPattern, parts[1]);
        Assert.NotEqual(parts[0], parts[1]);
    }

    [Fact]
    public void UniqueNumber_IsSixDigitsZeroPadded()
    {
        var result = TokenSubstitutor.Substitute("<unique_number>");
        Assert.Matches(new Regex(@"^\d{6}$"), result);
    }

    [Fact]
    public void MultipleUniqueNumber_AreMonotonicallyIncreasing()
    {
        var result = TokenSubstitutor.Substitute("<unique_number> <unique_number>");
        var parts = result.Split(' ');
        Assert.Equal(2, parts.Length);
        var first = int.Parse(parts[0]);
        var second = int.Parse(parts[1]);
        Assert.True(second > first);
    }

    [Fact]
    public void Now_IsReplacedWithTimestamp()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var result = TokenSubstitutor.Substitute("<now>");
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.Matches(TimestampPattern, result);
        var parsed = DateTime.Parse(result, null, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.True(parsed >= before && parsed <= after);
    }

    [Fact]
    public void NowPlusSeconds_AddsCorrectOffset()
    {
        var before = DateTime.UtcNow.AddSeconds(299);
        var result = TokenSubstitutor.Substitute("<now+300s>");
        var after = DateTime.UtcNow.AddSeconds(301);

        Assert.Matches(TimestampPattern, result);
        var parsed = DateTime.Parse(result, null, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.True(parsed >= before && parsed <= after);
    }

    [Fact]
    public void NowPlusZeroSeconds_EqualsNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var result = TokenSubstitutor.Substitute("<now+0s>");
        var after = DateTime.UtcNow.AddSeconds(1);

        var parsed = DateTime.Parse(result, null, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.True(parsed >= before && parsed <= after);
    }

    [Fact]
    public void NoTokens_ReturnsOriginal()
    {
        const string input = """{"entityId":"abc","isLive":true}""";
        var result = TokenSubstitutor.Substitute(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void MixedTokens_AllSubstituted()
    {
        var result = TokenSubstitutor.Substitute(
            """{"entityId":"<new_uuid>","name":"UAV <unique_number>","expiryTime":"<now+300s>","updateTime":"<now>"}""");

        Assert.DoesNotContain("<new_uuid>", result);
        Assert.DoesNotContain("<unique_number>", result);
        Assert.DoesNotContain("<now+300s>", result);
        Assert.DoesNotContain("<now>", result);
    }

    [Fact]
    public void NowTokenDoesNotPartiallyMatchNowPlus()
    {
        // <now+300s> must not become <TIMESTAMP+300s>
        var result = TokenSubstitutor.Substitute("<now+300s>");
        Assert.Matches(TimestampPattern, result);
        Assert.DoesNotContain("+300s>", result);
    }

    [Fact]
    public void LargeOffset_IsHandledCorrectly()
    {
        var before = DateTime.UtcNow.AddSeconds(3599);
        var result = TokenSubstitutor.Substitute("<now+3600s>");
        var after = DateTime.UtcNow.AddSeconds(3601);

        var parsed = DateTime.Parse(result, null, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.True(parsed >= before && parsed <= after);
    }
}
