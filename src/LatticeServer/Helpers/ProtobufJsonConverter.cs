using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace LatticeServer.Helpers;

/// <summary>
/// Converts between protobuf messages and JSON strings using Google.Protobuf's built-in JSON support.
/// Uses camelCase field names to match the Lattice REST API convention.
/// </summary>
public static class ProtobufJsonConverter
{
    private static readonly TypeRegistry TaskTypeRegistry = TypeRegistry.FromFiles(
        Anduril.Tasks.V2.IsrPubReflection.Descriptor,
        Anduril.Tasks.V2.ManeuverPubReflection.Descriptor,
        Anduril.Tasks.V2.StrikePubReflection.Descriptor,
        Anduril.Tasks.V2.CommonPubReflection.Descriptor,
        Anduril.Tasks.V2.ObjectivePubReflection.Descriptor,
        Anduril.Tasks.V2.CatalogPubReflection.Descriptor
    );

    private static readonly JsonFormatter Formatter = new(new JsonFormatter.Settings(true)
        .WithFormatDefaultValues(false)
        .WithTypeRegistry(TaskTypeRegistry));

    private static readonly JsonParser Parser = new(JsonParser.Settings.Default
        .WithIgnoreUnknownFields(true)
        .WithTypeRegistry(TaskTypeRegistry));

    public static string ToJson(IMessage message)
    {
        return Formatter.Format(message);
    }

    public static T FromJson<T>(string json) where T : IMessage<T>, new()
    {
        return Parser.Parse<T>(json);
    }
}
