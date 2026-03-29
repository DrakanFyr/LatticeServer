using Google.Protobuf;

namespace LatticeServer.Helpers;

/// <summary>
/// Converts between protobuf messages and JSON strings using Google.Protobuf's built-in JSON support.
/// Uses camelCase field names to match the Lattice REST API convention.
/// </summary>
public static class ProtobufJsonConverter
{
    private static readonly JsonFormatter Formatter = new(new JsonFormatter.Settings(true)
        .WithFormatDefaultValues(false));

    private static readonly JsonParser Parser = new(JsonParser.Settings.Default
        .WithIgnoreUnknownFields(true));

    public static string ToJson(IMessage message)
    {
        return Formatter.Format(message);
    }

    public static T FromJson<T>(string json) where T : IMessage<T>, new()
    {
        return Parser.Parse<T>(json);
    }
}
