using System.Runtime.InteropServices;

namespace GameShelf.Setup;

/// <summary>
/// Setup's text in English, German and French, picked by the Windows display language. Kept in code instead of
/// resource files: setup is one small Native AOT exe with invariant globalization, and these are a few dozen lines.
/// The disclaimer stays in English, as in the README, so there is one wording of it.
/// </summary>
internal static partial class Text
{
    private enum Language { English, German, French }

    private static readonly Language Current = (GetUserDefaultUILanguage() & 0x3FF) switch
    {
        0x07 => Language.German,
        0x0C => Language.French,
        _ => Language.English,
    };

    private static string Pick(string en, string de, string fr) => Current switch
    {
        Language.German => de,
        Language.French => fr,
        _ => en,
    };

    public static string WindowTitle => Pick("Game Shelf Setup", "Game Shelf Setup", "Installation de Game Shelf");

    // ---- Install ---------------------------------------------------------------------------------------------
    public static string Incomplete => Pick("This setup is incomplete", "Dieses Setup ist unvollständig", "Ce programme d’installation est incomplet");
    public static string IncompleteDetail => Pick("It was built without the app inside. Build it with publish.ps1.",
        "Es wurde ohne die App erstellt. Erstellen Sie es mit publish.ps1.", "Il a été créé sans l’application. Créez-le avec publish.ps1.");
    public static string InstallTitle => Pick("Install Game Shelf", "Game Shelf installieren", "Installer Game Shelf");
    public static string Install => Pick("Install", "Installieren", "Installer");
    public static string UpdateTitle(string version) => Pick($"Update Game Shelf to {version}", $"Game Shelf auf {version} aktualisieren", $"Mettre à jour Game Shelf vers {version}");
    public static string Update => Pick("Update", "Aktualisieren", "Mettre à jour");
    public static string AlreadyInstalled(string version) => Pick($"Game Shelf {version} is already installed", $"Game Shelf {version} ist bereits installiert", $"Game Shelf {version} est déjà installé");
    public static string Reinstall => Pick("Reinstall", "Neu installieren", "Réinstaller");
    public static string NewerInstalled(string installed) => Pick($"A newer Game Shelf ({installed}) is installed", $"Eine neuere Version von Game Shelf ({installed}) ist installiert", $"Une version plus récente de Game Shelf ({installed}) est installée");
    public static string ReplaceWith(string version) => Pick($"Replace it with {version}", $"Durch {version} ersetzen", $"Remplacer par {version}");
    public static string UpdateSummary => Pick("Your games, play history and settings are kept. If Game Shelf is open, it closes and comes back afterwards.",
        "Ihre Spiele, Spielzeiten und Einstellungen bleiben erhalten. Ein geöffnetes Game Shelf wird geschlossen und danach wieder gestartet.",
        "Vos jeux, votre historique et vos paramètres sont conservés. Si Game Shelf est ouvert, il se ferme puis revient ensuite.");
    public static string InstallSummary => Pick("Installs for your account only. No administrator rights needed.",
        "Wird nur für Ihr Konto installiert. Keine Administratorrechte nötig.", "S’installe pour votre compte uniquement. Aucun droit d’administrateur requis.");
    public static string Versions(string installed, string setup) => Pick($"Installed now: {installed}. This setup: {setup}.",
        $"Installiert: {installed}. Dieses Setup: {setup}.", $"Installée : {installed}. Ce programme : {setup}.");
    public static string InstallsTo(string dir) => Pick("Installs to " + dir, "Installationsort: " + dir, "Installation dans " + dir);
    public static string ReadDisclaimer => Pick("Please read the &disclaimer:", "Bitte lesen Sie den &Haftungsausschluss (auf Englisch):", "Veuillez lire l’&avertissement (en anglais) :");
    public static string AcceptDisclaimerMnemonic => Pick("I've &read and understood the disclaimer", "Ich habe den Haftungsausschluss &gelesen und verstanden", "J’ai &lu et compris l’avertissement");
    public static string AcceptDisclaimer => Pick("I've read and understood the disclaimer", "Ich habe den Haftungsausschluss gelesen und verstanden", "J’ai lu et compris l’avertissement");
    public static string ShowDisclaimer => Pick("Show the disclaimer", "Haftungsausschluss anzeigen", "Afficher l’avertissement");
    public static string HideDisclaimer => Pick("Hide the disclaimer", "Haftungsausschluss ausblenden", "Masquer l’avertissement");
    public static string Cancel => Pick("Cancel", "Abbrechen", "Annuler");
    public static string StartMenuDescription => Pick("Your installed games", "Ihre installierten Spiele", "Vos jeux installés");
    public static string UpToDate => Pick("Game Shelf is up to date", "Game Shelf ist aktuell", "Game Shelf est à jour");
    public static string Installed => Pick("Game Shelf is installed", "Game Shelf ist installiert", "Game Shelf est installé");
    public static string VersionLine(string version) => Pick($"Version {version}.", $"Version {version}.", $"Version {version}.");
    public static string InStartMenu => Pick("You'll find it in the Start menu. It sets itself up the first time it opens.",
        "Sie finden es im Startmenü. Beim ersten Öffnen richtet es sich ein.", "Vous le trouverez dans le menu Démarrer. Il se configure à la première ouverture.");
    public static string OpenNow => Pick("Open Game Shelf now", "Game Shelf jetzt öffnen", "Ouvrir Game Shelf maintenant");
    public static string UpdateFailed => Pick("Couldn't update Game Shelf", "Game Shelf konnte nicht aktualisiert werden", "Impossible de mettre à jour Game Shelf");
    public static string InstallFailed => Pick("Couldn't install Game Shelf", "Game Shelf konnte nicht installiert werden", "Impossible d’installer Game Shelf");
    public static string NothingLost => Pick("Nothing you had is lost. Running setup again usually fixes it.",
        "Es ist nichts verloren gegangen. Setup erneut auszuführen, behebt das meist.", "Rien n’a été perdu. Relancer l’installation règle généralement le problème.");

    // ---- Errors behind "Couldn't install" ----------------------------------------------------------------------
    public static string LinkedFolder(string dir) => Pick($"{dir} is a link to another folder, so setup won't write into it.",
        $"{dir} ist ein Link auf einen anderen Ordner, daher schreibt Setup nicht hinein.", $"{dir} est un lien vers un autre dossier, l’installation n’y écrira donc pas.");
    public static string LinkOnTheWay(string name) => Pick($"Setup won't write {name}: a folder on its way is a link.",
        $"Setup schreibt {name} nicht: ein Ordner auf dem Weg ist ein Link.", $"L’installation n’écrira pas {name} : un dossier du chemin est un lien.");
    public static string CheckFailed(string name) => Pick($"{name} didn't check out after writing it to disk.",
        $"{name} war nach dem Schreiben auf den Datenträger fehlerhaft.", $"{name} est incorrect après son écriture sur le disque.");
    public static string OwnFileNotFound => Pick("Setup couldn't find its own file.", "Setup hat seine eigene Datei nicht gefunden.", "L’installation n’a pas trouvé son propre fichier.");
    public static string AppsEntryFailed => Pick("Couldn't write the entry in Settings → Apps.", "Der Eintrag unter Einstellungen → Apps konnte nicht geschrieben werden.", "Impossible d’écrire l’entrée dans Paramètres → Applications.");
    public static string MissingFromSetup(string name) => Pick($"{name} is missing from this setup.", $"{name} fehlt in diesem Setup.", $"{name} est absent de ce programme d’installation.");
    public static string ShortcutFailed(string detail) => Pick($"Couldn't create the Start menu shortcut ({detail}).",
        $"Die Startmenü-Verknüpfung konnte nicht erstellt werden ({detail}).", $"Impossible de créer le raccourci du menu Démarrer ({detail}).");

    // ---- Program -----------------------------------------------------------------------------------------------
    public static string UnknownOption => Pick("Unknown option", "Unbekannte Option", "Option inconnue");
    public static string Usage => Pick("Run GameShelfSetup.exe without options to install, or with /S to install silently.",
        "Starten Sie GameShelfSetup.exe ohne Optionen zum Installieren oder mit /S für eine stille Installation.",
        "Lancez GameShelfSetup.exe sans option pour installer, ou avec /S pour une installation silencieuse.");
    public static string SomethingWentWrong => Pick("Something went wrong", "Etwas ist schiefgelaufen", "Un problème est survenu");
    public static string Details(string path) => Pick("Details: " + path, "Details: " + path, "Détails : " + path);

    // ---- Uninstall ---------------------------------------------------------------------------------------------
    public static string UninstallTitle => Pick("Uninstall Game Shelf?", "Game Shelf deinstallieren?", "Désinstaller Game Shelf ?");
    public static string UninstallSummary => Pick("Removes the app, its Start menu entry and Start with Windows. Shortcuts you moved onto the shelf go back on your desktop.",
        "Entfernt die App, ihren Startmenü-Eintrag und den Autostart. Verknüpfungen, die Sie ins Regal verschoben haben, kommen zurück auf den Desktop.",
        "Supprime l’application, son entrée du menu Démarrer et le démarrage avec Windows. Les raccourcis déplacés sur l’étagère retournent sur le bureau.");
    public static string UninstallHelper => Pick("Windows will ask you to allow removing Game mode's helper too.",
        "Windows fragt außerdem nach, ob das Hilfsprogramm des Spielmodus entfernt werden darf.", "Windows vous demandera aussi d’autoriser la suppression de l’assistant du mode jeu.");
    public static string Uninstall => Pick("Uninstall", "Deinstallieren", "Désinstaller");
    public static string AlsoDeleteData => Pick("Also delete my game list, play history and settings", "Auch Spieleliste, Spielzeiten und Einstellungen löschen", "Supprimer aussi ma liste de jeux, mon historique et mes paramètres");
    public static string GameRunning => Pick("A game is still running", "Es läuft noch ein Spiel", "Un jeu est toujours en cours");
    public static string GameRunningDetail => Pick("Game Shelf is keeping track of it. If you uninstall now, this session's playtime isn't saved.",
        "Game Shelf erfasst es gerade. Wenn Sie jetzt deinstallieren, wird die Spielzeit dieser Sitzung nicht gespeichert.",
        "Game Shelf le suit en ce moment. Si vous désinstallez maintenant, le temps de jeu de cette session ne sera pas enregistré.");
    public static string UninstallAnyway => Pick("Uninstall anyway", "Trotzdem deinstallieren", "Désinstaller quand même");
    public static string ShortcutsLeft(string dir) => Pick($"Some shortcuts couldn't go back on the desktop. They're still in {dir}.",
        $"Einige Verknüpfungen konnten nicht zurück auf den Desktop. Sie liegen noch in {dir}.", $"Certains raccourcis n’ont pas pu retourner sur le bureau. Ils sont toujours dans {dir}.");
    public static string DataDeleted => Pick("Your game list, play history and settings were deleted too.",
        "Spieleliste, Spielzeiten und Einstellungen wurden ebenfalls gelöscht.", "Votre liste de jeux, votre historique et vos paramètres ont aussi été supprimés.");
    public static string DataKept => Pick("Your game list, play history and settings are kept, in case you install it again.",
        "Spieleliste, Spielzeiten und Einstellungen bleiben erhalten, falls Sie es wieder installieren.", "Votre liste de jeux, votre historique et vos paramètres sont conservés si vous le réinstallez.");
    public static string HelperLeft(string dir) => Pick($"Game mode's helper is still in {dir} (it does nothing without Game Shelf). To remove it, install Game Shelf again and use Settings › Game mode › Remove it.",
        $"Das Hilfsprogramm des Spielmodus liegt noch in {dir} (ohne Game Shelf tut es nichts). Zum Entfernen Game Shelf erneut installieren und Einstellungen › Spielmodus › Entfernen wählen.",
        $"L’assistant du mode jeu est toujours dans {dir} (il ne fait rien sans Game Shelf). Pour le supprimer, réinstallez Game Shelf et utilisez Paramètres › Mode jeu › Le supprimer.");
    public static string Removed => Pick("Game Shelf was removed", "Game Shelf wurde entfernt", "Game Shelf a été supprimé");

    [LibraryImport("kernel32.dll")]
    private static partial ushort GetUserDefaultUILanguage();
}
