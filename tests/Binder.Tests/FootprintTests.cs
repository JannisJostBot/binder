using System.Buffers.Binary;
using Binder.Watcher;

namespace Binder.Tests;

static partial class Tests
{
    /// <summary>Lean speed-ups: the watcher's detection opens only processes it hasn't turned down before.</summary>
    static void WatcherDetectionTests()
    {
        // Three entries as Windows lays them out (x64), the last with NextEntryOffset 0.
        var data = new byte[0x200];
        void Entry(int at, uint next, uint pid, long created)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), next);
            BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(at + ProcessList.CreateTimeOffset), created);
            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(at + ProcessList.ProcessIdOffset), pid);
        }
        Entry(0, 0x80, 0, 0);
        Entry(0x80, 0x100, 4, 11);
        Entry(0x180, 0, 1234, 99);
        var list = new List<(uint Pid, long Created)>();
        ProcessList.Read(data, list);
        Check(list.Count == 3 && list[1] == (4u, 11L) && list[2] == (1234u, 99L), "process list: ids and start times read in order");

        list.Clear();
        ProcessList.Read(data.AsSpan(0, 0x1C0), list); // the last entry cut off
        Check(list.Count == 2, "process list: an entry past the end isn't read");
        list.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x180), 0x7FFFFFF0); // a bogus offset
        ProcessList.Read(data, list);
        Check(list.Count == 3, "process list: an offset past the end stops the reading");
        list.Clear();
        ProcessList.Read([], list);
        Check(list.Count == 0, "process list: nothing to read, no entries");

        var rejected = new RejectedProcesses();
        rejected.Add(10, 100);
        rejected.Add(20, 200);
        rejected.EndPass();
        Check(rejected.Known(10, 100), "rejected: a process turned down is skipped next pass");
        Check(!rejected.Known(20, 201), "rejected: a reused process id is looked at again");
        rejected.EndPass();
        Check(rejected.Known(10, 100) && rejected.Count == 1, "rejected: still running, still skipped");
        rejected.EndPass();
        rejected.EndPass();
        Check(!rejected.Known(10, 100) && rejected.Count == 0, "rejected: gone processes drop out");
    }
}
