using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using ITService.Backup.Helpers;
using ITService.Backup.Models;
using ITService.Backup.Services;
using Microsoft.Win32;

namespace ITService.Backup;

public partial class SettingsWindow : Window
{
    private readonly SqlBackupService _sqlService = new();
    private readonly SqlConfigSyncService _sqlSync = new();
    private readonly OneCPathImportService _oneCImport = new();
    private readonly OneCConfigSyncService _oneCSync = new();
    private readonly ObservableCollection<FolderEntry> _folders = [];
    private FolderEntry? _selectedFolder;
    private bool _backupTargetUiReady;
    private bool _syncingBackupTarget;
    private bool _sqlUiReady;

    public AppConfig Config { get; private set; }

    public SettingsWindow(AppConfig config)
    {
        InitializeComponent();
        Title = $"{CompanyInfo.Name} — Настройки";
        Config = CloneConfig(config);
        Config.BackupTarget ??= new BackupTargetSettings();
        Config.Usb ??= new UsbSettings();
        try
        {
            LoadUi();
            WireSqlUiEvents();
            _sqlUiReady = true;
            Loaded += (_, _) => _backupTargetUiReady = true;
        }
        catch (Exception ex)
        {
            LogService.Error($"Settings LoadUi: {ex}");
            Loaded += (_, _) =>
            {
                AppDialog.Error(this, $"Не удалось загрузить настройки:\n{ex.Message}", AppPaths.DataDirectory);
                Close();
            };
        }
    }

    private void LoadUi()
    {
        SqlThisPcCheck.IsChecked = Config.SqlServer.UseLocalServer;
        SqlInstanceBox.Text = Config.SqlServer.Instance;
        UpdateSqlServerPanel();
        SqlExcludeLogsCheck.IsChecked = Config.SqlServer.ExcludeTransactionLogs;
        SqlCompressionCheck.IsChecked = Config.SqlServer.UseCompression;
        WindowsAuthCheck.IsChecked = Config.SqlServer.UseWindowsAuth;
        SqlUserBox.Text = Config.SqlServer.UserName ?? "";
        SqlPasswordBox.Password = Config.SqlServer.Password ?? "";
        UpdateSqlAuthPanel();

        RebuildDatabaseList();

        _folders.Clear();
        foreach (var f in Config.Folders)
        {
            var clone = CloneFolder(f);
            OneCDiscovery.ApplyToFolder(clone, defaultCopyOnlyBase: false);
            _folders.Add(clone);
        }
        RebuildFoldersList();

        RemovableOnlyCheck.IsChecked = Config.BackupTarget.RemovableOnly;
        LocalFolderBox.Text = Config.BackupTarget.LocalFolderPath;
        LocalBackupWarningText.Text = BackupSafety.LocalBackupRiskWarning;
        NonRemovableDriveWarningText.Text = BackupSafety.NonRemovableDriveWarning;

        AutoDetectUsbCheck.IsChecked = Config.Usb.AutoDetectRemovable;
        PickDriveManuallyCheck.IsChecked = Config.Usb.PickDriveManually;
        UpdateBackupTargetPanels();
        var useFolderOnLoad = !Config.BackupTarget.RemovableOnly
                              && (!Config.BackupTarget.UseUsbTarget
                                  || !string.IsNullOrWhiteSpace(Config.BackupTarget.LocalFolderPath));
        ApplySaveTargetMode(useDisk: !useFolderOnLoad);
        RefreshDriveLetterCombo();
        SelectDriveLetterInCombo(Config.Usb.DriveLetter);

        UsbRootBox.Text = Config.Usb.RootFolder;
        foreach (ComboBoxItem item in ArchiveFormatCombo.Items)
        {
            if ((string?)item.Tag == Config.ArchiveNameFormat)
            {
                ArchiveFormatCombo.SelectedItem = item;
                break;
            }
        }
        if (ArchiveFormatCombo.SelectedItem == null && ArchiveFormatCombo.Items.Count > 0)
            ArchiveFormatCombo.SelectedIndex = 0;

        Warn1CBox.Text = Config.Ui.Warn1C;
        AutoAddOneCCheck.IsChecked = Config.Ui.AutoAddOneCDatabases;
        RememberSelectionCheck.IsChecked = Config.Ui.RememberLastSelection;
    }

    private void RebuildFoldersList()
    {
        FoldersList.Items.Clear();
        foreach (var folder in _folders)
        {
            var card = new Border
            {
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 4),
                Background = (Brush)FindResource("BgBrush"),
                BorderBrush = (Brush)FindResource("MutedBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Tag = folder,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            card.MouseLeftButtonDown += (_, _) => SelectFolder(folder, card);

            var root = new StackPanel();

            var mainRow = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var names = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            var displayBox = new TextBox
            {
                Text = folder.DisplayName,
                Height = 24,
                FontSize = 12,
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(0, 0, 0, 1),
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = (Brush)FindResource("CardBrush")
            };
            displayBox.TextChanged += (_, _) => folder.DisplayName = displayBox.Text;
            names.Children.Add(displayBox);
            names.Children.Add(new TextBlock
            {
                Text = folder.Path,
                FontSize = 10,
                Foreground = (Brush)FindResource("MutedBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                ToolTip = folder.Path
            });
            Grid.SetColumn(names, 0);
            mainRow.Children.Add(names);

            var zip = new CheckBox
            {
                Content = "ZIP",
                IsChecked = folder.Archive,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0),
                FontSize = 12,
                Padding = new Thickness(0)
            };
            Grid.SetColumn(zip, 1);
            mainRow.Children.Add(zip);
            root.Children.Add(mainRow);

            var optionsRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 0) };

            if (folder.Has1CDatabase)
            {
                var badge = new Border
                {
                    Background = (Brush)FindResource("OneCBrandBgBrush"),
                    BorderBrush = (Brush)FindResource("OneCBrandBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Файловая база 1С (.1CD в каталоге)"
                };
                badge.Child = new TextBlock
                {
                    Text = "1С",
                    Foreground = (Brush)FindResource("OneCBrandTextBrush"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11
                };
                optionsRow.Children.Add(badge);

                var onlyBase = new CheckBox
                {
                    Content = "Только .1CD",
                    IsChecked = folder.CopyOnly1cdBody,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    FontSize = 12,
                    Padding = new Thickness(0),
                    ToolTip = "Копировать только файл базы (.1CD), без служебных файлов каталога"
                };
                onlyBase.Checked += (_, _) => folder.CopyOnly1cdBody = true;
                onlyBase.Unchecked += (_, _) => folder.CopyOnly1cdBody = false;
                optionsRow.Children.Add(onlyBase);
            }

            var compressionLabel = new TextBlock
            {
                Text = "Сжатие:",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("MutedBrush"),
                Margin = new Thickness(0, 0, 4, 0),
                Visibility = folder.Archive ? Visibility.Visible : Visibility.Collapsed
            };
            optionsRow.Children.Add(compressionLabel);

            var compressionCombo = CreateCompressionCombo(folder);
            compressionCombo.Visibility = folder.Archive ? Visibility.Visible : Visibility.Collapsed;
            compressionCombo.Margin = new Thickness(0, 0, 0, 0);
            optionsRow.Children.Add(compressionCombo);

            void UpdateZipUi()
            {
                var show = folder.Archive;
                compressionLabel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                compressionCombo.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }

            zip.Checked += (_, _) =>
            {
                folder.Archive = true;
                UpdateZipUi();
            };
            zip.Unchecked += (_, _) =>
            {
                folder.Archive = false;
                UpdateZipUi();
            };

            if (optionsRow.Children.Count > 0)
                root.Children.Add(optionsRow);

            card.Child = root;
            FoldersList.Items.Add(card);
        }

        if (_selectedFolder != null)
            HighlightSelectedFolder();
    }

    private ComboBox CreateCompressionCombo(FolderEntry folder)
    {
        var combo = new ComboBox
        {
            Width = 110,
            Height = 24,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        combo.Items.Add(CreateCompressionItem("Быстрое", "fast"));
        combo.Items.Add(CreateCompressionItem("Обычное", "optimal"));
        combo.Items.Add(CreateCompressionItem("Максимум", "smallest"));
        SelectCompression(combo, folder.CompressionLevel);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                folder.CompressionLevel = tag;
        };
        return combo;
    }

    private static ComboBoxItem CreateCompressionItem(string label, string tag) =>
        new() { Content = label, Tag = tag };

    private static void SelectCompression(ComboBox combo, string level)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if ((string?)item.Tag == level)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = 1;
    }

    private void SelectFolder(FolderEntry folder, Border card)
    {
        _selectedFolder = folder;
        HighlightSelectedFolder();
    }

    private void HighlightSelectedFolder()
    {
        foreach (Border card in FoldersList.Items)
        {
            if (card.Tag is FolderEntry f && f == _selectedFolder)
            {
                card.BorderBrush = (Brush)FindResource("PrimaryBrush");
                card.BorderThickness = new Thickness(1.5);
            }
            else
            {
                card.BorderBrush = (Brush)FindResource("MutedBrush");
                card.BorderThickness = new Thickness(1);
            }
        }
    }

    private void RebuildDatabaseList()
    {
        DatabasesList.Items.Clear();
        foreach (var db in Config.SqlServer.Databases)
        {
            var row = new Border
            {
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6),
                Background = (Brush)FindResource("BgBrush"),
                BorderBrush = (Brush)FindResource("MutedBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Tag = db
            };

            var line = new Grid { VerticalAlignment = VerticalAlignment.Center };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var name = new TextBlock
            {
                Text = db.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                MaxWidth = 140,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = db.Name
            };
            Grid.SetColumn(name, 0);
            line.Children.Add(name);

            var captionLabel = new TextBlock
            {
                Text = "Подпись:",
                FontSize = 11,
                Foreground = (Brush)FindResource("MutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            Grid.SetColumn(captionLabel, 1);
            line.Children.Add(captionLabel);

            var display = new TextBox
            {
                Text = db.DisplayName,
                Height = 26,
                MinWidth = 120,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Как база будет показана в списке копирования"
            };
            display.TextChanged += (_, _) => db.DisplayName = display.Text;
            Grid.SetColumn(display, 2);
            line.Children.Add(display);

            row.Child = line;
            DatabasesList.Items.Add(row);
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplySqlFieldsToConfig();
            await _sqlService.TestConnectionAsync(Config.SqlServer);
            SqlConnectionStatus.Text = "Подключение успешно";
            SqlConnectionStatus.Foreground = (Brush)FindResource("SuccessBrush");
        }
        catch (Exception ex)
        {
            SqlConnectionStatus.Text = "Ошибка";
            SqlConnectionStatus.Foreground = (Brush)FindResource("ErrorBrush");
            AppDialog.Warning(this,
                $"{ex.Message}\n\nПроверьте имя сервера — как в SSMS.",
                "Ошибка подключения");
        }
    }

    private async void RefreshDatabases_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplySqlFieldsToConfig();
            var result = await _sqlSync.SyncAsync(Config.SqlServer);
            if (!string.IsNullOrWhiteSpace(Config.SqlServer.Instance))
                SqlInstanceBox.Text = Config.SqlServer.Instance;
            UpdateSqlServerPanel();
            RebuildDatabaseList();
            SqlConnectionStatus.Text = result.Message;
            SqlConnectionStatus.Foreground = (Brush)FindResource(
                result.Success ? "SuccessBrush" : "ErrorBrush");
            if (!result.Success)
                AppDialog.Warning(this, result.Message, "Ошибка SQL");
            else if (result.NewDatabases > 0)
                AppDialog.Info(this, $"Баз на сервере: {result.DatabaseCount}. Новых в списке: {result.NewDatabases}.");
        }
        catch (Exception ex)
        {
            SqlConnectionStatus.Text = "Ошибка";
            SqlConnectionStatus.Foreground = (Brush)FindResource("ErrorBrush");
            AppDialog.Error(this, ex.Message, title: "Ошибка SQL");
        }
    }

    private void BackupTarget_Changed(object sender, RoutedEventArgs e)
    {
        if (!_backupTargetUiReady)
            return;

        try
        {
            UpdateBackupTargetPanels();
        }
        catch (Exception ex)
        {
            LogService.Error($"BackupTarget: {ex}");
            AppDialog.Error(this, ex.Message, AppPaths.DataDirectory, "Носитель");
        }
    }

    private void AutoDetectUsb_Changed(object sender, RoutedEventArgs e)
    {
        if (!_backupTargetUiReady || _syncingBackupTarget)
            return;

        if (AutoDetectUsbCheck.IsChecked == true)
            PickDriveManuallyCheck.IsChecked = false;

        if (!IsRemovableOnlyMode())
            ApplySaveTargetMode(useDisk: true);

        UpdateDiskPickControls();
    }

    private void PickDriveManually_Changed(object sender, RoutedEventArgs e)
    {
        if (!_backupTargetUiReady || _syncingBackupTarget)
            return;

        if (PickDriveManuallyCheck.IsChecked == true)
        {
            AutoDetectUsbCheck.IsChecked = false;
            if (!IsRemovableOnlyMode())
                ApplySaveTargetMode(useDisk: true);
        }

        UpdateDiskPickControls();
    }

    private void DriveLetterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_backupTargetUiReady || _syncingBackupTarget)
            return;

        if (PickDriveManuallyCheck.IsChecked == true
            && DriveLetterCombo.SelectedIndex >= 0
            && !IsRemovableOnlyMode())
            ApplySaveTargetMode(useDisk: true);

        UpdateNonRemovableDriveWarning();
    }

    private void SaveTargetMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_backupTargetUiReady || _syncingBackupTarget)
            return;

        ApplySaveTargetMode(SaveToDiskRadio.IsChecked == true);
    }

    private void LocalFolderBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_backupTargetUiReady || _syncingBackupTarget || IsRemovableOnlyMode())
            return;

        if (!string.IsNullOrWhiteSpace(LocalFolderBox.Text))
            ApplySaveTargetMode(useDisk: false);
    }

    private bool IsRemovableOnlyMode() => RemovableOnlyCheck.IsChecked == true;

    private void ApplySaveTargetMode(bool useDisk)
    {
        _syncingBackupTarget = true;
        try
        {
            SaveToDiskRadio.IsChecked = useDisk;
            SaveToFolderRadio.IsChecked = !useDisk;
            SyncTargetPanelVisibility();

            if (useDisk)
                LocalFolderBox.Text = "";
            else
                ClearDiskUiSelection();
        }
        finally
        {
            _syncingBackupTarget = false;
        }

        UpdateDiskPickControls();
        UpdateNonRemovableDriveWarning();
    }

    private void ClearDiskUiSelection()
    {
        AutoDetectUsbCheck.IsChecked = true;
        PickDriveManuallyCheck.IsChecked = false;
        DriveLetterCombo.SelectedIndex = -1;
    }

    private void SyncTargetPanelVisibility()
    {
        var removableOnly = IsRemovableOnlyMode();
        var useDisk = removableOnly || SaveToDiskRadio.IsChecked == true;

        DiskTargetPanel.Visibility = useDisk ? Visibility.Visible : Visibility.Collapsed;
        FolderTargetPanel.Visibility = !removableOnly && !useDisk ? Visibility.Visible : Visibility.Collapsed;
        LocalBackupWarningPanel.Visibility = FolderTargetPanel.Visibility;
    }

    private void UpdateBackupTargetPanels()
    {
        var removableOnly = IsRemovableOnlyMode();
        TargetModePanel.Visibility = removableOnly ? Visibility.Collapsed : Visibility.Visible;
        SyncTargetPanelVisibility();

        AutoDetectUsbCheck.Content = removableOnly
            ? "Автоопределение съёмного диска"
            : "Автоопределение диска";

        var saveToDisk = removableOnly || SaveToDiskRadio.IsChecked == true;
        ArchiveSubfolderLabel.Text = removableOnly
            ? "Подпапка для архивов на USB"
            : saveToDisk
                ? "Подпапка на выбранном диске"
                : "Подпапка внутри выбранной папки";

        if (removableOnly)
            ApplySaveTargetMode(useDisk: true);

        RefreshDriveLetterCombo();
        UpdateDiskPickControls();
        UpdateNonRemovableDriveWarning();
    }

    private void UpdateDiskPickControls()
    {
        var manual = PickDriveManuallyCheck.IsChecked == true;
        DriveLetterCombo.IsEnabled = manual;
        if (!manual && DriveLetterCombo.SelectedItem is DriveComboItem item)
            DriveLetterCombo.SelectedIndex = -1;
    }

    private void RefreshDriveLetterCombo()
    {
        var removableOnly = RemovableOnlyCheck.IsChecked == true;
        var selected = (DriveLetterCombo.SelectedItem as DriveComboItem)?.Letter ?? "";
        var drives = UsbService.DetectBackupDrives(removableOnly);

        DriveLetterCombo.Items.Clear();
        foreach (var drive in drives)
        {
            var letter = NormalizeDriveLetter(drive.Name);
            var label = UsbService.FormatDriveComboLabel(drive);
            if (string.IsNullOrEmpty(letter) || string.IsNullOrEmpty(label))
                continue;

            DriveLetterCombo.Items.Add(new DriveComboItem(letter, label, drive.DriveType));
        }

        SelectDriveLetterInCombo(string.IsNullOrEmpty(selected) ? Config.Usb.DriveLetter : selected);
    }

    private void SelectDriveLetterInCombo(string? letter)
    {
        var normalized = NormalizeDriveLetter(letter);
        if (string.IsNullOrEmpty(normalized))
        {
            DriveLetterCombo.SelectedIndex = -1;
            return;
        }

        for (var i = 0; i < DriveLetterCombo.Items.Count; i++)
        {
            if (DriveLetterCombo.Items[i] is DriveComboItem item
                && string.Equals(item.Letter, normalized, StringComparison.OrdinalIgnoreCase))
            {
                DriveLetterCombo.SelectedIndex = i;
                return;
            }
        }

        DriveLetterCombo.SelectedIndex = -1;
    }

    private void UpdateNonRemovableDriveWarning()
    {
        var removableOnly = RemovableOnlyCheck.IsChecked == true;
        if (removableOnly
            || PickDriveManuallyCheck.IsChecked != true
            || (!removableOnly && SaveToFolderRadio.IsChecked == true))
        {
            NonRemovableDriveWarningPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var isRemovable = DriveLetterCombo.SelectedItem is DriveComboItem item
                          && item.DriveType == DriveType.Removable;
        NonRemovableDriveWarningPanel.Visibility = DriveLetterCombo.SelectedIndex >= 0 && !isRemovable
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static string NormalizeDriveLetter(string? letter) =>
        DriveLetterHelper.Normalize(letter);

    private static string DescribeDriveType(DriveType type) => type switch
    {
        DriveType.Removable => "съёмный",
        DriveType.Fixed => "локальный",
        DriveType.Network => "сеть",
        _ => type.ToString()
    };

    private sealed class DriveComboItem(string letter, string display, DriveType driveType)
    {
        public string Letter { get; } = letter;
        public string Display { get; } = display;
        public DriveType DriveType { get; } = driveType;
        public override string ToString() => Display;
    }

    private void BrowseLocalBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Папка для резервных копий на этом компьютере"
        };
        if (!string.IsNullOrWhiteSpace(LocalFolderBox.Text) && Directory.Exists(LocalFolderBox.Text.Trim()))
            dlg.InitialDirectory = LocalFolderBox.Text.Trim();

        if (dlg.ShowDialog() == true)
        {
            if (!IsRemovableOnlyMode())
                ApplySaveTargetMode(useDisk: false);
            LocalFolderBox.Text = dlg.FolderName;
        }
    }

    private void WireSqlUiEvents()
    {
        SqlThisPcCheck.Checked += SqlThisPc_Changed;
        SqlThisPcCheck.Unchecked += SqlThisPc_Changed;
        WindowsAuthCheck.Checked += WindowsAuth_Changed;
        WindowsAuthCheck.Unchecked += WindowsAuth_Changed;
    }

    private void SqlThisPc_Changed(object sender, RoutedEventArgs e)
    {
        if (!_sqlUiReady)
            return;

        UpdateSqlServerPanel();
    }

    private void UpdateSqlServerPanel()
    {
        if (Config.SqlServer == null)
            return;

        var local = SqlThisPcCheck.IsChecked == true;
        SqlInstanceBox.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        SqlLocalInstanceText.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
        SqlServerLabel.Text = local ? "Сервер" : "Адрес SQL";

        if (local)
        {
            SqlLocalInstanceText.Text = string.IsNullOrWhiteSpace(Config.SqlServer.Instance)
                ? "Определится автоматически (кнопка «Обновить базы»)"
                : Config.SqlServer.Instance;
        }
    }

    private void WindowsAuth_Changed(object sender, RoutedEventArgs e)
    {
        if (!_sqlUiReady)
            return;

        UpdateSqlAuthPanel();
    }

    private void UpdateSqlAuthPanel()
    {
        var win = WindowsAuthCheck.IsChecked == true;
        SqlAuthPanel.Visibility = win ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ImportFromV8iFile_Click(object sender, RoutedEventArgs e)
    {
        if (!Helpers.OneCV8iFileDialog.TryPick(out var filePath))
            return;

        var discovered = _oneCImport.DiscoverFromV8iFile(filePath!);
        if (discovered.Count == 0)
        {
            AppDialog.Warning(this,
                $"В файле не найдено баз с распознанным Connect=:\n{filePath}",
                "Файл .v8i");
            return;
        }

        ShowOneCImportDialog(discovered);
    }

    private void ShowOneCImportDialog(IReadOnlyList<OneCDiscoveredPath> discovered)
    {
        var dlg = new OneCImportWindow(discovered) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        var existingPaths = new HashSet<string>(
            _folders.Select(f => f.Path),
            StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var item in dlg.SelectedPaths.Where(p => p.Kind == OneCPathKind.File))
        {
            if (!existingPaths.Add(item.Path))
                continue;

            var entry = OneCPathImportService.ToFolderEntry(item);
            _folders.Add(entry);
            added++;
        }

        RebuildFoldersList();

        if (added > 0)
            AppDialog.Info(this, $"Добавлено папок: {added}.");
        else
            AppDialog.Warning(this, "Новые пути не добавлены (уже есть в списке или ничего не выбрано).");
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Выберите папку для копирования" };
        if (dlg.ShowDialog() != true)
            return;

        var entry = new FolderEntry
        {
            Path = dlg.FolderName,
            DisplayName = Path.GetFileName(dlg.FolderName.TrimEnd('\\', '/')) ?? dlg.FolderName,
            Enabled = true,
            Archive = true,
            CompressionLevel = "optimal"
        };
        OneCDiscovery.ApplyToFolder(entry, defaultCopyOnlyBase: true);
        _folders.Add(entry);
        _selectedFolder = entry;
        RebuildFoldersList();
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFolder == null)
        {
            AppDialog.Info(this, "Выберите папку в списке (щелчок по карточке), затем нажмите «Удалить».");
            return;
        }

        _folders.Remove(_selectedFolder);
        _selectedFolder = null;
        RebuildFoldersList();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) =>
        UriHelper.Hyperlink_RequestNavigate(sender, e);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ApplySqlFieldsToConfig();
        if (!Config.SqlServer.UseLocalServer && string.IsNullOrWhiteSpace(Config.SqlServer.Instance))
        {
            AppDialog.Warning(this, "Укажите IP или имя удалённого SQL Server.", "SQL Server");
            return;
        }

        Config.Folders = _folders.Select(CloneFolder).ToList();
        Config.BackupTarget.RemovableOnly = RemovableOnlyCheck.IsChecked == true;

        var useFolder = !Config.BackupTarget.RemovableOnly && SaveToFolderRadio.IsChecked == true;
        var folderPath = LocalFolderBox.Text.Trim();

        if (useFolder)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                AppDialog.Warning(this, "Укажите папку для резервных копий или выберите «На любой диск».", "Папка назначения");
                return;
            }

            try
            {
                var full = Path.GetFullPath(folderPath);
                if (!Directory.Exists(full))
                {
                    AppDialog.Warning(this, $"Папка не найдена:\n{full}", "Папка назначения");
                    return;
                }

                Config.BackupTarget.LocalFolderPath = full;
                Config.BackupTarget.UseUsbTarget = false;
            }
            catch (Exception ex)
            {
                AppDialog.Warning(this, $"Некорректный путь: {ex.Message}", "Папка назначения");
                return;
            }

            if (!AppDialog.Question(this,
                    BackupSafety.LocalBackupRiskWarning + "\n\nСохранить настройки?",
                    "Подтверждение"))
                return;
        }
        else
        {
            Config.BackupTarget.UseUsbTarget = true;
            Config.BackupTarget.LocalFolderPath = "";
            Config.Usb.AutoDetectRemovable = AutoDetectUsbCheck.IsChecked == true;
            Config.Usb.PickDriveManually = PickDriveManuallyCheck.IsChecked == true;

            if (Config.Usb.PickDriveManually)
            {
                if (DriveLetterCombo.SelectedItem is not DriveComboItem picked)
                {
                    AppDialog.Warning(this, "Выберите диск из списка.", "Носитель");
                    return;
                }

                Config.Usb.DriveLetter = picked.Letter;
            }
            else
            {
                Config.Usb.DriveLetter = "";
            }
        }

        Config.Usb.RootFolder = string.IsNullOrWhiteSpace(UsbRootBox.Text) ? "Backups" : UsbRootBox.Text.Trim();
        if (ArchiveFormatCombo.SelectedItem is ComboBoxItem fmt && fmt.Tag is string tag)
            Config.ArchiveNameFormat = tag;
        Config.Ui.Warn1C = Warn1CBox.Text.Trim();
        Config.Ui.AutoAddOneCDatabases = AutoAddOneCCheck.IsChecked == true;
        Config.Ui.RememberLastSelection = RememberSelectionCheck.IsChecked == true;

        DialogResult = true;
        Close();
    }

    private void ApplySqlFieldsToConfig()
    {
        Config.SqlServer.UseLocalServer = SqlThisPcCheck.IsChecked == true;
        if (!Config.SqlServer.UseLocalServer)
            Config.SqlServer.Instance = SqlInstanceBox.Text.Trim();
        Config.SqlServer.ExcludeTransactionLogs = SqlExcludeLogsCheck.IsChecked == true;
        Config.SqlServer.UseCompression = SqlCompressionCheck.IsChecked == true;
        Config.SqlServer.UseWindowsAuth = WindowsAuthCheck.IsChecked == true;
        Config.SqlServer.UserName = SqlUserBox.Text.Trim();
        Config.SqlServer.Password = SqlPasswordBox.Password;
    }

    private static AppConfig CloneConfig(AppConfig c)
    {
        c.BackupTarget ??= new BackupTargetSettings();
        c.Usb ??= new UsbSettings();
        c.SqlServer ??= new SqlServerSettings();
        c.Ui ??= new UiSettings();
        return new()
    {
        ProductName = c.ProductName,
        ArchiveNameFormat = c.ArchiveNameFormat,
        Usb = new UsbSettings
        {
            AutoDetectRemovable = c.Usb.AutoDetectRemovable,
            PickDriveManually = c.Usb.PickDriveManually,
            DriveLetter = c.Usb.DriveLetter ?? "",
            RootFolder = c.Usb.RootFolder
        },
        BackupTarget = new BackupTargetSettings
        {
            RemovableOnly = c.BackupTarget.RemovableOnly,
            UseUsbTarget = c.BackupTarget.UseUsbTarget,
            LocalFolderPath = c.BackupTarget.LocalFolderPath
        },
        Ui = new UiSettings
        {
            AutoAddOneCDatabases = c.Ui.AutoAddOneCDatabases,
            RememberLastSelection = c.Ui.RememberLastSelection,
            Warn1C = c.Ui.Warn1C,
            InitialDiscoveryCompleted = c.Ui.InitialDiscoveryCompleted
        },
        SqlServer = new SqlServerSettings
        {
            UseLocalServer = c.SqlServer.UseLocalServer,
            Instance = c.SqlServer.Instance,
            UseWindowsAuth = c.SqlServer.UseWindowsAuth,
            UserName = c.SqlServer.UserName,
            Password = c.SqlServer.Password,
            ExcludeTransactionLogs = c.SqlServer.ExcludeTransactionLogs,
            UseCompression = c.SqlServer.UseCompression,
            Databases = c.SqlServer.Databases.Select(d => new DatabaseEntry
            {
                Name = d.Name,
                DisplayName = d.DisplayName,
                Enabled = true
            }).ToList()
        },
        Folders = c.Folders.Select(CloneFolder).ToList(),
        Files = c.Files.Select(f => new FileEntry
        {
            Id = f.Id,
            Path = f.Path,
            DisplayName = f.DisplayName,
            Enabled = true,
            Is1CDatabase = f.Is1CDatabase,
            CopyOnly1cdBody = f.CopyOnly1cdBody
        }).ToList()
        };
    }

    private static FolderEntry CloneFolder(FolderEntry f) => new()
    {
        Id = f.Id,
        Path = f.Path,
        DisplayName = f.DisplayName,
        Enabled = true,
        Archive = f.Archive,
        CompressionLevel = f.CompressionLevel,
        Has1CDatabase = f.Has1CDatabase,
        CopyOnly1cdBody = f.CopyOnly1cdBody
    };
}
