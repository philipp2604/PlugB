using FluentAssertions;
using PlugB.Internal.State;

namespace PlugB.Tests;

public class HostSequenceTrackerTests
{
    [Fact]
    public void Tracker_Should_Allow_InOrder_Sequences_And_Wrap_At_256()
    {
        var tracker = new HostSequenceTracker();

        // Act
        tracker.ProcessNBirth("G1", "E1", bdSeq: 10);

        var res1 = tracker.ValidateSequence("G1", "E1", 1);
        var res2 = tracker.ValidateSequence("G1", "E1", 2);

        res1.Should().Be(SequenceValidationResult.Valid);
        res2.Should().Be(SequenceValidationResult.Valid);

        // fast forward to 255
        for (ulong i = 3; i < 256; i++)
        {
            tracker.ValidateSequence("G1", "E1", i).Should().Be(SequenceValidationResult.Valid);
        }

        // Must wrap to 0
        tracker.ValidateSequence("G1", "E1", 0).Should().Be(SequenceValidationResult.Valid);
        tracker.ValidateSequence("G1", "E1", 1).Should().Be(SequenceValidationResult.Valid);
    }

    [Fact]
    public void Tracker_Should_Detect_Sequence_Gap()
    {
        var tracker = new HostSequenceTracker();
        tracker.ProcessNBirth("G1", "E1", bdSeq: 10);

        // Act
        var result = tracker.ValidateSequence("G1", "E1", 2); // Expected 1, got 2

        // Assert
        result.Should().Be(SequenceValidationResult.SequenceGap);
    }

    [Fact]
    public void Tracker_Should_Detect_DataBeforeBirth()
    {
        var tracker = new HostSequenceTracker();

        // Act
        var result = tracker.ValidateSequence("G1", "UnknownEdge", 5);

        // Assert
        result.Should().Be(SequenceValidationResult.DataBeforeBirth);
    }

    [Fact]
    public void Tracker_Should_Correlate_Death_With_BdSeq()
    {
        var tracker = new HostSequenceTracker();
        tracker.ProcessNBirth("G1", "E1", bdSeq: 42);

        // Act & Assert
        tracker.ValidateNDeath("G1", "E1", 41).Should().Be(SequenceValidationResult.InvalidDeathBdSeq, "Old LWT from previous session should be ignored.");

        tracker.ValidateNDeath("G1", "E1", 42).Should().Be(SequenceValidationResult.Valid, "Matching bdSeq marks the death as valid.");
    }
}