namespace LatticeServer.Models;

public record TemplateListItem(
    string TemplateId,
    bool IsTaskable,
    int TickIntervalMs,
    string? Category,
    string? DisplayName);

public record SpawnedInstanceInfo(
    string EntityId,
    string TemplateId);
