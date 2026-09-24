using Amperfy.App.Library;
using Amperfy.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Amperfy.App.Controls;

/// Universal list row used by the library list templates: shows a PlayableRow, PodcastEpisodeRow,
/// EntityRow or a section header depending on the item (the DataContext: LibraryItem or
/// SectionHeaderItem). The child rows are created once and reused when the container is recycled.
public sealed partial class LibraryRow : UserControl
{
    private PlayableRow? _playableRow;
    private PodcastEpisodeRow? _episodeRow;
    private EntityRow? _entityRow;
    private SectionHeaderRow? _headerRow;

    public LibraryRow()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        DataContextChanged += (_, args) => Show(args.NewValue);
    }

    private void Show(object? data)
    {
        switch (data)
        {
            case SectionHeaderItem header:
                _headerRow ??= new SectionHeaderRow();
                _headerRow.Bind(header);
                Content = _headerRow;
                break;
            case LibraryItem item when item.Playable is PodcastEpisode && item.Context.IsEpisodeStyle:
                _episodeRow ??= new PodcastEpisodeRow();
                _episodeRow.Bind(item);
                Content = _episodeRow;
                break;
            case LibraryItem item when item.Playable is not null || item.Entity is Download or PlaylistItem:
                _playableRow ??= new PlayableRow();
                _playableRow.Bind(item);
                Content = _playableRow;
                break;
            case LibraryItem item:
                _entityRow ??= new EntityRow();
                _entityRow.Bind(item);
                Content = _entityRow;
                break;
            default:
                Content = null;
                break;
        }
    }
}

/// Section title row inside a flat list, with an optional action link ("Show all").
public sealed partial class SectionHeaderRow : UserControl
{
    private readonly TextBlock _title;
    private readonly HyperlinkButton _action;
    private SectionHeaderItem? _item;

    public SectionHeaderRow()
    {
        var root = new Grid { Padding = new Thickness(0, 16, 0, 4) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _title = Ui.Text("", "SubtitleTextBlockStyle");
        root.Children.Add(_title);
        _action = new HyperlinkButton { VerticalAlignment = VerticalAlignment.Center };
        _action.Click += (_, _) => _item?.Action?.Invoke();
        Grid.SetColumn(_action, 1);
        root.Children.Add(_action);
        Content = root;
        IsTabStop = false;
    }

    public void Bind(SectionHeaderItem item)
    {
        _item = item;
        _title.Text = item.Title;
        _action.Content = item.ActionText ?? "";
        _action.Visibility = item.ActionText is null || item.Action is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
