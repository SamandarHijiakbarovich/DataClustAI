using System.Collections.Concurrent;
using ExcelAiCategorizer.Models;

namespace ExcelAiCategorizer.Services;

public interface IJobStore
{
    void Add(CategorizationJob job);
    CategorizationJob? Get(Guid id);
    IReadOnlyList<CategorizationJob> Snapshot();
    void Remove(Guid id);
}

/// <summary>
/// Xotiradagi oddiy job registri. Bitta server nusxasi uchun yetarli.
/// Bir nechta serverga kengaytirilsa — Redis yoki SQL bilan almashtiriladi.
/// </summary>
public sealed class InMemoryJobStore : IJobStore
{
    private readonly ConcurrentDictionary<Guid, CategorizationJob> _jobs = new();

    public void Add(CategorizationJob job) => _jobs[job.Id] = job;

    public CategorizationJob? Get(Guid id) =>
        _jobs.TryGetValue(id, out var job) ? job : null;

    public IReadOnlyList<CategorizationJob> Snapshot() => _jobs.Values.ToList();

    public void Remove(Guid id) => _jobs.TryRemove(id, out _);
}
