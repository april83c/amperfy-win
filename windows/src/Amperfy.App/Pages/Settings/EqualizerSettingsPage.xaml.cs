using Amperfy.App.Services;
using Amperfy.Core.Common;
using Amperfy.Core.Storage;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Amperfy.App.Pages.Settings;

/// Equalizer settings (port of EqualizerSettingsView): enable, active preset and an editor for the
/// user presets with 10 bands (32 Hz - 16 kHz, ±6 dB). Changes of the active preset are previewed live.
public sealed partial class EqualizerSettingsPage : Page
{
    private readonly AppServices _services = AppServices.Instance;
    private readonly Slider[] _sliders = new Slider[EqualizerSetting.Frequencies.Length];
    private readonly TextBlock[] _valueLabels = new TextBlock[EqualizerSetting.Frequencies.Length];
    private EqualizerSetting? _editing;
    private bool _isLoading;
    private bool _isPreviewActive;

    private UserSettings User => _services.Settings.User;

    public EqualizerSettingsPage()
    {
        InitializeComponent();
        BuildBands();
        BuildNewMenu();
        EqualizerToggle.Bind(User.IsEqualizerEnabled, isEnabled =>
        {
            User.IsEqualizerEnabled = isEnabled;
            _services.Player.UpdateEqualizerEnabled(isEnabled);
            ActiveEqualizerCard.IsEnabled = isEnabled;
        });
        ActiveEqualizerCard.IsEnabled = User.IsEqualizerEnabled;
        ActiveEqualizerCombo.SelectionChanged += ActiveEqualizerCombo_SelectionChanged;
        EditEqualizerCombo.SelectionChanged += EditEqualizerCombo_SelectionChanged;
        NameBox.TextChanged += (_, _) => UpdatePreview();
        SaveButton.Click += (_, _) => Save();
        FlatButton.Click += (_, _) => SetGains(EqualizerSetting.DefaultGains);
        DeleteButton.Click += (_, _) => _ = DeleteAsync();
        ReloadCombos();
        Unloaded += (_, _) => RevertPreview();
    }

    private static string FrequencyLabel(float frequency) =>
        frequency < 1000 ? $"{(int)frequency}" : $"{(int)(frequency / 1000)}k";

    private void BuildBands()
    {
        BandsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        BandsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        BandsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < EqualizerSetting.Frequencies.Length; i++)
        {
            BandsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            var valueLabel = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            };
            var slider = new Slider
            {
                Orientation = Orientation.Vertical,
                Minimum = -EqualizerSetting.RangeFromZero,
                Maximum = EqualizerSetting.RangeFromZero,
                StepFrequency = 0.5,
                SmallChange = 0.5,
                LargeChange = 1,
                TickFrequency = 3,
                TickPlacement = TickPlacement.Outside,
                Height = 180,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var frequencyLabel = new TextBlock
            {
                Text = FrequencyLabel(EqualizerSetting.Frequencies[i]),
                HorizontalAlignment = HorizontalAlignment.Center,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            };
            var band = i;
            AutomationProperties.SetName(slider, $"{FrequencyLabel(EqualizerSetting.Frequencies[i])} Hz");
            slider.ValueChanged += (_, e) => BandChanged(band, e.NewValue);
            Grid.SetColumn(valueLabel, i);
            Grid.SetColumn(slider, i);
            Grid.SetRow(slider, 1);
            Grid.SetColumn(frequencyLabel, i);
            Grid.SetRow(frequencyLabel, 2);
            BandsGrid.Children.Add(valueLabel);
            BandsGrid.Children.Add(slider);
            BandsGrid.Children.Add(frequencyLabel);
            _sliders[i] = slider;
            _valueLabels[i] = valueLabel;
        }
    }

    private void BuildNewMenu()
    {
        var menu = new MenuFlyout();
        var flat = new MenuFlyoutItem { Text = "Flat" };
        flat.Click += (_, _) => CreateEqualizer("My new Equalizer", EqualizerSetting.DefaultGains);
        menu.Items.Add(flat);
        menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var preset in new[] { EqualizerPreset.IncreasedBass, EqualizerPreset.ReducedBass, EqualizerPreset.IncreasedTreble })
        {
            var item = new MenuFlyoutItem { Text = $"From preset: {preset.Description()}" };
            item.Click += (_, _) => CreateEqualizer(preset.Description(), preset.Gains());
            menu.Items.Add(item);
        }
        NewEqualizerButton.Flyout = menu;
    }

    // --- active equalizer ----------------------------------------------------------------------

    private void ReloadCombos(Guid? editId = null)
    {
        _isLoading = true;
        var settings = User.EqualizerSettings;
        ActiveEqualizerCombo.Items.Clear();
        ActiveEqualizerCombo.Items.Add(EqualizerSetting.Off.Name);
        foreach (var eq in settings) ActiveEqualizerCombo.Items.Add(eq.Name);
        var activeIndex = settings.FindIndex(e => e.Id == User.ActiveEqualizerSetting.Id);
        ActiveEqualizerCombo.SelectedIndex = activeIndex + 1;

        EditEqualizerCombo.Items.Clear();
        foreach (var eq in settings) EditEqualizerCombo.Items.Add(eq.Name);
        EditEqualizerCombo.SelectedIndex = editId is { } id ? settings.FindIndex(e => e.Id == id) : -1;
        _isLoading = false;
    }

    private void ActiveEqualizerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || ActiveEqualizerCombo.SelectedIndex < 0) return;
        var index = ActiveEqualizerCombo.SelectedIndex;
        var settings = User.EqualizerSettings;
        var selected = index == 0 || index > settings.Count ? EqualizerSetting.Off : settings[index - 1];
        _isPreviewActive = false;
        User.ActiveEqualizerSetting = selected;
        _services.Player.UpdateEqualizerSetting(selected);
        UpdatePreview();
    }

    // --- editor --------------------------------------------------------------------------------

    private void EditEqualizerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        var index = EditEqualizerCombo.SelectedIndex;
        var settings = User.EqualizerSettings;
        if (index < 0 || index >= settings.Count) return;
        StartEditing(settings[index]);
    }

    private void StartEditing(EqualizerSetting eq)
    {
        RevertPreview();
        _editing = new EqualizerSetting(eq.Name, [.. NormalizedGains(eq.Gains)], eq.Id);
        _isLoading = true;
        NameBox.Text = eq.Name;
        for (var i = 0; i < _sliders.Length; i++) _sliders[i].Value = _editing.Gains[i];
        _isLoading = false;
        UpdateLabels();
        EditorPanel.Visibility = Visibility.Visible;
    }

    private static float[] NormalizedGains(float[] gains)
    {
        var result = EqualizerSetting.DefaultGains;
        for (var i = 0; i < Math.Min(result.Length, gains.Length); i++)
            result[i] = Math.Clamp(gains[i], -EqualizerSetting.RangeFromZero, EqualizerSetting.RangeFromZero);
        return result;
    }

    private void SetGains(float[] gains)
    {
        for (var i = 0; i < _sliders.Length && i < gains.Length; i++) _sliders[i].Value = gains[i];
    }

    private void BandChanged(int band, double value)
    {
        if (_editing is null || _isLoading) return;
        _editing.Gains[band] = (float)value;
        UpdateLabels();
        UpdatePreview();
    }

    private void UpdateLabels()
    {
        if (_editing is null) return;
        for (var i = 0; i < _valueLabels.Length; i++)
        {
            var gain = _editing.Gains[i];
            _valueLabels[i].Text = gain > 0 ? $"+{gain:0.#}" : $"{gain:0.#}";
        }
        CompensationText.Text = $"Volume compensation: {_editing.GainCompensation:0.0} dB";
    }

    /// Plays the edited gains when the edited equalizer is the active one.
    private void UpdatePreview()
    {
        if (_editing is null || _isLoading) return;
        if (!User.IsEqualizerEnabled || User.ActiveEqualizerSetting.Id != _editing.Id) return;
        _services.Player.UpdateEqualizerSetting(new EqualizerSetting(NameBox.Text, [.. _editing.Gains], _editing.Id));
        _isPreviewActive = true;
    }

    /// Restores the saved gains if a preview of unsaved changes is playing.
    private void RevertPreview()
    {
        if (!_isPreviewActive) return;
        _isPreviewActive = false;
        _services.Player.UpdateEqualizerSetting(User.ActiveEqualizerSetting);
    }

    private void CreateEqualizer(string name, float[] gains)
    {
        var eq = new EqualizerSetting(name, [.. gains]);
        User.EqualizerSettings = [.. User.EqualizerSettings, eq];
        ReloadCombos(eq.Id);
        StartEditing(eq);
        NameBox.Focus(FocusState.Programmatic);
        NameBox.SelectAll();
    }

    private void Save()
    {
        if (_editing is null) return;
        var list = User.EqualizerSettings.ToList();
        var index = list.FindIndex(e => e.Id == _editing.Id);
        var name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Equalizer" : NameBox.Text.Trim();
        var saved = new EqualizerSetting(name, [.. _editing.Gains], _editing.Id);
        if (index >= 0) list[index] = saved;
        else list.Add(saved);
        User.EqualizerSettings = list;
        var active = User.ActiveEqualizerSetting;
        if (active.Id == saved.Id)
        {
            // same id -> the setter would ignore the new instance: update the stored one in place
            active.Name = saved.Name;
            active.Gains = [.. saved.Gains];
            User.NotifyChanged();
            _services.Player.UpdateEqualizerSetting(active);
        }
        _isPreviewActive = false;
        _editing = new EqualizerSetting(saved.Name, [.. saved.Gains], saved.Id);
        ReloadCombos(saved.Id);
        _services.Alerts.ShowInfo("Equalizer", $"\"{saved.Name}\" saved.");
    }

    private async Task DeleteAsync()
    {
        if (_editing is null) return;
        var confirmed = await _services.Dialogs.ConfirmAsync("Delete Equalizer", "Are you sure to delete this equalizer?", "Delete", destructive: true);
        if (!confirmed || _editing is null) return;
        var id = _editing.Id;
        User.EqualizerSettings = User.EqualizerSettings.Where(e => e.Id != id).ToList();
        if (User.ActiveEqualizerSetting.Id == id)
        {
            User.ActiveEqualizerSetting = EqualizerSetting.Off;
            _services.Player.UpdateEqualizerSetting(EqualizerSetting.Off);
        }
        _isPreviewActive = false;
        _editing = null;
        EditorPanel.Visibility = Visibility.Collapsed;
        ReloadCombos();
    }
}

