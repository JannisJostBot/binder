using GameShelf.Library;
using GameShelf.Models;
using GameShelf.ViewModels;

namespace GameShelf;

/// <summary>What first-run setup asks the panel to do (<see cref="ISetupActions"/>).</summary>
public sealed partial class MainWindow
{
    IReadOnlyList<GameSource> ISetupActions.Launchers => LibraryScanner.StoreSources;
    void ISetupActions.Skip() => FinishSetup(null);
    void ISetupActions.LoadShortcuts() => _ = LoadSetupShortcutsAsync();
    void ISetupActions.Finish(SetupChoices choices) => FinishSetup(choices);

    void ISetupActions.Import(IReadOnlyList<ShortcutFile> picked)
    {
        ShowImportResult(StoreShortcuts(picked));
        SetupPage.ViewModel!.ImportDone();
    }
}
