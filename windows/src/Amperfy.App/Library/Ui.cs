using Amperfy.App.Helpers;
using Amperfy.App.Services;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Amperfy.App.Library;

/// Small helpers to build the library UI in code (keeps the XAML minimal).
public static class Ui
{
    /// Application resource (incl. the merged WinUI resources); null if it doesn't exist.
    public static object? Resource(string key)
    {
        try
        {
            if (Application.Current.Resources.TryGetValue(key, out var value)) return value;
            return Application.Current.Resources[key];
        }
        catch
        {
            return null;
        }
    }

    public static Style? Style(string key) => Resource(key) as Style;

    public static Brush Brush(string key, Brush? fallback = null) =>
        Resource(key) as Brush ?? fallback ?? new SolidColorBrush(Colors.Gray);

    /// Opacity of secondary texts/icons. Theme brushes are not looked up in code because the app
    /// theme (RootGrid.RequestedTheme) may differ from the application theme; inherited
    /// foregrounds with reduced opacity follow the element theme.
    public const double SecondaryOpacity = 0.68;

    public static Brush FavoriteBrush { get; } = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 232, 17, 35));
    public static Brush RatingBrush { get; } = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 225, 175, 65));

    /// Accent color of the active account theme.
    public static Brush ThemeAccent => new SolidColorBrush(AppServices.Instance.ActiveTheme.AccentColor());

    public static FontFamily SymbolFont =>
        Resource("SymbolThemeFontFamily") as FontFamily ?? new FontFamily("Segoe Fluent Icons");

    public static TextBlock Text(string text = "", string? style = null, bool secondary = false, int maxLines = 1, bool wrap = false)
    {
        var tb = new TextBlock
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = wrap || maxLines > 1 ? TextWrapping.WrapWholeWords : TextWrapping.NoWrap,
            MaxLines = maxLines,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (style is not null && Style(style) is { } s) tb.Style = s;
        if (secondary) tb.Opacity = SecondaryOpacity;
        return tb;
    }

    public static FontIcon Icon(string glyph, double size = 16, Brush? foreground = null, bool secondary = false)
    {
        var icon = new FontIcon { Glyph = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        if (foreground is not null) icon.Foreground = foreground;
        if (secondary) icon.Opacity = SecondaryOpacity;
        return icon;
    }

    /// Transparent icon button with tooltip.
    public static Button IconButton(string glyph, string tooltip, RoutedEventHandler? onClick = null, double size = 16)
    {
        var button = new Button
        {
            Content = Icon(glyph, size),
            Padding = new Thickness(8),
            MinWidth = 0,
            MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(button, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip);
        if (onClick is not null) button.Click += onClick;
        return button;
    }

    /// Button with icon and text.
    public static Button TextButton(string text, string? glyph, RoutedEventHandler? onClick = null, bool accent = false, string? tooltip = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (glyph is not null) panel.Children.Add(Icon(glyph, 14));
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = panel, VerticalAlignment = VerticalAlignment.Center };
        if (accent && Style("AccentButtonStyle") is { } accentStyle) button.Style = accentStyle;
        if (tooltip is not null) ToolTipService.SetToolTip(button, tooltip);
        if (onClick is not null) button.Click += onClick;
        return button;
    }

    public static MenuFlyoutItem MenuItem(string text, string? glyph, Action onClick, bool isEnabled = true)
    {
        var item = new MenuFlyoutItem { Text = text, IsEnabled = isEnabled };
        if (glyph is not null) item.Icon = new FontIcon { Glyph = glyph };
        item.Click += (_, _) => onClick();
        return item;
    }

    public static RadioMenuFlyoutItem RadioItem(string text, string group, bool isChecked, Action onClick)
    {
        var item = new RadioMenuFlyoutItem { Text = text, GroupName = group, IsChecked = isChecked };
        item.Click += (_, _) => onClick();
        return item;
    }

    public static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match) return match;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    public static void SetFontWeight(TextBlock tb, bool bold) => tb.FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal;

    /// Empty state (icon, title, secondary text) shown when a list has no content.
    public static StackPanel EmptyState(string glyph, string title, string text)
    {
        var panel = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24, 48, 24, 48),
        };
        panel.Children.Add(Icon(glyph, 48, secondary: true));
        var titleBlock = Text(title, "SubtitleTextBlockStyle");
        titleBlock.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(titleBlock);
        var textBlock = Text(text, "BodyTextBlockStyle", secondary: true, maxLines: 3);
        textBlock.HorizontalAlignment = HorizontalAlignment.Center;
        textBlock.TextAlignment = TextAlignment.Center;
        panel.Children.Add(textBlock);
        return panel;
    }

    public static void UpdateEmptyState(StackPanel panel, string glyph, string title, string text)
    {
        if (panel.Children.Count < 3) return;
        if (panel.Children[0] is FontIcon icon) icon.Glyph = glyph;
        if (panel.Children[1] is TextBlock t) t.Text = title;
        if (panel.Children[2] is TextBlock s) s.Text = text;
    }

    public static string Plural(int count, string singular, string plural) => count == 1 ? $"1 {singular}" : $"{count} {plural}";

    /// ListView/GridView items stretch horizontally (row content fills the container).
    public static void StretchItems(ListViewBase list)
    {
        if (list is not ListView) return;
        var style = new Style(typeof(ListViewItem));
        if (Resource("DefaultListViewItemStyle") is Style baseStyle) style.BasedOn = baseStyle;
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 0, 12, 0)));
        list.ItemContainerStyle = style;
    }
}
