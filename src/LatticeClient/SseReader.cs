using System.Runtime.CompilerServices;

namespace LatticeClient;

/// <summary>
/// Represents a single Server-Sent Event.
/// </summary>
internal record SseEvent(string EventType, string Data);

/// <summary>
/// Reads Server-Sent Events (SSE) from a stream.
/// </summary>
internal static class SseReader
{
    public static async IAsyncEnumerable<SseEvent> ReadEventsAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream);

        string? eventType = null;
        string? data = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            if (line == null)
                yield break; // stream ended

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                eventType = line[7..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data = line[6..];
            }
            else if (line.Length == 0 && eventType != null && data != null)
            {
                yield return new SseEvent(eventType, data);
                eventType = null;
                data = null;
            }
        }
    }
}
