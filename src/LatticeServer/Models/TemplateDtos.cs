namespace LatticeServer.Models;

public record TemplateListItem(
    string TemplateId,
    bool IsTaskable,
    int TickIntervalMs);

public record SpawnedInstanceInfo(
    string EntityId,
    string TemplateId);
