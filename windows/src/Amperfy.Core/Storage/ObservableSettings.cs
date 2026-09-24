using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Amperfy.Core.Storage;

/// Base class for settings sections. Raises PropertyChanged (for UI bindings) and Changed
/// (bubbled up to AmperfySettings, which persists the settings with a small debounce).
public abstract class ObservableSettings : INotifyPropertyChanged, IJsonOnDeserialized
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// Raised on any change of this section or of a nested section.
    public event Action? Changed;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        if (field is ObservableSettings oldChild) oldChild.Changed -= NotifyChanged;
        field = value;
        if (value is ObservableSettings newChild) newChild.Changed += NotifyChanged;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        NotifyChanged();
        return true;
    }

    /// Call when a nested collection was mutated in place.
    public void NotifyChanged() => Changed?.Invoke();

    protected void AttachChild(ObservableSettings? child)
    {
        if (child is null) return;
        child.Changed -= NotifyChanged;
        child.Changed += NotifyChanged;
    }

    public virtual void OnDeserialized() { }
}
