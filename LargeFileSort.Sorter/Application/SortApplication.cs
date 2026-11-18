using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LargeFileSort.Sorter.Chunks;
using LargeFileSort.Sorter.Merging;
using LargeFileSort.Sorter.Models;
using LargeFileSort.Sorter.Storage;
using Microsoft.Extensions.Logging;

namespace LargeFileSort.Sorter.Application;

internal sealed class SortApplication(
    ChunkPipeline pipeline,
    MergeCoordinator merger,
    ResultPublisher publisher,
    SortResult statistics,
    ILogger<SortApplication> logger)
{
    public async Task<SortResult> RunAsync(CancellationToken cancellationToken)
    {
        Stopwatch total = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Reading and sorting chunks.");
        ChunkPipelineResult chunks = await pipeline.RunAsync(cancellationToken);
        if (!chunks.OutputCompleted)
        {
            logger.LogInformation("Merging sorted runs.");
            await merger.MergeAsync(chunks.Runs, cancellationToken);
        }

        var counts = publisher.GetCompletedCounts();
        if (counts.RecordCount != statistics.ReadLineCount - statistics.SkippedLineCount)
            throw new InvalidDataException("Output record count does not match the number of valid input records.");
        statistics.WrittenLineCount = counts.RecordCount;
        statistics.WrittenByteCount = counts.ByteCount;

        logger.LogInformation("Publishing result.");
        cancellationToken.ThrowIfCancellationRequested();
        Stopwatch publication = Stopwatch.StartNew();
        publisher.Publish();
        publication.Stop();
        statistics.PublicationTime = publication.Elapsed;
        logger.LogDebug("Result published.");

        try
        {
            using Process process = Process.GetCurrentProcess();
            long peak = process.PeakWorkingSet64;
            statistics.PeakWorkingSetBytes = peak > 0 ? peak : null;
        }
        catch (Exception exception) when (exception is Win32Exception or NotSupportedException or InvalidOperationException)
        {
            statistics.PeakWorkingSetBytes = null;
            logger.LogDebug(exception, "Process peak working set is unavailable.");
        }
        total.Stop();
        statistics.TotalTime = statistics.PreparationTime + total.Elapsed;
        return statistics;
    }
}
