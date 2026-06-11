using FluentAssertions;
using PlugB.Builders;
using PlugB.Internal.Domain;
using PlugB.Internal.State;
using PlugB.Options;
using PlugB.Storage;
using Xunit;

namespace PlugB.Tests;

public class StateTests
{
    [Fact]
    public void SequenceManager_Should_Reset_To_Zero_And_Increment()
    {
        // Arrange
        var manager = new SequenceManager();

        // Act & Assert
        // simulate a few random messages
        manager.NextSeq();
        manager.NextSeq();

        // NBIRTH forces a reset to 0
        manager.ResetSeq();

        var seq1 = manager.NextSeq(); // NBIRTH
        var seq2 = manager.NextSeq(); // next message
        var seq3 = manager.NextSeq(); // next message

        seq1.Should().Be(0ul, "NBIRTH must always start with seq=0");
        seq2.Should().Be(1ul);
        seq3.Should().Be(2ul);
    }

    [Fact]
    public void SequenceManager_Should_Wrap_Seq_From_255_To_0()
    {
        // Arrange
        var manager = new SequenceManager();
        manager.ResetSeq();

        // Act: fast forward to 255
        for (int i = 0; i < 256; i++)
        {
            manager.NextSeq();
        }

        // 256th call (index 255) returned 255. The NEXT one must be 0 again.
        var wrappedSeq = manager.NextSeq();

        // Assert
        wrappedSeq.Should().Be(0ul, "Sequence must wrap around from 255 back to 0");
    }

    [Fact]
    public void SequenceManager_Should_Increment_BdSeq_And_Wrap()
    {
        // Arrange
        var manager = new SequenceManager();

        // Act
        var bdSeq1 = manager.NextBdSeq(); // first connection
        var bdSeq2 = manager.NextBdSeq(); // second connection (reconnect)

        // Assert
        bdSeq1.Should().Be(0ul, "First connection bdSeq must be 0");
        bdSeq2.Should().Be(1ul, "Second connection bdSeq must increment by 1");

        // fast forward to wrap around
        for (int i = 0; i < 254; i++)
        {
            manager.NextBdSeq();
        }

        var wrappedBdSeq = manager.NextBdSeq();
        wrappedBdSeq.Should().Be(0ul, "bdSeq must wrap around from 255 back to 0");
    }

    [Fact]
    public void StateParser_Should_Parse_Valid_Json()
    {
        var validJson = "{\"online\": true, \"timestamp\": 1629837492000}";
        var result = StateParser.Parse(validJson, null);

        result.Should().NotBeNull();
        result!.Online.Should().BeTrue();
        result.TimestampMs.Should().Be(1629837492000);
    }

    [Fact]
    public void StateParser_Should_Reject_Invalid_Or_Incomplete_Json()
    {
        var missingTimestamp = "{\"online\": true}";
        StateParser.Parse(missingTimestamp, null).Should().BeNull();

        var missingOnline = "{\"timestamp\": 1629837492000}";
        StateParser.Parse(missingOnline, null).Should().BeNull();

        var completelyBroken = "{ this is no json }";
        StateParser.Parse(completelyBroken, null).Should().BeNull();
    }

    [Theory]
    [InlineData(false, 0, true, 100, true)]  // not known -> always accept
    [InlineData(true, 100, true, 200, true)]  // newer timestamp -> accept
    [InlineData(true, 100, false, 200, true)]  // newer timestamp -> accept
    [InlineData(true, 100, false, 50, false)] // older timestamp -> reject
    [InlineData(true, 100, true, 50, false)] // older timestamp -> reject
    [InlineData(true, 100, true, 100, true)]  // identical timestamp AND online=true -> accept
    [InlineData(true, 100, false, 100, false)] // identical timestamp AND online=false -> reject
    public void PrimaryHostMonitor_Should_Apply_Staleness_Matrix(
        bool initiallyKnown, long initialTs,
        bool incomingOnline, long incomingTs,
        bool shouldAccept)
    {
        // Arrange
        var monitor = new PrimaryHostMonitor(null);
        if (initiallyKnown)
        {
            monitor.ProcessStateMessage(new StateMessage(true, initialTs));
        }

        // Act
        var message = new StateMessage(incomingOnline, incomingTs);
        monitor.ProcessStateMessage(message);

        // Assert
        if (shouldAccept)
        {
            monitor.CurrentState.Known.Should().BeTrue();
            monitor.CurrentState.LastTimestampMs.Should().Be(incomingTs);
            monitor.CurrentState.Online.Should().Be(incomingOnline);
        }
        else
        {
            // state should remain unchanged
            monitor.CurrentState.LastTimestampMs.Should().Be(initialTs);
        }
    }

    [Fact]
    public async Task InMemoryStore_Should_Apply_DropOldest_Eviction()
    {
        // Arrange
        var store = new InMemoryForwardStore(2, EvictionPolicy.DropOldest);
        var entry1 = new ForwardEntry("T1", SparkplugMessageType.DeviceData, MetricBuilder.Create("M1").WithValue(1).Build());
        var entry2 = new ForwardEntry("T2", SparkplugMessageType.DeviceData, MetricBuilder.Create("M2").WithValue(2).Build());
        var entry3 = new ForwardEntry("T3", SparkplugMessageType.DeviceData, MetricBuilder.Create("M3").WithValue(3).Build());

        bool overflowFired = false;
        store.BufferOverflow += (s, e) => overflowFired = true;

        // Act
        await store.EnqueueAsync(entry1, CancellationToken.None);
        await store.EnqueueAsync(entry2, CancellationToken.None);
        await store.EnqueueAsync(entry3, CancellationToken.None); // should push out entry1

        // Assert
        overflowFired.Should().BeTrue();
        (await store.CountAsync(CancellationToken.None)).Should().Be(2);

        var drained = new List<ForwardEntry>();
        await foreach (var e in store.DrainAsync(CancellationToken.None)) drained.Add(e);

        drained.Count.Should().Be(2);
        drained[0].TargetTopic.Should().Be("T2", "DropOldest must discard the oldest entry.");
        drained[1].TargetTopic.Should().Be("T3");
    }

    [Fact]
    public async Task PFileStore_Should_Survive_Restarts_And_Maintain_Order()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), "PlugB_TestStore_" + Guid.NewGuid().ToString());

        try
        {
            // App Run 1
            var store1 = new FileForwardStore(testDir, 1000, EvictionPolicy.DropOldest);
            var entry1 = new ForwardEntry("TopicA", SparkplugMessageType.DeviceData, MetricBuilder.Create("M1").WithValue(42).Build());
            var entry2 = new ForwardEntry("TopicB", SparkplugMessageType.DeviceData, MetricBuilder.Create("M2").WithValue(84).Build());

            await store1.EnqueueAsync(entry1, CancellationToken.None);
            await store1.EnqueueAsync(entry2, CancellationToken.None);

            (await store1.CountAsync(CancellationToken.None)).Should().Be(2);

            // App Run 2
            // create a new instance pointing to the same directory
            var store2 = new FileForwardStore(testDir, 1000, EvictionPolicy.DropOldest);

            // state should be recovered from disk
            (await store2.CountAsync(CancellationToken.None)).Should().Be(2);

            var drained = new List<ForwardEntry>();
            await foreach (var e in store2.DrainAsync(CancellationToken.None))
            {
                drained.Add(e);
            }

            // Assert
            drained.Count.Should().Be(2);
            drained[0].TargetTopic.Should().Be("TopicA", "Read must be in strict FIFO write order.");
            drained[0].Metric.Value.Should().Be(42);

            drained[1].TargetTopic.Should().Be("TopicB");
            drained[1].Metric.Value.Should().Be(84);

            // after draining, store should be empty and files deleted
            (await store2.CountAsync(CancellationToken.None)).Should().Be(0);
            Directory.GetFiles(testDir, "*.jsonl").Length.Should().Be(0);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }
}