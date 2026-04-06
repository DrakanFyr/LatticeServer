using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace LatticeServer.Helpers;

/// <summary>
/// Converts between protobuf messages and JSON strings using Google.Protobuf's built-in JSON support.
/// Uses camelCase field names to match the Lattice REST API convention.
/// Supports dynamic registration of custom task proto types from template behavior DLLs.
/// </summary>
public static class ProtobufJsonConverter
{
    private static readonly FileDescriptor[] BaseFileDescriptors =
    [
        Anduril.Tasks.V2.IsrPubReflection.Descriptor,
        Anduril.Tasks.V2.ManeuverPubReflection.Descriptor,
        Anduril.Tasks.V2.StrikePubReflection.Descriptor,
        Anduril.Tasks.V2.CommonPubReflection.Descriptor,
        Anduril.Tasks.V2.ObjectivePubReflection.Descriptor,
        Anduril.Tasks.V2.CatalogPubReflection.Descriptor,
    ];

    private static readonly object _rebuildLock = new();
    private static readonly Dictionary<string, IReadOnlyList<MessageDescriptor>> _templateDescriptors = new();

    private static TypeRegistry _registry = null!;
    private static JsonFormatter _formatter = null!;
    private static JsonParser _parser = null!;

    static ProtobufJsonConverter()
    {
        RebuildRegistry();
    }

    public static string ToJson(IMessage message)
    {
        return _formatter.Format(message);
    }

    public static T FromJson<T>(string json) where T : IMessage<T>, new()
    {
        return _parser.Parse<T>(json);
    }

    public static void RegisterTypes(string templateId, IEnumerable<MessageDescriptor> descriptors)
    {
        lock (_rebuildLock)
        {
            _templateDescriptors[templateId] = descriptors.ToList();
            RebuildRegistry();
        }
    }

    public static void UnregisterTypes(string templateId)
    {
        lock (_rebuildLock)
        {
            if (_templateDescriptors.Remove(templateId))
                RebuildRegistry();
        }
    }

    private static void RebuildRegistry()
    {
        var customFiles = _templateDescriptors.Values
            .SelectMany(list => list)
            .Select(d => d.File)
            .Distinct();

        var allFiles = BaseFileDescriptors.Concat(customFiles).ToArray();
        _registry = TypeRegistry.FromFiles(allFiles);
        _formatter = new JsonFormatter(new JsonFormatter.Settings(true)
            .WithFormatDefaultValues(false)
            .WithTypeRegistry(_registry));
        _parser = new JsonParser(JsonParser.Settings.Default
            .WithIgnoreUnknownFields(true)
            .WithTypeRegistry(_registry));
    }
}
