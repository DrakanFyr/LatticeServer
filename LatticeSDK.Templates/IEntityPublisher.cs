namespace LatticeSDK.Templates;

/// <summary>
/// Injected by the server — lets the behavior publish entity position/state updates
/// without depending on the protobuf or ASP.NET packages.
/// </summary>
public interface IEntityPublisher
{
    /// <summary>
    /// Merges the supplied update fields onto the last-published entity and re-publishes it,
    /// refreshing the expiry time. Only non-null fields in <paramref name="update"/> are applied.
    /// </summary>
    void PublishEntityUpdate(EntityUpdate update);
}
