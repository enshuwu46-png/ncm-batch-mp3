using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace NcmBatchMp3.App;

public sealed class BatchObservableCollection<T> : ObservableCollection<T>
{
    public void AddRange(IReadOnlyList<T> values)
    {
        if (values.Count == 0) return;
        CheckReentrancy();
        foreach (var value in values) Items.Add(value);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
