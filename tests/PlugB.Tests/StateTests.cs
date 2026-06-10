using FluentAssertions;
using PlugB.Internal.State;
using Xunit;

namespace PlugB.Tests;

public class StateTests
{
    [Fact]
    public void S1_SequenceManager_Should_Reset_To_Zero_And_Increment()
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
    public void S1_SequenceManager_Should_Wrap_Seq_From_255_To_0()
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
    public void S3_SequenceManager_Should_Increment_BdSeq_And_Wrap()
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
}