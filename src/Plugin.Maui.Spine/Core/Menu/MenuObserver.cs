using System.Collections.Specialized;
using System.ComponentModel;

namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Watches a whole menu tree and reports any change, so the platform rebuilds its native menu.
/// Menus are small; rebuilding the whole thing is cheaper than diffing it.
/// </summary>
internal sealed class MenuObserver : IDisposable
{
    private readonly Action _changed;
    private readonly List<INotifyCollectionChanged> _collections = [];
    private readonly List<INotifyPropertyChanged> _elements = [];

    public MenuObserver(MenuItems items, Action changed)
    {
        _changed = changed;
        Watch(items, items);
    }

    private void Watch(INotifyCollectionChanged collection, IEnumerable<MenuElement> elements)
    {
        collection.CollectionChanged += OnCollectionChanged;
        _collections.Add(collection);

        foreach (var element in elements)
        {
            element.PropertyChanged += OnPropertyChanged;
            _elements.Add(element);

            switch (element)
            {
                case MenuSection section:
                    Watch(section.Items, section.Items);
                    break;
                case SubMenu subMenu:
                    Watch(subMenu.Items, subMenu.Items);
                    break;
                case MenuPicker picker:
                    Watch(picker.Items, picker.Items);
                    break;
            }
        }
    }

    // The tree may have gained or lost branches; start over rather than patch the subscriptions.
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => _changed();

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => _changed();

    public void Dispose()
    {
        foreach (var collection in _collections)
            collection.CollectionChanged -= OnCollectionChanged;

        foreach (var element in _elements)
            element.PropertyChanged -= OnPropertyChanged;

        _collections.Clear();
        _elements.Clear();
    }
}
