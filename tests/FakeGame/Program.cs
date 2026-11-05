// Stays alive like a game would, then exits. Optional first argument: seconds (default 15).
int seconds = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 15;
Thread.Sleep(TimeSpan.FromSeconds(seconds));
