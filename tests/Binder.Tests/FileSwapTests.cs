using System.Security.Cryptography;
using System.Text;
using Binder.Setup;

namespace Binder.Tests;

/// <summary>The setup's all-or-nothing file swap (spec §25, "To decide" 3), on real files in a scratch folder.</summary>
static partial class Tests
{
    static void FileSwapTests(string root)
    {
        var dir = Path.Combine(root, "swap");
        Directory.CreateDirectory(dir);
        string A = Path.Combine(dir, "a.exe"), B = Path.Combine(dir, "b.dll"), C = Path.Combine(dir, "c.txt");
        void Reset()
        {
            foreach (var f in Directory.GetFiles(dir)) File.Delete(f);
            File.WriteAllText(A, "old a");
            File.WriteAllText(B, "old b"); // c.txt is new in the "update"
        }
        static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        static Action<Stream> Write(string text) => s => s.Write(Encoding.UTF8.GetBytes(text));
        var log = new List<string>();
        bool OnlyFinal() => Directory.GetFiles(dir).All(f => !f.EndsWith(".new") && !f.EndsWith(".old"));

        // A file that doesn't check out: nothing installed is touched, nothing staged is left.
        Reset();
        var swap = new FileSwap(log.Add);
        swap.Stage("a.exe", A, Write("new a"), Hash("new a"));
        bool refused = false;
        try { swap.Stage("b.dll", B, Write("tampered"), Hash("new b")); }
        catch (FileCheckException e) { refused = e.Name == "b.dll"; }
        swap.Discard();
        Check(refused && File.ReadAllText(A) == "old a" && File.ReadAllText(B) == "old b" && OnlyFinal(),
            "setup swap: a bad file stops it before anything is replaced, and the staged files are removed");

        // All checked: all swapped, a new file added, the old ones kept as .old for the clean-up.
        Reset();
        swap = new FileSwap(log.Add);
        swap.Stage("a.exe", A, Write("new a"), Hash("new a"));
        swap.Stage("b.dll", B, Write("new b"), Hash("new b"));
        swap.Stage("c.txt", C, Write("new c"), null);
        Check(File.ReadAllText(A) == "old a" && File.Exists(A + ".new"), "setup swap: staging leaves the installed files alone");
        swap.Commit();
        Check(File.ReadAllText(A) == "new a" && File.ReadAllText(B) == "new b" && File.ReadAllText(C) == "new c" &&
              File.ReadAllText(A + ".old") == "old a" && !File.Exists(A + ".new") && swap.Count == 0,
            "setup swap: commit swaps every file in");

        // A file in use the way a running exe is (renaming allowed): still swapped, the old one moved aside.
        Reset();
        swap = new FileSwap(log.Add);
        swap.Stage("a.exe", A, Write("new a"), Hash("new a"));
        using (new FileStream(A, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            swap.Commit();
        Check(File.ReadAllText(A) == "new a" && File.ReadAllText(A + ".old") == "old a", "setup swap: a running file is moved aside and replaced");

        // A rename that fails halfway: what was already swapped is put back, the rest discarded. (Only Windows
        // refuses to rename a file opened without sharing.)
        if (!OperatingSystem.IsWindows())
        {
            Directory.Delete(dir, recursive: true);
            return;
        }
        Reset();
        swap = new FileSwap(log.Add);
        swap.Stage("a.exe", A, Write("new a"), Hash("new a"));
        swap.Stage("c.txt", C, Write("new c"), null);
        swap.Stage("b.dll", B, Write("new b"), Hash("new b"));
        bool failed = false;
        using (new FileStream(B, FileMode.Open, FileAccess.Read, FileShare.None)) // can't be renamed
        {
            try { swap.Commit(); }
            catch (IOException) { failed = true; }
        }
        Check(failed && File.ReadAllText(A) == "old a" && File.ReadAllText(B) == "old b" && !File.Exists(C) && OnlyFinal() &&
              log.Any(l => l.Contains("swap failed after 2 of 3")),
            "setup swap: a failed swap puts back the old version whole");

        Directory.Delete(dir, recursive: true);
    }
}
