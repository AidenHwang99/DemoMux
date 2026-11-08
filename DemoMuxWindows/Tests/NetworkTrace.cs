using System.Diagnostics.Tracing;
internal sealed class NetworkTrace : EventListener
{
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name is "System.Net.Sockets" or "System.Net.Security" or "System.Net.Http") EnableEvents(source, EventLevel.Informational);
    }
    protected override void OnEventWritten(EventWrittenEventArgs e) => Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss.fff} {e.EventSource.Name}/{e.EventName}: {string.Join(" | ", e.Payload?.Select(p => p?.ToString()) ?? [])}");
}
