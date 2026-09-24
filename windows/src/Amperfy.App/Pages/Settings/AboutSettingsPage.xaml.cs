using Amperfy.App.Services;
using Amperfy.Core;
using Microsoft.UI.Xaml.Controls;

namespace Amperfy.App.Pages.Settings;

/// About & license (port of LicenseSettingsView): version, source, the GPLv3 license text
/// (repository LICENSE, embedded into the app) and the acknowledgements.
public sealed partial class AboutSettingsPage : Page
{
    /// The original Amperfy project (iOS/iPadOS/macOS)
    public const string SourceUrl = "https://github.com/BLeeEZ/amperfy";
    private const string LicenseResourceName = "Amperfy.LICENSE.txt";

    /// Third party software of the Windows app: name, license, purpose, project URL.
    public static readonly IReadOnlyList<(string Name, string License, string Purpose, string Url)> WindowsPackages =
    [
        ("Windows App SDK / WinUI 3", "MIT License", "User interface, notifications, windowing", "https://github.com/microsoft/WindowsAppSDK"),
        (".NET", "MIT License", "Runtime and base class libraries (incl. System.Security.Cryptography.ProtectedData)", "https://github.com/dotnet/runtime"),
        ("Windows Community Toolkit", "MIT License", "Settings controls, segmented control", "https://github.com/CommunityToolkit/Windows"),
        (".NET Community Toolkit (MVVM)", "MIT License", "MVVM helpers", "https://github.com/CommunityToolkit/dotnet"),
        ("Entity Framework Core", "MIT License", "Library database (SQLite provider, lazy loading proxies)", "https://github.com/dotnet/efcore"),
        ("SQLitePCLRaw", "Apache License 2.0", "SQLite bindings", "https://github.com/ericsink/SQLitePCL.raw"),
        ("SQLite", "Public Domain", "Database engine", "https://www.sqlite.org"),
        ("Castle Core", "Apache License 2.0", "Dynamic proxies used by EF Core lazy loading", "https://github.com/castleproject/Core"),
        ("TagLib#", "LGPL-2.1", "Embedded artworks and tags of downloaded files", "https://github.com/mono/taglib-sharp"),
        ("Velopack", "MIT License", "Installer and automatic updates", "https://github.com/velopack/velopack"),
    ];

    /// Third party software of the iOS/macOS app (LicenseSettingsView.swift).
    public static readonly IReadOnlyList<(string Name, string License)> UpstreamPackages =
    [
        ("AudioStreaming", "MIT License"),
        ("MarqueeLabel", "MIT License"),
        ("NotificationBanner", "MIT License"),
        ("ID3TagEditor", "MIT License"),
        ("CoreDataMigrationRevised-Example", "MIT License"),
        ("VYPlayIndicator", "MIT License"),
        ("CallbackURLKit", "MIT License"),
        ("DominantColors", "MIT License"),
        ("AudioVisualizerKit", "MIT License"),
        ("Alamofire", "MIT License"),
        ("Ifrit", "MIT License"),
        ("iOS-swiftUI-spotify-equalizer", "MIT License"),
        ("swift-collections", "Apache License 2.0"),
    ];

    public AboutSettingsPage()
    {
        InitializeComponent();
        VersionText.Text = $"Version {AmperfyInfo.Version}";
        SourceCard.Click += (_, _) => SettingsUi.OpenUri(UpdateService.RepositoryUrl);
        ToolTipService.SetToolTip(SourceCard, UpdateService.RepositoryUrl);
        SetupUpdates();
        LicenseText.Text = LoadLicense();

        foreach (var (name, license, purpose, url) in WindowsPackages)
        {
            var card = SettingsUi.ActionCard(name, $"{license} · {purpose}", null, () => SettingsUi.OpenUri(url));
            ToolTipService.SetToolTip(card, url);
            PackagesExpander.Items.Add(card);
        }

        var amperfy = SettingsUi.ActionCard("Amperfy", "Copyright © 2019-2026 Maximilian Bauer · GPLv3", null, () => SettingsUi.OpenUri(SourceUrl));
        ToolTipService.SetToolTip(amperfy, SourceUrl);
        UpstreamExpander.Items.Add(amperfy);
        foreach (var (name, license) in UpstreamPackages)
        {
            UpstreamExpander.Items.Add(SettingsUi.Card(name, $"{license} · used by the iOS/macOS app"));
        }
    }

    private void SetupUpdates()
    {
        if (!UpdateService.IsInstalled)
        {
            UpdatesCard.Description = "Automatic updates are available when Amperfy was installed with the installer from the releases page.";
            CheckUpdatesButton.IsEnabled = false;
            return;
        }
        UpdatesCard.Description = UpdateService.PendingVersion is { } pending
            ? $"Version {pending} is downloaded and will be installed when Amperfy closes."
            : "Amperfy checks for updates automatically and installs them when it closes.";
        CheckUpdatesButton.Click += async (_, _) =>
        {
            CheckUpdatesButton.IsEnabled = false;
            UpdatesCard.Description = "Checking for updates…";
            var status = await UpdateService.CheckAndDownloadAsync(isUserInitiated: true);
            UpdatesCard.Description = status switch
            {
                UpdateService.Status.UpToDate => $"Amperfy {UpdateService.CurrentVersion} is up to date.",
                UpdateService.Status.UpdateReady => $"Version {UpdateService.PendingVersion} is downloaded and will be installed when Amperfy closes.",
                _ => "The update check failed. See Support > Event Log.",
            };
            CheckUpdatesButton.IsEnabled = true;
        };
    }

    /// The GPLv3 text of the repository (embedded resource).
    public static string LoadLicense()
    {
        try
        {
            using var stream = typeof(AboutSettingsPage).Assembly.GetManifestResourceStream(LicenseResourceName);
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
        }
        catch
        {
            // fall through to the short notice
        }
        return "GNU General Public License v3.0\n\nThis program is free software: you can redistribute it and/or modify it under the terms of the " +
               "GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) " +
               "any later version.\n\nSee https://www.gnu.org/licenses/gpl-3.0.html";
    }
}
