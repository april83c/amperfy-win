using Amperfy.App.Pages.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace Amperfy.App.Pages;

/// Settings (port of SettingsTabView / SettingsView): a list of sections on the left, the section
/// page (Pages/Settings/*) on the right. The navigation parameter is the id of the section to open
/// (e.g. <see cref="AccountSection"/>); default is the general section.
public sealed partial class SettingsPage : Page
{
    public const string GeneralSection = "general";
    public const string AccountSection = "account";
    public const string DisplaySection = "display";
    public const string SidebarSection = "sidebar";
    public const string LibrarySection = "library";
    public const string PlayerSection = "player";
    public const string EqualizerSection = "equalizer";
    public const string ArtworkSection = "artwork";
    public const string NotificationsSection = "notifications";
    public const string SupportSection = "support";
    public const string AboutSection = "about";

    /// A settings section: id (navigation parameter), title, glyph and page.
    public sealed record Section(string Id, string Title, string Glyph, Type PageType);

    public static readonly IReadOnlyList<Section> Sections =
    [
        new(GeneralSection, "General", SettingsGlyphs.General, typeof(GeneralSettingsPage)),
        new(AccountSection, "Account", SettingsGlyphs.Account, typeof(AccountSettingsPage)),
        new(DisplaySection, "Display & Interaction", SettingsGlyphs.Display, typeof(DisplaySettingsPage)),
        new(SidebarSection, "Sidebar & Home", SettingsGlyphs.Sidebar, typeof(LibraryDisplaySettingsPage)),
        new(LibrarySection, "Library", SettingsGlyphs.Library, typeof(LibrarySettingsPage)),
        new(PlayerSection, "Player, Stream & Scrobble", SettingsGlyphs.Player, typeof(PlayerSettingsPage)),
        new(EqualizerSection, "Equalizer", SettingsGlyphs.Equalizer, typeof(EqualizerSettingsPage)),
        new(ArtworkSection, "Artwork", SettingsGlyphs.Artwork, typeof(ArtworkSettingsPage)),
        new(NotificationsSection, "Notifications", SettingsGlyphs.Notifications, typeof(NotificationSettingsPage)),
        new(SupportSection, "Support", SettingsGlyphs.Support, typeof(SupportSettingsPage)),
        new(AboutSection, "About & License", SettingsGlyphs.About, typeof(AboutSettingsPage)),
    ];

    /// Sub pages that belong to a section (highlighted in the section list).
    private static readonly Dictionary<Type, string> SubPageSections = new()
    {
        [typeof(EventLogSettingsPage)] = SupportSection,
    };

    private bool _isSyncingSelection;

    public SettingsPage()
    {
        InitializeComponent();
        foreach (var section in Sections)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(new FontIcon { Glyph = section.Glyph, FontSize = 16 });
            row.Children.Add(new TextBlock { Text = section.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var item = new ListViewItem { Content = row, Tag = section };
            ToolTipService.SetToolTip(item, section.Title);
            SectionList.Items.Add(item);
        }
        SectionList.SelectionChanged += SectionList_SelectionChanged;
        SectionFrame.Navigated += SectionFrame_Navigated;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ShowSection(e.Parameter as string ?? GeneralSection);
    }

    /// Opens a section by id (see the *Section constants).
    public void ShowSection(string sectionId)
    {
        var section = Sections.FirstOrDefault(s => s.Id == sectionId) ?? Sections[0];
        if (SectionFrame.CurrentSourcePageType != section.PageType)
        {
            SectionFrame.Navigate(section.PageType, null, new EntranceNavigationTransitionInfo());
            SectionFrame.BackStack.Clear();
        }
        SelectSection(section.Id);
    }

    private void SelectSection(string sectionId)
    {
        _isSyncingSelection = true;
        SectionList.SelectedItem = SectionList.Items.OfType<ListViewItem>().FirstOrDefault(i => (i.Tag as Section)?.Id == sectionId);
        _isSyncingSelection = false;
    }

    private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection) return;
        if (SectionList.SelectedItem is ListViewItem { Tag: Section section }) ShowSection(section.Id);
    }

    /// Sub pages may navigate the section frame themselves (e.g. account -> sidebar, support -> event log).
    private void SectionFrame_Navigated(object sender, NavigationEventArgs e)
    {
        var id = SubPageSections.TryGetValue(e.SourcePageType, out var parent)
            ? parent
            : Sections.FirstOrDefault(s => s.PageType == e.SourcePageType)?.Id;
        if (id is not null) SelectSection(id);
    }

    /// Opens a section from a sub page (the settings page hosting the sub page's frame).
    internal static void Open(Frame? sectionFrame, string sectionId)
    {
        var section = Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section is null || sectionFrame is null) return;
        sectionFrame.Navigate(section.PageType, null, new EntranceNavigationTransitionInfo());
        sectionFrame.BackStack.Clear();
    }
}
