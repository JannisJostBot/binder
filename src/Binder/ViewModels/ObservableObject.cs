using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Binder.ViewModels;

/// <summary>
/// Base class for view models: raises <see cref="PropertyChanged"/> so <c>x:Bind Mode=OneWay/TwoWay</c> updates the
/// view. No reflection (Native AOT): <see cref="CallerMemberNameAttribute"/> fills in the property name at compile time.
/// </summary>
public abstract partial class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Sets <paramref name="field"/> and notifies, only when the value really changes. Returns whether it did.</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Tells every binding to read again (x:Bind treats an empty name as "all properties").</summary>
    protected void OnAllPropertiesChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
