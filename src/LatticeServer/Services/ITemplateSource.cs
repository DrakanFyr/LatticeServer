using LatticeServer.Models;

namespace LatticeServer.Services;

public interface ITemplateSource
{
    /// <summary>
    /// Starts discovery. Calls onEvent for each discovered template at startup,
    /// then for ongoing changes. The callback is awaited before processing the next event.
    /// </summary>
    Task StartAsync(Func<TemplateSourceEvent, Task> onEvent, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

public abstract record TemplateSourceEvent(string TemplateId);
public record TemplateAdded(string TemplateId, RawTemplateFiles Files) : TemplateSourceEvent(TemplateId);
public record TemplateRemoved(string TemplateId) : TemplateSourceEvent(TemplateId);
public record TemplateUpdated(string TemplateId, RawTemplateFiles Files) : TemplateSourceEvent(TemplateId);
