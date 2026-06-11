using System.Runtime.CompilerServices;
using System.Text.Json;
using PlugB.Options;

namespace PlugB.Internal.State;

/// <summary>
/// A persistent Store-and-Forward queue using append-only segment files.
/// Survives process restarts by recovering state from disk.
/// </summary>
internal class FileForwardStore : IForwardStore
{
    private readonly string _storageDirectory;
    private readonly int _capacity;
    private readonly EvictionPolicy _eviction;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // store max 10,000 lines per segment file
    private const int EntriesPerSegment = 10_000;
    private int _currentCount;

    /// <inheritdoc />
    public event EventHandler<BufferOverflowInfo>? BufferOverflow;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileForwardStore"/> class.
    /// Automatically ensures the storage directory exists and calculates the current entry count.
    /// </summary>
    /// <param name="storageDirectory">The directory where segment files will be saved.</param>
    /// <param name="capacity">The maximum number of entries to store before applying the eviction policy.</param>
    /// <param name="eviction">The policy determining how to handle overflows.</param>
    public FileForwardStore(string storageDirectory, int capacity, EvictionPolicy eviction)
    {
        _storageDirectory = storageDirectory;
        _capacity = capacity;
        _eviction = eviction;

        if (!Directory.Exists(_storageDirectory))
        {
            Directory.CreateDirectory(_storageDirectory);
        }

        // init state from disk
        _currentCount = CalculateTotalEntries();
    }

    /// <inheritdoc />
    public async ValueTask EnqueueAsync(ForwardEntry entry, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_currentCount >= _capacity)
            {
                if (_eviction == EvictionPolicy.RejectNew)
                    throw new InvalidOperationException("File store is full (RejectNew policy).");

                if (_eviction == EvictionPolicy.DropOldest)
                {
                    DropOldestSegment();
                }
                else if (_eviction == EvictionPolicy.DropNewest)
                {
                    BufferOverflow?.Invoke(this, new BufferOverflowInfo(1));
                    return;
                }
            }

            var segmentFile = GetActiveWriteSegment();
            var dto = ForwardEntryDto.FromForwardEntry(entry);
            var jsonLine = JsonSerializer.Serialize(dto) + Environment.NewLine;

            await File.AppendAllTextAsync(segmentFile, jsonLine, ct);
            _currentCount++;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ForwardEntry> DrainAsync([EnumeratorCancellation] CancellationToken ct)
    {
        string[] files;
        await _lock.WaitAsync(ct);
        try
        {
            files = GetOrderedSegments();
        }
        finally
        {
            _lock.Release();
        }

        foreach (var file in files)
        {
            if (!File.Exists(file)) continue;

            string[] lines;
            try
            {
                lines = await File.ReadAllLinesAsync(file, ct);
            }
            catch (IOException)
            {
                continue; // skip if locked or missing
            }

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                ForwardEntryDto? dto = null;
                try { dto = JsonSerializer.Deserialize<ForwardEntryDto>(line); } catch { /* ignore corrupted line */ }

                if (dto != null)
                {
                    yield return dto.ToForwardEntry();
                }
            }

            // delete the segment after read
            await _lock.WaitAsync(ct);
            try
            {
                if (File.Exists(file)) File.Delete(file);
                _currentCount = Math.Max(0, _currentCount - lines.Length);
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<int> CountAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try { return _currentCount; }
        finally { _lock.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask ClearAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            foreach (var file in GetOrderedSegments())
            {
                File.Delete(file);
            }
            _currentCount = 0;
        }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// Retrieves all segment files ordered by their creation timestamp (oldest first).
    /// </summary>
    private string[] GetOrderedSegments()
    {
        return Directory.GetFiles(_storageDirectory, "segment_*.jsonl")
                        .OrderBy(f => f)
                        .ToArray();
    }

    /// <summary>
    /// Determines the active segment file to append new data to.
    /// Creates a new segment if the current one exceeds the maximum line limit.
    /// </summary>
    private string GetActiveWriteSegment()
    {
        var files = GetOrderedSegments();
        if (files.Length == 0) return CreateNewSegment();

        var lastFile = files.Last();
        // if file exceeds lines, roll over
        var lines = File.ReadLines(lastFile).Count();
        if (lines >= EntriesPerSegment)
        {
            return CreateNewSegment();
        }

        return lastFile;
    }

    /// <summary>
    /// Generates a filename for a new segment based on the current UTC timestamp.
    /// </summary>
    private string CreateNewSegment()
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return Path.Combine(_storageDirectory, $"segment_{timestamp}.jsonl");
    }

    /// <summary>
    /// Evicts the oldest segment file from disk to free up capacity.
    /// </summary>
    private void DropOldestSegment()
    {
        var files = GetOrderedSegments();
        if (files.Length > 0)
        {
            var linesDropped = File.ReadLines(files[0]).Count();
            File.Delete(files[0]);
            _currentCount = Math.Max(0, _currentCount - linesDropped);
            BufferOverflow?.Invoke(this, new BufferOverflowInfo(linesDropped));
        }
    }

    /// <summary>
    /// Calculates the total number of entries currently stored across all segments.
    /// </summary>
    private int CalculateTotalEntries()
    {
        int count = 0;
        foreach (var file in GetOrderedSegments())
        {
            count += File.ReadLines(file).Count();
        }
        return count;
    }
}