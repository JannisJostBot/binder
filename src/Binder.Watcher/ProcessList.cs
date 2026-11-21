using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Binder.Watcher;

/// <summary>
/// Every running process's id and start time in one system call (NtQuerySystemInformation, the list Task Manager
/// reads), without opening any of them. Detection uses it to open only processes it hasn't already turned down
/// (<see cref="RejectedProcesses"/>). The buffer exists only while detecting; <see cref="Release"/> frees it.
/// It comes from VirtualAlloc, not the process heap: a heap keeps a freed block of this size committed (measured on
/// the real PC: ~450 KB more commit size for the whole game), VirtualFree gives the pages back to Windows.
/// </summary>
internal sealed unsafe partial class ProcessList
{
    private const int SystemProcessInformation = 5;
    private const uint StatusInfoLengthMismatch = 0xC0000004;
    private const int MaxBytes = 64 * 1024 * 1024;
    public const int MaxProcesses = 8192;

    // SYSTEM_PROCESS_INFORMATION on x64: NextEntryOffset at 0, CreateTime at 0x20, UniqueProcessId at 0x50.
    internal const int CreateTimeOffset = 0x20, ProcessIdOffset = 0x50, MinEntrySize = 0x58;

    private void* _buffer;
    private int _size;

    /// <summary>Fills <paramref name="into"/> with the running processes. False when Windows didn't give the list.</summary>
    public bool Take(List<(uint Pid, long Created)> into)
    {
        into.Clear();
        if (_buffer is null)
        {
            _size = 512 * 1024; // enough for a few hundred processes with their threads
            _buffer = Allocate(_size);
            if (_buffer is null) return false;
        }
        for (int attempt = 0; attempt < 4; attempt++)
        {
            uint status = NtQuerySystemInformation(SystemProcessInformation, _buffer, (uint)_size, out uint needed);
            if (status == 0)
            {
                Read(new ReadOnlySpan<byte>(_buffer, (int)Math.Min(needed == 0 ? (uint)_size : needed, (uint)_size)), into);
                return true;
            }
            if (status != StatusInfoLengthMismatch) return false;
            // Grown since the size was asked for: room for more.
            int size = (int)Math.Min((long)Math.Max(needed, (uint)_size) + 128 * 1024, MaxBytes);
            if (size <= _size) return false;
            Release();
            _size = size;
            _buffer = Allocate(_size);
            if (_buffer is null) return false;
        }
        return false;
    }

    private static void* Allocate(int size) => VirtualAlloc(null, (nuint)size, MemCommit | MemReserve, PageReadWrite);

    /// <summary>Frees the buffer (detection is over); <see cref="Take"/> makes a new one when needed.</summary>
    public void Release()
    {
        if (_buffer is null) return;
        VirtualFree(_buffer, 0, MemRelease);
        _buffer = null;
    }

    /// <summary>Reads the list's entries, bounded by the data and <see cref="MaxProcesses"/>. Pure, unit-tested.</summary>
    internal static void Read(ReadOnlySpan<byte> data, List<(uint Pid, long Created)> into)
    {
        int offset = 0;
        while (into.Count < MaxProcesses && offset >= 0 && offset <= data.Length - MinEntrySize)
        {
            var entry = data[offset..];
            long created = BinaryPrimitives.ReadInt64LittleEndian(entry[CreateTimeOffset..]);
            ulong pid = BinaryPrimitives.ReadUInt64LittleEndian(entry[ProcessIdOffset..]);
            into.Add(((uint)pid, created));
            uint next = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            if (next == 0 || next > int.MaxValue - offset) break;
            offset += (int)next;
        }
    }

    [LibraryImport("ntdll.dll")]
    private static partial uint NtQuerySystemInformation(int infoClass, void* info, uint length, out uint returnLength);

    private const uint MemCommit = 0x1000, MemReserve = 0x2000, MemRelease = 0x8000, PageReadWrite = 0x04;

    [LibraryImport("kernel32.dll")]
    private static partial void* VirtualAlloc(void* address, nuint size, uint allocationType, uint protect);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool VirtualFree(void* address, nuint size, uint freeType);
}

/// <summary>
/// Processes detection opened and found not to be the game: skipped on the next passes, so each pass opens only
/// new processes. Keyed by id and start time, so an id Windows reuses for a new process is looked at again.
/// Entries of processes that are gone drop out after each pass. Pure, unit-tested.
/// </summary>
internal sealed class RejectedProcesses
{
    private Dictionary<uint, long> _last = [], _next = [];

    /// <summary>Already turned down (and kept for the next pass).</summary>
    public bool Known(uint pid, long created)
    {
        if (!_last.TryGetValue(pid, out var known) || known != created) return false;
        _next[pid] = created;
        return true;
    }

    /// <summary>Opened and not the game (never a crash reporter, which is looked for again after the game exits).</summary>
    public void Add(uint pid, long created) => _next[pid] = created;

    /// <summary>The pass is over: only what this pass saw is kept.</summary>
    public void EndPass()
    {
        (_last, _next) = (_next, _last);
        _next.Clear();
    }

    public int Count => _last.Count;
}
