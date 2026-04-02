namespace LatticeSDK.Templates;

/// <summary>
/// Injected by the server — lets behaviors query the current position of any entity in the store.
/// </summary>
public interface IEntityQuerier
{
    /// <summary>
    /// Returns the last-known position of the entity with the given ID,
    /// or <c>null</c> if the entity does not exist or has no position data.
    /// </summary>
    EntityPosition? TryGetPosition(string entityId);
}
