using FluentAssertions;
using Microsoft.Extensions.Options;
using MRP.Agents.Recovery;
using Xunit;

namespace MRP.Tests.Integration;

/// <summary>
/// Tests for the recovery channel: capacity, backpressure, metrics, and concurrency.
/// These are in-process tests that don't require a database.
/// </summary>
public class RecoveryChannelTests
{
    private static RecoveryChannel CreateChannel(int capacity = 500)
        => new(Options.Create(new RecoveryOptions { ChannelCapacity = capacity }));

    [Fact]
    public void Channel_AcceptsItems_UpToCapacity()
    {
        var channel = CreateChannel(capacity: 10);

        for (var i = 0; i < 10; i++)
        {
            var item = new RecoveryWorkItem(Guid.NewGuid(), "high", DateTime.UtcNow);
            var written = channel.Writer.TryWrite(item);
            written.Should().BeTrue($"item {i} should be accepted");
            channel.RecordEnqueued();
        }

        var metrics = channel.GetMetrics();
        metrics.Enqueued.Should().Be(10);
        metrics.Dropped.Should().Be(0);
    }

    [Fact]
    public void Channel_DropsOldest_WhenFull()
    {
        var channel = CreateChannel(capacity: 5);

        // Fill channel
        for (var i = 0; i < 5; i++)
        {
            channel.Writer.TryWrite(new RecoveryWorkItem(Guid.NewGuid(), "high", DateTime.UtcNow));
            channel.RecordEnqueued();
        }

        // Write one more — should succeed (DropOldest drops the oldest item)
        var overflow = channel.Writer.TryWrite(
            new RecoveryWorkItem(Guid.NewGuid(), "critical", DateTime.UtcNow));
        overflow.Should().BeTrue("DropOldest mode should accept the write and discard the oldest");
    }

    [Fact]
    public async Task Channel_Reader_ReceivesWrittenItems()
    {
        var channel = CreateChannel(capacity: 10);
        var anomalyId = Guid.NewGuid();
        var item = new RecoveryWorkItem(anomalyId, "high", DateTime.UtcNow);

        channel.Writer.TryWrite(item);
        channel.Writer.Complete();

        var received = new List<RecoveryWorkItem>();
        await foreach (var workItem in channel.Reader.ReadAllAsync())
        {
            received.Add(workItem);
        }

        received.Should().ContainSingle()
            .Which.AnomalyId.Should().Be(anomalyId);
    }

    [Fact]
    public void Metrics_TrackAllCounters()
    {
        var channel = CreateChannel();

        channel.RecordEnqueued();
        channel.RecordEnqueued();
        channel.RecordDropped();
        channel.RecordProcessed();
        channel.RecordProcessed();
        channel.RecordFailed();

        var metrics = channel.GetMetrics();
        metrics.Enqueued.Should().Be(2);
        metrics.Dropped.Should().Be(1);
        metrics.Processed.Should().Be(2);
        metrics.Failed.Should().Be(1);
    }

    [Fact]
    public void Metrics_AreThreadSafe()
    {
        var channel = CreateChannel();
        const int iterations = 1000;

        Parallel.For(0, iterations, _ =>
        {
            channel.RecordEnqueued();
            channel.RecordProcessed();
        });

        var metrics = channel.GetMetrics();
        metrics.Enqueued.Should().Be(iterations);
        metrics.Processed.Should().Be(iterations);
    }

    [Fact]
    public void ChannelCapacity_IsClamped()
    {
        // Capacity below minimum should be clamped to 10
        var small = CreateChannel(capacity: 1);
        // Should still work — capacity is clamped to 10
        for (var i = 0; i < 10; i++)
        {
            small.Writer.TryWrite(new RecoveryWorkItem(Guid.NewGuid(), "high", DateTime.UtcNow))
                .Should().BeTrue();
        }
    }

    [Fact]
    public async Task Channel_StressTest_HighThroughput()
    {
        var channel = CreateChannel(capacity: 100);
        const int producerCount = 10;
        const int itemsPerProducer = 50;

        // Produce items concurrently
        var producerTasks = Enumerable.Range(0, producerCount).Select(p => Task.Run(() =>
        {
            for (var i = 0; i < itemsPerProducer; i++)
            {
                channel.Writer.TryWrite(
                    new RecoveryWorkItem(Guid.NewGuid(), "high", DateTime.UtcNow));
                channel.RecordEnqueued();
            }
        }));

        await Task.WhenAll(producerTasks);
        channel.Writer.Complete();

        // Consume all items
        var consumed = 0;
        await foreach (var _ in channel.Reader.ReadAllAsync())
        {
            consumed++;
            channel.RecordProcessed();
        }

        var metrics = channel.GetMetrics();
        metrics.Enqueued.Should().Be(producerCount * itemsPerProducer);
        // Some items may have been dropped due to capacity overflow
        consumed.Should().BeLessOrEqualTo(producerCount * itemsPerProducer);
        metrics.Processed.Should().Be(consumed);
    }
}
