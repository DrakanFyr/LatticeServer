namespace LatticeServer.Models;

public record TemplateListItem(
    string TemplateId,
    bool IsTaskable,
    int TickIntervalMs,
    string? Category,
    string? DisplayName,
    bool HasCustomTaskTypes);

public record SpawnedInstanceInfo(
    string EntityId,
    string TemplateId);
