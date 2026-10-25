using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace WheelWizard.CustomMusic.ViewModels;

/// <summary>An <see cref="ObservableCollection{T}"/> whose contents can be replaced with a single change notification.</summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>Replaces everything at once and raises one <see cref="NotifyCollectionChangedAction.Reset"/>.</summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
