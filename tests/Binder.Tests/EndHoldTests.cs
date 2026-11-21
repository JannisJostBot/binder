using Binder.Library;
using Binder.Models;
using Binder.ViewModels;
using Binder.Views;
using Binder.Watcher;

namespace Binder.Tests;

/// <summary>"End a stuck game" (spec §28): the watcher's arguments, and how the shelf words a game the user ended.</summary>
static partial class Tests
{
    static void EndHoldTests()
    {
        // The watcher's arguments: anything off or malformed leaves the feature off or the defaults in place.
        Check(EndHold.ParseMode("hung") == EndHold.Mode.WhenNotResponding && EndHold.ParseMode("always") == EndHold.Mode.Always &&
              EndHold.ParseMode("") == EndHold.Mode.Off && EndHold.ParseMode("Always") == EndHold.Mode.Off, "end: only hung and always turn it on");
        Check(EndHold.ParseKey("12,71", out var m, out var k) && m == 12 && k == 'G', "end: Win+Shift+G as modifiers,key");
        Check(!EndHold.ParseKey("0,71", out _, out _) && !EndHold.ParseKey("16,71", out _, out _) && !EndHold.ParseKey("12,13", out _, out _) &&
              !EndHold.ParseKey("12", out _, out _) && !EndHold.ParseKey("a,b", out _, out _), "end: no modifier, unknown bits or a non-letter key are refused");
        var labels = EndHold.ParseLabels("Ending Hades II\nlet go to cancel\nCancelled\nHades II is still running\nHades II is responding\nHades II is responding again\nnothing was ended");
        Check(labels is { Ending: "Ending Hades II", NothingEnded: "nothing was ended" }, "end: seven lines of text");
        Check(EndHold.ParseLabels("a\nb") is null && EndHold.ParseLabels("a\nb\nc\nd\ne\nf\n") is null &&
              EndHold.ParseLabels("a\nb\nc\nd\ne\nf\ng\u0007") is null && EndHold.ParseLabels(new string('x', 3000)) is null,
            "end: a wrong count, an empty or control character or too much text is refused");
        Check(EndHold.ReportedPid("C:\\Windows\\system32\\WerFault.exe -u -p 4242 -s 1180") == 4242 &&
              EndHold.ReportedPid("WerFault.exe -u -s 1180") == 0 && EndHold.ReportedPid("WerFault.exe -p x") == 0, "end: the process an error report is about");
        Check(EndHold.Shown(0.6, true) == 0.6 && EndHold.Shown(0.6, false) == 0.5 && EndHold.Shown(0.99, false) == 0.75 &&
              EndHold.Shown(1.4, true) == 1, "end: smooth fill, or quarters with animations off");
        Check(EndHold.HoldMs == 2500 && EndHold.DelayMs == 300, "end: a 2.5 s hold after 0.3 s (owner, 2026-11-21)");
        Check(EndHold.SegmentFill(0, 0) == 0 && EndHold.SegmentFill(0.125, 0) == 0.5 && EndHold.SegmentFill(0.125, 1) == 0 &&
              EndHold.SegmentFill(0.625, 1) == 1 && EndHold.SegmentFill(0.625, 2) == 0.5 && EndHold.SegmentFill(1, 3) == 1,
            "end: the four parts grow one after another");
        Check(EndHold.SegmentFill(EndHold.Shown(0.6, false), 2) == 0 && EndHold.SegmentFill(EndHold.Shown(0.8, false), 2) == 1,
            "end: with animations off a part lights up whole");
        // The hold's sound (board 44): rises from silence, grows with each part, ends ringing out, never clips.
        var hum = new short[(int)(HoldSound.Seconds * HoldSound.SampleRate)];
        HoldSound.Render(hum);
        double P = HoldSound.Part;
        double Rms(short[] s, double from, double to)
        {
            int a = (int)(from * HoldSound.SampleRate), b = (int)(to * HoldSound.SampleRate);
            double sum = 0;
            for (int k = a; k < b; k++) sum += (double)s[k] * s[k];
            return Math.Sqrt(sum / (b - a));
        }
        int peak = hum.Max(s => Math.Abs((int)s));
        Check(Math.Abs((int)hum[0]) < 50 && peak is > 3000 and < 32000, $"end sound: starts silent and stays clear of clipping (peak {peak})");
        Check(HoldSound.FullAt == 2.5 && Rms(hum, 0.8 * P, 0.95 * P) < Rms(hum, 1.8 * P, 1.95 * P) && Rms(hum, 1.8 * P, 1.95 * P) < Rms(hum, 2.8 * P, 2.95 * P) &&
              Rms(hum, 2.8 * P, 2.95 * P) < Rms(hum, 3.8 * P, 3.95 * P), "end sound: each part of the ring adds to it, timed with the ring");
        double full = HoldSound.FullAt;
        Check(Rms(hum, full + 0.1, full + 0.3) > 300 && Rms(hum, full + 2.0, full + 2.1) < Rms(hum, full + 0.1, full + 0.3) / 10 && Math.Abs((int)hum[^1]) < 20,
            "end sound: full, it rings out to silence");
        // Let go: the samples not yet played fade from where they are, with no step anywhere (a click).
        var faded = (short[])hum.Clone();
        int at = (int)(1.3 * HoldSound.SampleRate), len = (int)(HoldSound.FadeSeconds * HoldSound.SampleRate);
        HoldSound.FadeOut(faded, at, len);
        int step = 0;
        for (int j = at - 200; j < faded.Length - 1; j++) step = Math.Max(step, Math.Abs(faded[j + 1] - faded[j]));
        int hum1 = 0;
        for (int j = at - 200; j < at + len; j++) hum1 = Math.Max(hum1, Math.Abs(hum[j + 1] - hum[j]));
        Check(faded[at - 1] == hum[at - 1] && step <= hum1 && faded.Skip(at + len).All(s => s == 0) && Rms(faded, 1.3 + 0.3 * HoldSound.FadeSeconds, 1.3 + 0.4 * HoldSound.FadeSeconds) < Rms(hum, 1.3, 1.31) * 1.5,
            "end sound: let go, the hum sinks away smoothly and then is silent");

        // Held while the keys can't be read (a hung game's "Not responding" window, an admin game): the auto-repeat.
        Check(EndHold.RepeatTimes(1, 31) == (633, 126), "end: Windows' default keyboard: first repeat by 0.63 s, then every 0.13 s at most");
        Check(EndHold.RepeatTimes(3, 0) == (1500, 860) && EndHold.RepeatTimes(9, -4) == EndHold.RepeatTimes(3, 0),
            "end: the slowest repeat still counts as held; out-of-range settings are clamped");
        var text = Format.EndStuckText("Hades II").Split('\n');
        Check(text.Length == 7 && text[0] == "Ending Hades II" && text[5] == "Hades II is responding again" &&
              EndHold.ParseLabels(string.Join('\n', text)) is not null, "end: the widget's texts are what the watcher accepts");
        Check(EndHold.ParseLabels(Format.EndStuckText(new string('x', 200) + "\u0001")) is not null, "end: a long or odd game name still fits");

        // How a session ended with the hold is told on the shelf.
        var start = new DateTimeOffset(2026, 11, 19, 20, 0, 0, TimeSpan.FromHours(1));
        Session S(bool hung = false, bool report = false) => new()
        {
            GameId = "steam:1", Start = start, End = start.AddMinutes(72), ExitCode = EndHold.EndedExitCode, Exe = "Hades2.exe",
            EndedByUser = true, Hung = hung, ErrorReport = report,
        };
        Check(CrashAnalysis.NeedsCheck(S()), "end: an ended session is always checked");
        var ok = CrashAnalysis.Analyze(S(), null, []);
        Check(ok.Ending == GameEnding.EndedByUser && !ok.IsProblem && ok.HasCard && !ok.Seen && ok.Culprit == CrashCulprit.None,
            "end: responding when ended is not a crash, but has a card");
        Check(CrashAnalysis.Title("Hades II", ok) == "You ended Hades II after 1 h 12 min" &&
              CrashAnalysis.Explanation(ok) == "It was still responding, so it isn't counted as a crash.", "end: the plain card's words");
        Check(!CrashAnalysis.IsRestartable(ok), "end: never restarted");
        var hung = CrashAnalysis.Analyze(S(hung: true), null, []);
        Check(hung.Ending == GameEnding.Crashed && hung.Hang && hung.IsProblem && hung.Culprit == CrashCulprit.None && !CrashAnalysis.IsRestartable(hung),
            "end: not responding when ended is a hang, not restarted");
        Check(CrashAnalysis.Title("Hades II", hung) == "Hades II stopped responding after 1 h 12 min" &&
              CrashAnalysis.Explanation(hung) == "You ended it after Windows marked it as not responding." &&
              CrashAnalysis.WhatHappened(hung) == "Stopped responding, and you ended it", "end: the hang card's words");
        var report = CrashAnalysis.Analyze(S(hung: true, report: true), null, []);
        Check(report.Ending == GameEnding.Crashed && !report.Hang && report.ErrorReport &&
              CrashAnalysis.Title("Hades II", report) == "Hades II crashed after 1 h 12 min" &&
              CrashAnalysis.Explanation(report) == "You ended it while Windows was saving the crash report.", "end: an error report running is a crash");
        Check(!CrashAnalysis.BugReport("Hades II", null, hung, new PcFacts("Windows 11", [], 32), 1, 1).Contains("0x00000001"),
            "end: the bug report doesn't pass the hold's exit code off as the game's error");
        var crashEvent = new WindowsEvent(1000, "Application Error", start.AddMinutes(71), new Dictionary<string, string>
        {
            ["AppName"] = "Hades2.exe", ["AppPath"] = @"C:\Games\Hades II\Hades2.exe", ["ModuleName"] = "nvwgf2umx.dll", ["ExceptionCode"] = "c0000005",
        });
        var driver = CrashAnalysis.Analyze(S(report: true), @"C:\Games\Hades II", [crashEvent]);
        Check(driver.Culprit == CrashCulprit.NvidiaDriver && CrashAnalysis.AlsoAtThatTime(driver)!.Contains("You ended it"),
            "end: a known crash keeps its advice, and says it was ended");

        // Its card shows when Binder comes back for that game, never later (with "Come back" off).
        Check(CrashAnalysis.ShowsCard(ok, "steam:1") && !CrashAnalysis.ShowsCard(ok, null) && !CrashAnalysis.ShowsCard(ok, "steam:2") &&
              CrashAnalysis.ShowsCard(CrashAnalysis.Analyze(new Session { GameId = "g", Start = start, End = start.AddMinutes(9), ExitCode = 0xC0000005 }, null, []), null),
            "end: the card only on the return; crashes as before");

        // Settings: the card's choice and its line.
        var spy = new SettingsActionsSpy();
        var vm = new SettingsViewModel(spy);
        var settings = new Settings();
        vm.Load(settings, [], autostartOn: false, refreshing: false, importedShortcuts: 0, [], null, hotkeyFree: true);
        Check(vm.EndStuckIndex == 0 && vm.EndStuckText == "Hold Win+Shift+G" && vm.EndStuckAvailable, "end: on by default, when it stops responding");
        vm.EndStuckIndex = 2;
        Check(settings.EndStuckGame == EndStuckGame.Off && vm.EndStuckText == "Off" && spy.Changes is [SettingChange.Behavior], "end: turned off");
        vm.EndStuckIndex = 1;
        Check(settings.EndStuckGame == EndStuckGame.Always, "end: always");
        vm.HotkeyIndex = 3; // Off
        Check(!vm.EndStuckAvailable && vm.EndStuckText == "Needs a keyboard shortcut", "end: needs the panel's shortcut");
    }
}
