using Google.Protobuf.Reflection;

namespace LatticeSDK.Templates;

/// <summary>
/// Implemented by any class in behavior.dll that provides custom protobuf message types
/// for task specifications. The server registers these types so that google.protobuf.Any
/// JSON serialization works correctly for custom task specs.
///
/// Typically implemented alongside ITaskableEntity on the behavior class, but can also
/// exist independently (e.g., for templates whose tasks are executed externally).
///
/// Implementors must have a public parameterless constructor (required by Activator.CreateInstance).
/// </summary>
public interface ICustomTaskTypes
{
    /// <summary>
    /// Returns the MessageDescriptors for all custom task specification types this
    /// template can receive. Each descriptor's type URL becomes a valid value for
    /// a Task.specification @type field.
    /// </summary>
    IEnumerable<MessageDescriptor> GetTaskDescriptors();
}
