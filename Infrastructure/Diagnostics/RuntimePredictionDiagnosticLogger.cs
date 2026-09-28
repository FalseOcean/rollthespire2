using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using RolltheSpire2.Core.Diagnostics;

namespace RolltheSpire2.Infrastructure.Diagnostics;

/// <summary>
/// Bounded, non-blocking JSON Lines writer. Producers only enqueue immutable DTOs;
/// all directory and file I/O occurs on one background task. Logging failure is
/// isolated from prediction and is exposed only through LastFailureCode.
/// </summary>
public sealed class RuntimePredictionDiagnosticLogger : IRuntimePredictionDiagnosticSink, IAsyncDisposable
{
    private sealed record PendingEvent(RuntimePredictionDiagnosticEvent Event, bool Force);

    private const long MaximumFileBytes = 5L * 1024L * 1024L;
    private const int MaximumFiles = 10;
    private readonly Channel<PendingEvent> _queue;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _writerTask;
    private readonly JsonSerializerOptions _jsonOptions;
    private int _enabled;
    private int _verbosity = (int)RuntimePredictionDiagnosticLevel.Info;
    private string _latestLogPath = string.Empty;
    private string _lastFailureCode = string.Empty;
    private int _fileSequence;

    public RuntimePredictionDiagnosticLogger(
        string logDirectory,
        bool enabled = false,
        RuntimePredictionDiagnosticLevel verbosity = RuntimePredictionDiagnosticLevel.Info)
    {
        if (string.IsNullOrWhiteSpace(logDirectory))
            throw new ArgumentException("Diagnostic log directory is required.", nameof(logDirectory));
        LogDirectory = Path.GetFullPath(logDirectory);
        _enabled = enabled ? 1 : 0;
        _verbosity = (int)verbosity;
        _queue = Channel.CreateBounded<PendingEvent>(new BoundedChannelOptions(4096)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
        _writerTask = Task.Run(WriterLoopAsync, CancellationToken.None);
    }

    public bool Enabled => Volatile.Read(ref _enabled) != 0;
    public RuntimePredictionDiagnosticLevel Verbosity =>
        (RuntimePredictionDiagnosticLevel)Volatile.Read(ref _verbosity);
    public string LogDirectory { get; }
    public string LatestLogPath => Volatile.Read(ref _latestLogPath) ?? string.Empty;
    public string LastFailureCode => Volatile.Read(ref _lastFailureCode) ?? string.Empty;

    public void Configure(bool enabled, RuntimePredictionDiagnosticLevel verbosity)
    {
        Volatile.Write(ref _verbosity, (int)verbosity);
        Volatile.Write(ref _enabled, enabled ? 1 : 0);
    }

    public bool TryWrite(RuntimePredictionDiagnosticEvent diagnosticEvent, bool force = false)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(diagnosticEvent);
            if (!force)
            {
                if (!Enabled || diagnosticEvent.Level > Verbosity)
                {
                    return false;
                }
            }
            bool queued = _queue.Writer.TryWrite(new PendingEvent(diagnosticEvent, force));
            if (!queued)
            {
                Volatile.Write(ref _lastFailureCode, "DiagnosticQueueFull");
            }
            return queued;
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastFailureCode, "DiagnosticQueueFailed:" + ex.GetType().Name);
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        _lifetime.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await _writerTask.ConfigureAwait(false);
        }
        catch
        {
            // Diagnostic shutdown must never escape into game shutdown.
        }
        _lifetime.Dispose();
    }

    private async Task WriterLoopAsync()
    {
        StreamWriter? writer = null;
        long bytesWritten = 0;
        try
        {
            await foreach (PendingEvent pending in _queue.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                if (!pending.Force && (!Enabled || pending.Event.Level > Verbosity))
                {
                    continue;
                }

                string line;
                try
                {
                    line = JsonSerializer.Serialize(pending.Event, _jsonOptions);
                }
                catch (Exception ex)
                {
                    Volatile.Write(ref _lastFailureCode, "DiagnosticSerializationFailed:" + ex.GetType().Name);
                    continue;
                }

                int lineBytes = Encoding.UTF8.GetByteCount(line) + 1;
                if (writer is null || bytesWritten + lineBytes > MaximumFileBytes)
                {
                    if (writer is not null)
                    {
                        await writer.FlushAsync().ConfigureAwait(false);
                        await writer.DisposeAsync().ConfigureAwait(false);
                    }
                    writer = await CreateWriterAsync().ConfigureAwait(false);
                    bytesWritten = 0;
                }

                await writer.WriteLineAsync(line).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
                bytesWritten += lineBytes;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastFailureCode, "DiagnosticWriterFailed:" + ex.GetType().Name);
        }
        finally
        {
            if (writer is not null)
            {
                try
                {
                    await writer.FlushAsync().ConfigureAwait(false);
                    await writer.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }
    }

    private Task<StreamWriter> CreateWriterAsync()
    {
        Directory.CreateDirectory(LogDirectory);
        DeleteOldFilesBestEffort();
        string timestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
        int sequence = Interlocked.Increment(ref _fileSequence);
        string path = Path.Combine(LogDirectory, $"rt2_diagnostic_{timestamp}_{sequence:D2}.jsonl");
        var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 16 * 1024,
            useAsync: true);
        Volatile.Write(ref _latestLogPath, path);
        return Task.FromResult(new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)));
    }

    private void DeleteOldFilesBestEffort()
    {
        try
        {
            FileInfo[] files = new DirectoryInfo(LogDirectory)
                .GetFiles("rt2_diagnostic_*.jsonl", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();
            foreach (FileInfo file in files.Skip(Math.Max(0, MaximumFiles - 1)))
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                }
            }
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _lastFailureCode, "DiagnosticRetentionFailed:" + ex.GetType().Name);
        }
    }
}
