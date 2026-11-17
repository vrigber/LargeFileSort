using System;
using System.Collections.Generic;
using LargeFileSort.Sorter.Models;
using LargeFileSort.Sorter.Planning;

namespace LargeFileSort.Sorter.Chunks;

internal sealed class ChunkSlots(SortPlan plan)
{
    private readonly object _gate = new();
    private readonly List<SortChunk> _all = [];
    private readonly Stack<SortChunk> _free = [];
    private readonly long _fixedBytes = ValidatePlan(plan);
    private int _held;
    private int _workers;

    public int PeakChunkCount { get; private set; }
    public int PeakWorkerCount { get; private set; }
    public long PeakEstimatedMemoryBytes { get; private set; }

    public SortChunk Acquire()
    {
        lock (_gate)
        {
            SortChunk chunk;
            if (_free.Count > 0) chunk = _free.Pop();
            else
            {
                if (_all.Count == plan.ChunkSlotCount)
                    throw new InvalidOperationException("The planned number of chunk slots has been reached.");
                chunk = new(plan.ChunkBudgetBytes);
                _all.Add(chunk);
            }
            _held++;
            PeakChunkCount = Math.Max(PeakChunkCount, _held);
            PeakEstimatedMemoryBytes = Math.Max(PeakEstimatedMemoryBytes, _fixedBytes + _held * plan.ChunkBudgetBytes);
            return chunk;
        }
    }

    public void Release(SortChunk chunk)
    {
        chunk.Release();
        lock (_gate)
        {
            _held--;
            _free.Push(chunk);
        }
    }

    public void StartWorker()
    {
        lock (_gate)
        {
            if (_workers == plan.WorkerCount) throw new InvalidOperationException("The planned worker limit has been reached.");
            _workers++;
            PeakWorkerCount = Math.Max(PeakWorkerCount, _workers);
        }
    }

    public void FinishWorker() { lock (_gate) _workers--; }

    public void Clear()
    {
        foreach (SortChunk chunk in _all) chunk.Release();
        _all.Clear();
        _free.Clear();
        _held = 0;
    }

    private static long ValidatePlan(SortPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.WorkerCount < 1 || plan.ChunkSlotCount != plan.WorkerCount + 1L
            || plan.ChunkBudgetBytes < 1 || plan.MaxRunCount < 1 || plan.InitialSegmentTargetBytes < 0)
            throw new ArgumentException("The chunk plan must provide W workers and W+1 usable slots.", nameof(plan));
        long fixedBytes = checked(plan.ReaderMemoryBytes + plan.WorkerCount * plan.WriterMemoryBytes
            + plan.ReadScratchBudgetBytes + plan.RunCatalogBudgetBytes + plan.ControlBudgetBytes);
        if (checked(fixedBytes + plan.ChunkSlotCount * plan.ChunkBudgetBytes) > plan.MemoryBudgetBytes
            || checked(2 * (MemoryEstimator.ArrayHeaderBytes + (long)MemoryEstimator.RunSlotBytes * (plan.MaxRunCount + 1L))) > plan.RunCatalogBudgetBytes)
            throw new ArgumentException("Chunk slots or run catalog exceed the planned RAM budget.", nameof(plan));
        return fixedBytes;
    }
}
