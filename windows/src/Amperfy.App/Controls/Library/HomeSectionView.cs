using Amperfy.App.Helpers;
using Amperfy.App.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Amperfy.App.Controls;

/// One section of the home page (port of HomeVC's horizontally scrolling sections): title,
/// optional refresh (random sections) and "See all" actions, and a row of tiles that is paged with
/// the arrow buttons (desktop replacement of the touch scrolling row).
public sealed partial class HomeSectionView : UserControl
{
    private const double Spacing = 8;
    private readonly TextBlock _title;
    private readonly Button _refreshButton;
    private readonly HyperlinkButton _seeAllButton;
    private readonly Button _previousButton;
    private readonly Button _nextButton;
    private readonly StackPanel _tilesPanel;
    private readonly TextBlock _emptyText;
    private readonly List<EntityTile> _tiles = [];
    private IReadOnlyList<LibraryItem> _items = [];
    private int _offset;
    private int _visibleCount = 1;

    public HomeSectionView()
    {
        var root = new StackPanel { Spacing = 8, Padding = new Thickness(24, 8, 24, 16) };
        var header = new Grid { ColumnSpacing = 4 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _title = Ui.Text("", "SubtitleTextBlockStyle");
        header.Children.Add(_title);
        _refreshButton = Ui.IconButton(Icons.Refresh, "Refresh", (_, _) => RefreshRequested?.Invoke(), 14);
        _refreshButton.Visibility = Visibility.Collapsed;
        Grid.SetColumn(_refreshButton, 1);
        header.Children.Add(_refreshButton);
        _seeAllButton = new HyperlinkButton { Content = "See All", Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        _seeAllButton.Click += (_, _) => SeeAll?.Invoke();
        Grid.SetColumn(_seeAllButton, 3);
        header.Children.Add(_seeAllButton);
        _previousButton = Ui.IconButton(LibraryGlyphs.ChevronLeft, "Previous", (_, _) => Page(-1), 14);
        Grid.SetColumn(_previousButton, 4);
        header.Children.Add(_previousButton);
        _nextButton = Ui.IconButton(LibraryGlyphs.ChevronRight, "Next", (_, _) => Page(1), 14);
        Grid.SetColumn(_nextButton, 5);
        header.Children.Add(_nextButton);
        root.Children.Add(header);

        _tilesPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Spacing, Margin = new Thickness(-4, 0, 0, 0) };
        _emptyText = Ui.Text("Nothing here yet.", "BodyTextBlockStyle", secondary: true);
        _emptyText.Visibility = Visibility.Collapsed;
        root.Children.Add(_tilesPanel);
        root.Children.Add(_emptyText);
        Content = root;
        SizeChanged += (_, e) => UpdateVisibleCount(e.NewSize.Width);
    }

    public double TileWidth { get; set; } = 164;

    public string Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    /// Random sections can be refreshed (Swift: section header refresh button).
    public bool IsRefreshable
    {
        set => _refreshButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public event Action? RefreshRequested;

    /// Navigation to the matching library page (null: hidden).
    public Action? SeeAll
    {
        get => _seeAll;
        set
        {
            _seeAll = value;
            _seeAllButton.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private Action? _seeAll;

    /// Tile activation (default: open the entity; songs play).
    public Action<LibraryItem>? Activated { get; set; }

    public void SetItems(IReadOnlyList<LibraryItem> items)
    {
        _items = items;
        _offset = 0;
        Render();
    }

    private void UpdateVisibleCount(double width)
    {
        var count = Math.Max(1, (int)Math.Floor((width - 48 + Spacing) / (TileWidth + Spacing)));
        if (count == _visibleCount) return;
        _visibleCount = count;
        Render();
    }

    private void Page(int direction)
    {
        var maxOffset = Math.Max(0, _items.Count - _visibleCount);
        _offset = Math.Clamp(_offset + direction * _visibleCount, 0, maxOffset);
        Render();
    }

    private void Render()
    {
        var visible = _items.Skip(_offset).Take(_visibleCount).ToList();
        while (_tiles.Count < visible.Count)
        {
            var tile = new EntityTile { IsStandalone = true, TileWidth = TileWidth };
            tile.Activated = item => (Activated ?? (i => EntityActions.Open(LibraryItem.Unwrap(i.Entity))))(item);
            _tiles.Add(tile);
        }
        _tilesPanel.Children.Clear();
        for (var i = 0; i < visible.Count; i++)
        {
            _tiles[i].TileWidth = TileWidth;
            _tiles[i].Bind(visible[i]);
            _tilesPanel.Children.Add(_tiles[i]);
        }
        _emptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _tilesPanel.Visibility = _items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _previousButton.IsEnabled = _offset > 0;
        _nextButton.IsEnabled = _offset + _visibleCount < _items.Count;
        var showPaging = _items.Count > _visibleCount;
        _previousButton.Visibility = showPaging ? Visibility.Visible : Visibility.Collapsed;
        _nextButton.Visibility = showPaging ? Visibility.Visible : Visibility.Collapsed;
    }
}
