using System.Text.Json;

namespace Mimicraft.Recorder.Windows;

public sealed class EventLog
{
    private readonly object _gate = new();
    public string FilePath { get; }
    public EventLog(string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mimicraft", "Recorder", "Logs");
        Directory.CreateDirectory(directory);
        // Retain bounded log history. Only our own dated files are considered.
        foreach (var old in Directory.GetFiles(directory, "recorder-*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc).Skip(19))
            try { File.Delete(old); } catch (IOException) { }
        FilePath = Path.Combine(directory, $"recorder-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.jsonl");
    }
    public void Write(string eventId, object? details = null, string? recordingId = null, Exception? error = null)
    {
        var json = JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, level = error is null ? "Information" : "Error", eventId,
            processId = Environment.ProcessId, recordingId, details, exceptionType = error?.GetType().Name, message = error?.Message });
        lock (_gate)
        {
            try { File.AppendAllText(FilePath, json + Environment.NewLine); }
            catch (IOException) { System.Diagnostics.Trace.WriteLine(json); }
            catch (UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(json); }
        }
    }
}
