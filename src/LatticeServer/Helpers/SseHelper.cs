using System.Text;
using Google.Protobuf;

namespace LatticeServer.Helpers;

/// <summary>
/// Writes Server-Sent Events (SSE) to an HTTP response stream.
/// </summary>
public static class SseHelper
{
    public static void SetSseHeaders(HttpResponse response)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
    }

    public static async Task WriteEventAsync(HttpResponse response, string eventType, string data, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append("event: ").Append(eventType).Append('\n');
        sb.Append("data: ").Append(data).Append('\n');
        sb.Append('\n');

        await response.WriteAsync(sb.ToString(), ct);
        await response.Body.FlushAsync(ct);
    }

    public static async Task WriteProtobufEventAsync(HttpResponse response, string eventType, IMessage message, CancellationToken ct)
    {
        var json = ProtobufJsonConverter.ToJson(message);
        await WriteEventAsync(response, eventType, json, ct);
    }

    public static async Task WriteHeartbeatAsync(HttpResponse response, CancellationToken ct)
    {
        var timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow);
        var json = $"{{\"timestamp\":\"{timestamp.ToDateTime():O}\"}}";
        await WriteEventAsync(response, "heartbeat", json, ct);
    }
}
