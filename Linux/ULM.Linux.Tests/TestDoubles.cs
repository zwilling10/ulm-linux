using System.Collections.Generic;
using ULM.Core.Models;
using ULM.Core.Services;

namespace ULM.Linux.Tests
{
    internal sealed class FakeIsoDatabaseService : IIsoDatabaseService
    {
        private readonly List<IsoEntry> _entries = new();

        public IReadOnlyList<IsoEntry> Entries => _entries;
        public int Count => _entries.Count;
        public int SaveCount { get; private set; }

        public void Load() { }
        public void Save() => SaveCount++;
        public void SaveFilenames() { }
        public void Add(IsoEntry entry) => _entries.Add(entry);
        public void Remove(int index) => _entries.RemoveAt(index);
        public void SaveExpectedSize(IsoEntry entry, long bytes) => entry.ExpectedSizeBytes = bytes;
    }
}
