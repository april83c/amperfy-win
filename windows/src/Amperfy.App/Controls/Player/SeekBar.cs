using Amperfy.App.Services.Player;
using Amperfy.Core.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Amperfy.App.Controls.Player;

/// Seconds → "1:23" for the slider thumb tool tip.
public sealed class SecondsToTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        double d => d.AsColonDurationString(),
        int i => i.AsColonDurationString(),
        _ => "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// Time slider with elapsed/remaining time (port of SeekableTimeSlider + PlayerUIHandler.refreshTimeInfo):
/// dragging previews the time and seeks on release, clicks/keyboard seek immediately; live streams show "LIVE".
/// The host calls <see cref="Refresh"/> periodically.
public sealed partial class SeekBar : UserControl
{
    private readonly Slider _slider;
    private readonly TextBlock _elapsed;
    private readonly TextBlock _remaining;
    private readonly Border _live;
    private readonly StackPanel _audioInfoPanel;
    private readonly FontIcon _playTypeIcon;
    private readonly TextBlock _audioInfo;
    private bool _isUpdating;
    private bool _isDragging;

    public SeekBar()
    {
        _slider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            StepFrequency = 1,
            SmallChange = 5,
            LargeChange = 30,
            IsThumbToolTipEnabled = true,
            ThumbToolTipValueConverter = new SecondsToTimeConverter(),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_slider, "Seek");
        _elapsed = CreateTimeLabel(TextAlignment.Right);
        _remaining = CreateTimeLabel(TextAlignment.Left);
        _live = new Border
        {
            Padding = new Thickness(8, 1, 8, 1),
            CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            // theme independent (brushes from the app resources don't follow a per window RequestedTheme)
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 196, 43, 28)),
            Child = new TextBlock
            {
                Text = "LIVE",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            },
        };

        _playTypeIcon = new FontIcon { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        _audioInfo = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        _audioInfoPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.7,
            Visibility = Visibility.Collapsed,
        };
        _audioInfoPanel.Children.Add(_playTypeIcon);
        _audioInfoPanel.Children.Add(_audioInfo);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(_slider, 1);
        Grid.SetColumn(_live, 1);
        Grid.SetColumn(_remaining, 2);
        Grid.SetRow(_audioInfoPanel, 1);
        Grid.SetColumnSpan(_audioInfoPanel, 3);
        grid.Children.Add(_elapsed);
        grid.Children.Add(_slider);
        grid.Children.Add(_live);
        grid.Children.Add(_remaining);
        grid.Children.Add(_audioInfoPanel);
        Content = grid;

        _slider.ValueChanged += Slider_ValueChanged;
        _slider.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => _isDragging = true), true);
        _slider.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => EndDrag()), true);
        _slider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler((_, _) => EndDrag()), true);
        _slider.AddHandler(PointerCanceledEvent, new PointerEventHandler((_, _) => EndDrag()), true);
    }

    /// Shows the audio format line (e.g. "FLAC 1024 kbps") below the slider.
    public bool ShowsAudioInfo { get; set; }

    private static TextBlock CreateTimeLabel(TextAlignment alignment) => new()
    {
        Text = "--:--",
        FontSize = 12,
        Opacity = 0.8,
        TextAlignment = alignment,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private void Slider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdating) return;
        if (_isDragging)
        {
            // preview while dragging (Swift timeSliderIsChanging)
            _elapsed.Text = PlayerUi.ElapsedTimeText(e.NewValue);
            _remaining.Text = PlayerUi.RemainingTimeText(e.NewValue, PlayerUi.Player.Duration);
            return;
        }
        PlayerUi.Player.Seek(e.NewValue);
    }

    private void EndDrag()
    {
        if (!_isDragging) return;
        _isDragging = false;
        if (_slider.IsEnabled) PlayerUi.Player.Seek(_slider.Value);
    }

    public void Refresh()
    {
        var player = PlayerUi.Player;
        var playable = player.CurrentlyPlaying;
        _isUpdating = true;
        try
        {
            if (playable is null)
            {
                _slider.Visibility = Visibility.Visible;
                _live.Visibility = Visibility.Collapsed;
                _slider.IsEnabled = false;
                _slider.Maximum = 1;
                _slider.Value = 0;
                _elapsed.Text = "--:--";
                _remaining.Text = "--:--";
                _audioInfoPanel.Visibility = Visibility.Collapsed;
                return;
            }
            if (playable.IsRadio)
            {
                _slider.Visibility = Visibility.Collapsed;
                _live.Visibility = Visibility.Visible;
                _elapsed.Text = "";
                _remaining.Text = "";
                _audioInfoPanel.Visibility = Visibility.Collapsed;
                return;
            }
            _slider.Visibility = Visibility.Visible;
            _live.Visibility = Visibility.Collapsed;
            var duration = player.Duration;
            if (!double.IsFinite(duration) || duration <= 0) duration = playable.Duration;
            _slider.IsEnabled = duration > 0;
            if (!_isDragging)
            {
                var elapsed = Math.Clamp(player.ElapsedTime, 0, Math.Max(duration, 0));
                _slider.Maximum = Math.Max(1, duration);
                _slider.Value = elapsed;
                _elapsed.Text = PlayerUi.ElapsedTimeText(elapsed);
                _remaining.Text = PlayerUi.RemainingTimeText(elapsed, duration);
            }
            if (ShowsAudioInfo && PlayerUi.AudioInfo() is { } info && info.Text.Length > 0)
            {
                _audioInfo.Text = info.Text;
                _playTypeIcon.Glyph = info.IsCached ? PlayerGlyphs.Cached : PlayerGlyphs.Streaming;
                ToolTipService.SetToolTip(_audioInfoPanel, info.IsCached ? "Playing from cache" : "Streaming");
                _audioInfoPanel.Visibility = Visibility.Visible;
            }
            else
            {
                _audioInfoPanel.Visibility = Visibility.Collapsed;
            }
        }
        finally
        {
            _isUpdating = false;
        }
    }
}
