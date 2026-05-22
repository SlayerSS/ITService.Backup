using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using ITService.Backup.Helpers;
using ITService.Backup.Models;
using ITService.Backup.Services;

namespace ITService.Backup;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService = new();
    private readonly HistoryService _historyService = new();
    private readonly BackupDestinationService _destinationService = new();
    private readonly BackupOrchestrator _orchestrator = new();
    private readonly MachineDiscoveryService _machineDiscovery = new();
    private readonly OneCConfigSyncService _oneCSync = new();
    private readonly HashSet<string> _newFolderIdsUncheckedOnMain = new(StringComparer.OrdinalIgnoreCase);

    private AppConfig _config = new();
    private bool _sqlSyncInProgress;
    private string _sqlDiscoveryTooltip = "";
    private string _oneCDiscoveryTooltip = "";
    private string _foldersDiscoveryTooltip = "";
    private BackupDestinationStatus _destStatus = new();
    private readonly Dictionary<string, CheckBox> _sqlChecks = new();
    private readonly Dictionary<string, CheckBox> _folderChecks = new();
    private readonly DispatcherTimer _usbTimer;
    private CancellationTokenSource? _estimateCts;
    private bool _spaceInsufficient;
    private bool _infoErrorFromSpaceEstimate;
    private string? _infoError;
    private string _infoBannerFullText = "";
    private const double InfoBannerHeightNormal = 54;
    private const double InfoBannerHeightWithMoreButton = 70;
    private const double InfoBannerErrorTextMaxHeight = 38;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"{CompanyInfo.Name} — Резервное копирование";
        _config = _configService.Load();
        RenderInfoBanner();

        _usbTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _usbTimer.Tick += (_, _) => RefreshUsbAndHistory();
        _usbTimer.Start();

        Loaded += async (_, _) =>
        {
            RefreshUsbAndHistory();
            RebuildItems();
            UpdateDiscoveryIndicators(searching: false);

            if (!MachineDiscoveryService.IsStartupDiscoveryCompleted)
                await RunStartupDiscoveryAsync();
        };
    }

    private async Task RunStartupDiscoveryAsync()
    {
        if (_sqlSyncInProgress)
            return;

        _sqlSyncInProgress = true;
        UiThread.Run(() => UpdateDiscoveryIndicators(searching: true));

        try
        {
            var (sqlResult, oneCResult) = await _machineDiscovery.DiscoverAsync(
                _config, MachineDiscoveryService.GetStartupOptions(_config));

            _config.Ui.InitialDiscoveryCompleted = true;
            RegisterNewMainListFolderIds(oneCResult.NewFolderIds);
            _configService.Save(_config);

            _sqlDiscoveryTooltip = sqlResult.Message;
            _oneCDiscoveryTooltip = oneCResult.Message;
            _foldersDiscoveryTooltip = BuildFoldersDiscoveryTooltip();

            UiThread.Run(() =>
            {
                RebuildItems();
                UpdateDiscoveryIndicators(searching: false);
                if (sqlResult.Success)
                    ClearInfoError();
            });
        }
        catch (Exception ex)
        {
            _sqlDiscoveryTooltip = $"Ошибка: {ex.Message}";
            _oneCDiscoveryTooltip = "";
            _foldersDiscoveryTooltip = "";
            LogService.Warn($"Startup discovery failed: {ex.Message}");
            UiThread.Run(() => UpdateDiscoveryIndicators(searching: false));
        }
        finally
        {
            _sqlSyncInProgress = false;
            MachineDiscoveryService.MarkStartupDiscoveryCompleted();
        }
    }

    private string BuildFoldersDiscoveryTooltip()
    {
        var extra = _config.Folders.Count(f => !f.Has1CDatabase);
        return extra > 0
            ? $"Доп. папки: включено {extra}"
            : "Доп. папки: укажите в настройках (вкладка «Папки»)";
    }

    private enum DiscoveryIndicatorState { Searching, Ready, Unavailable }

    private void UpdateDiscoveryIndicators(bool searching)
    {
        var sqlCount = _config.SqlServer.Databases.Count;
        var oneCCount = _config.Folders.Count(f => f.Has1CDatabase);
        var extraFoldersCount = _config.Folders.Count(f => !f.Has1CDatabase);

        if (searching)
        {
            if (sqlCount > 0)
                ApplyDiscoveryIndicator(SqlDiscoveryDot, SqlDiscoveryHint, DiscoveryIndicatorState.Ready, sqlCount);
            else
                ApplyDiscoveryIndicator(SqlDiscoveryDot, SqlDiscoveryHint, DiscoveryIndicatorState.Searching, 0);

            ApplyDiscoveryIndicator(OneCDiscoveryDot, OneCDiscoveryHint, DiscoveryIndicatorState.Searching, 0);

            var foldersStateSearching = extraFoldersCount > 0
                ? DiscoveryIndicatorState.Ready
                : DiscoveryIndicatorState.Unavailable;
            ApplyDiscoveryIndicator(FoldersDiscoveryDot, FoldersDiscoveryHint, foldersStateSearching, extraFoldersCount);

            SqlDiscoveryPanel.ToolTip = sqlCount > 0
                ? "Обновление списка баз SQL…"
                : "Поиск SQL Server…";
            OneCDiscoveryPanel.ToolTip = "Поиск путей 1С…";
            FoldersDiscoveryPanel.ToolTip = BuildFoldersDiscoveryTooltip();
            return;
        }

        var sqlState = sqlCount > 0 ? DiscoveryIndicatorState.Ready : DiscoveryIndicatorState.Unavailable;
        var oneCState = oneCCount > 0 ? DiscoveryIndicatorState.Ready : DiscoveryIndicatorState.Unavailable;
        var foldersState = extraFoldersCount > 0
            ? DiscoveryIndicatorState.Ready
            : DiscoveryIndicatorState.Unavailable;

        ApplyDiscoveryIndicator(SqlDiscoveryDot, SqlDiscoveryHint, sqlState, sqlCount);
        ApplyDiscoveryIndicator(OneCDiscoveryDot, OneCDiscoveryHint, oneCState, oneCCount);
        ApplyDiscoveryIndicator(FoldersDiscoveryDot, FoldersDiscoveryHint, foldersState, extraFoldersCount);

        SqlDiscoveryPanel.ToolTip = string.IsNullOrWhiteSpace(_sqlDiscoveryTooltip)
            ? BuildDiscoveryTooltip("SQL", sqlState, sqlCount)
            : _sqlDiscoveryTooltip;
        OneCDiscoveryPanel.ToolTip = string.IsNullOrWhiteSpace(_oneCDiscoveryTooltip)
            ? BuildDiscoveryTooltip("1С", oneCState, oneCCount)
            : _oneCDiscoveryTooltip;
        FoldersDiscoveryPanel.ToolTip = string.IsNullOrWhiteSpace(_foldersDiscoveryTooltip)
            ? BuildDiscoveryTooltip("Доп. папки", foldersState, extraFoldersCount)
            : _foldersDiscoveryTooltip;
    }

    private static string BuildDiscoveryTooltip(
        string label,
        DiscoveryIndicatorState state,
        int count) =>
        state switch
        {
            DiscoveryIndicatorState.Ready => $"{label}: в настройках {count}",
            DiscoveryIndicatorState.Searching => $"{label}: поиск…",
            _ => $"{label}: не найдено"
        };

    private void ApplyDiscoveryIndicator(
        System.Windows.Shapes.Ellipse dot,
        TextBlock hint,
        DiscoveryIndicatorState state,
        int count)
    {
        var brushKey = state switch
        {
            DiscoveryIndicatorState.Ready => "SuccessBrush",
            DiscoveryIndicatorState.Searching => "WarningBrush",
            _ => "ErrorBrush"
        };
        dot.Fill = (Brush)FindResource(brushKey);
        hint.Text = state switch
        {
            DiscoveryIndicatorState.Searching => "поиск",
            DiscoveryIndicatorState.Ready => count.ToString(),
            _ => "нет"
        };
    }

    private void UpdateSelectedCount()
    {
        var sql = _sqlChecks.Values.Count(c => c.IsChecked == true);
        var folders = _folderChecks.Values.Count(c => c.IsChecked == true);
        var files = _config.Files.Count;
        var total = sql + folders + files;
        SelectedCountText.Text = $"Выбрано: {total}";
    }

    private string BuildDefaultInfoText()
    {
        var useUsb = _config.BackupTarget?.UseUsbTarget ?? true;
        var removableOnly = _config.BackupTarget?.RemovableOnly ?? true;
        var text = BackupSafety.SafetyNotice(useUsb, removableOnly);
        if (!string.IsNullOrWhiteSpace(_config.Ui.Warn1C))
            text += "\n" + _config.Ui.Warn1C.Trim();
        return text;
    }

    private void SetInfoError(string message)
    {
        _infoError = message.Trim();
        RenderInfoBanner();
    }

    private void ClearInfoError()
    {
        _infoError = null;
        RenderInfoBanner();
    }

    private void SetInfoNotice(string message)
    {
        _infoError = null;
        ApplyInfoBannerChrome(isError: false);
        _infoBannerFullText = message;
        InfoBannerText.Text = message;
        ScheduleInfoBannerOverflowCheck();
    }

    private void ApplyInfoBannerChrome(bool isError)
    {
        if (isError)
        {
            InfoBannerBorder.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE));
            InfoBannerBorder.BorderBrush = (Brush)FindResource("ErrorBrush");
            InfoBannerText.Foreground = (Brush)FindResource("ErrorBrush");
            return;
        }

        InfoBannerBorder.Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF8, 0xE9));
        InfoBannerBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xE1, 0xA5));
        InfoBannerText.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x69, 0x1E));
    }

    private void RenderInfoBanner()
    {
        if (!string.IsNullOrWhiteSpace(_infoError))
        {
            ApplyInfoBannerChrome(isError: true);
            _infoBannerFullText = _infoError;
            InfoBannerText.Text = _infoBannerFullText;
        }
        else
        {
            ApplyInfoBannerChrome(isError: false);
            _infoBannerFullText = BuildDefaultInfoText();
            InfoBannerText.Text = _infoBannerFullText;
        }

        ScheduleInfoBannerOverflowCheck();
    }

    private void ScheduleInfoBannerOverflowCheck() =>
        Dispatcher.BeginInvoke(UpdateInfoBannerOverflow, DispatcherPriority.Loaded);

    private void InfoBannerBorder_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateInfoBannerOverflow();

    private void UpdateInfoBannerOverflow()
    {
        InfoBannerText.MaxHeight = double.PositiveInfinity;

        if (string.IsNullOrWhiteSpace(_infoBannerFullText))
        {
            InfoBannerMoreButton.Visibility = Visibility.Collapsed;
            InfoBannerBorder.Height = InfoBannerHeightNormal;
            return;
        }

        var width = Math.Max(0, InfoBannerBorder.ActualWidth - 20);
        if (width < 40)
        {
            InfoBannerMoreButton.Visibility = Visibility.Collapsed;
            InfoBannerBorder.Height = InfoBannerHeightNormal;
            return;
        }

        var formatted = MeasureInfoBannerText(width);
        var isError = !string.IsNullOrWhiteSpace(_infoError);

        if (!isError)
        {
            InfoBannerMoreButton.Visibility = Visibility.Collapsed;
            InfoBannerBorder.Height = InfoBannerHeightNormal;
            return;
        }

        var innerMax = InfoBannerErrorTextMaxHeight;
        var needsMore = formatted.Height > innerMax + 0.5;
        InfoBannerMoreButton.Visibility = needsMore ? Visibility.Visible : Visibility.Collapsed;
        InfoBannerBorder.Height = needsMore ? InfoBannerHeightWithMoreButton : InfoBannerHeightNormal;
        if (needsMore)
            InfoBannerText.MaxHeight = innerMax;
    }

    private FormattedText MeasureInfoBannerText(double width)
    {
        var brush = InfoBannerText.Foreground ?? Brushes.Black;
        var typeface = new Typeface(
            InfoBannerText.FontFamily,
            InfoBannerText.FontStyle,
            InfoBannerText.FontWeight,
            InfoBannerText.FontStretch);

        return new FormattedText(
            _infoBannerFullText,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            InfoBannerText.FontSize,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = width,
            LineHeight = InfoBannerText.LineHeight > 0 ? InfoBannerText.LineHeight : 13
        };
    }

    private void InfoBannerMoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_infoBannerFullText))
            return;

        var title = !string.IsNullOrWhiteSpace(_infoError) ? "Сообщение об ошибке" : "Информация";
        if (!string.IsNullOrWhiteSpace(_infoError))
            AppDialog.Error(this, _infoBannerFullText, AppPaths.DataDirectory, title);
        else
            AppDialog.Info(this, _infoBannerFullText, title);
    }

    private void ApplyHistoryToInfoBanner(BackupHistory history)
    {
        if (!string.IsNullOrWhiteSpace(_infoError))
            return;

        if (history.LastAttempt != null && !history.LastAttempt.Success
            && !string.IsNullOrWhiteSpace(history.LastAttempt.Error))
        {
            SetInfoError($"Последняя попытка: {history.LastAttempt.Error}");
        }
    }

    private string BuildUsbNotReadyHint()
    {
        var usb = _config.Usb;
        if (!UsbSettingsHelper.ExpectsSpecificDriveLetter(usb))
        {
            var removableOnly = _config.BackupTarget?.RemovableOnly ?? true;
            return removableOnly
                ? "Вставьте съёмный USB-накопитель"
                : "Подключите диск или выберите букву в настройках";
        }

        var letter = DriveLetterHelper.Normalize(usb?.DriveLetter);
        if (string.IsNullOrEmpty(letter))
            return "Укажите диск в настройках (вкладка «Носитель»)";

        return $"Ожидается только диск {letter}: (выбран в настройках)";
    }

    private void RefreshUsbAndHistory()
    {
        try
        {
            RefreshUsbAndHistoryCore();
        }
        catch (Exception ex) when (ex is DriveNotFoundException or IOException or UnauthorizedAccessException)
        {
            LogService.WarnGlobal($"Обновление носителя: {ex.Message}");
            var removableOnly = _config.BackupTarget?.RemovableOnly ?? true;
            _destStatus = new BackupDestinationStatus
            {
                UseUsbTarget = _config.BackupTarget?.UseUsbTarget ?? true,
                IsReady = false,
                Message = removableOnly
                    ? "Съёмный диск не найден. Вставьте USB-накопитель."
                    : "Диск недоступен. Подключите накопитель."
            };
            UsbIndicator.Fill = (Brush)FindResource("ErrorBrush");
            UsbStatusText.Text = _destStatus.Message;
            UsbFreeSpaceText.Text = BuildUsbNotReadyHint();
            UsbFreeSpaceText.Foreground = (Brush)FindResource("MutedBrush");
            UpdateBackupButtonState(destinationReady: false);
        }
    }

    private void RefreshUsbAndHistoryCore()
    {
        SyncUsbDriveSelection();
        _destStatus = _destinationService.GetStatus(_config);
        UsbIndicator.Fill = (Brush)FindResource(_destStatus.IsReady ? "SuccessBrush" : "ErrorBrush");
        UsbStatusText.Text = _destStatus.Message;
        if (_destStatus.IsReady)
        {
            UsbFreeSpaceText.Text = $"Свободно {_destStatus.FreeSpaceText} из {_destStatus.TotalSpaceText}";
            UsbFreeSpaceText.Foreground = (Brush)FindResource("TextBrush");
        }
        else
        {
            UsbFreeSpaceText.Text = (_config.BackupTarget?.UseUsbTarget ?? true)
                ? BuildUsbNotReadyHint()
                : "Укажите папку для копий в настройках";
            UsbFreeSpaceText.Foreground = (Brush)FindResource("MutedBrush");
        }

        var history = _historyService.Load();
        if (history.LastSuccess != null)
        {
            LastSuccessText.Text = $"Последний успех: {history.LastSuccess.At:dd.MM.yyyy, HH:mm}";
            LastSuccessText.Foreground = (Brush)FindResource("SuccessBrush");
        }
        else
        {
            LastSuccessText.Text = "Последний успех: ещё не было";
            LastSuccessText.Foreground = (Brush)FindResource("MutedBrush");
        }

        if (history.LastSuccess != null)
        {
            LastAttemptText.Text = history.LastSuccess.Folder;
            LastAttemptText.Foreground = (Brush)FindResource("MutedBrush");
        }
        else
            LastAttemptText.Text = "";

        ApplyHistoryToInfoBanner(history);

        ScheduleBackupEstimateUpdate();
        UpdateBackupButtonState(_destStatus.IsReady);
    }

    private void SyncUsbDriveSelection()
    {
        var useDisk = _config.BackupTarget?.UseUsbTarget ?? true;
        var autoUsb = useDisk && !UsbSettingsHelper.ExpectsSpecificDriveLetter(_config.Usb);
        if (!useDisk || !autoUsb)
        {
            SetUsbNavButtons(visible: false, enabled: false);
            UsbSwitcherHint.Visibility = Visibility.Collapsed;
            return;
        }

        SetUsbNavButtons(visible: true, enabled: false);

        var letters = GetDetectedUsbLetters();
        if (letters.Count == 0)
        {
            UsbSwitcherHint.Visibility = Visibility.Collapsed;
            return;
        }

        _config.Usb ??= new UsbSettings();
        var preferred = NormalizeUsbLetter(_config.Usb.DriveLetter);
        if (string.IsNullOrEmpty(preferred))
            _config.Usb.DriveLetter = letters[0];
        else if (!letters.Contains(preferred))
        {
            // Авто: выбранная на главном буква недоступна — переключим на первую из найденных.
            _config.Usb.DriveLetter = letters[0];
        }

        var canSwitch = letters.Count > 1;
        SetUsbNavButtons(visible: true, enabled: canSwitch);
        if (canSwitch)
        {
            var idx = letters.IndexOf(NormalizeUsbLetter(_config.Usb.DriveLetter));
            UsbSwitcherHint.Text = $"Накопитель {idx + 1} из {letters.Count}";
            UsbSwitcherHint.Visibility = Visibility.Visible;
        }
        else
            UsbSwitcherHint.Visibility = Visibility.Collapsed;
    }

    private void SetUsbNavButtons(bool visible, bool enabled)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        UsbPrevButton.Visibility = visibility;
        UsbNextButton.Visibility = visibility;
        UsbPrevButton.IsEnabled = visible && enabled;
        UsbNextButton.IsEnabled = visible && enabled;
    }

    private List<string> GetDetectedUsbLetters()
    {
        var removableOnly = _config.BackupTarget?.RemovableOnly ?? true;
        return UsbService.DetectBackupDrives(removableOnly)
            .Select(d => NormalizeUsbLetter(d.Name))
            .Where(l => !string.IsNullOrEmpty(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeUsbLetter(string? letter) =>
        DriveLetterHelper.Normalize(letter);

    private void CycleUsbDrive(int delta)
    {
        var letters = GetDetectedUsbLetters();
        if (letters.Count < 2)
            return;

        _config.Usb ??= new UsbSettings();
        var current = NormalizeUsbLetter(_config.Usb.DriveLetter);
        var idx = letters.FindIndex(l => string.Equals(l, current, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
            idx = 0;
        idx = (idx + delta + letters.Count) % letters.Count;
        _config.Usb.DriveLetter = letters[idx];
        RefreshUsbAndHistory();
    }

    private void UsbPrevButton_Click(object sender, RoutedEventArgs e) => CycleUsbDrive(-1);

    private void UsbNextButton_Click(object sender, RoutedEventArgs e) => CycleUsbDrive(1);

    private void RebuildItems()
    {
        ItemsPanel.Children.Clear();
        _sqlChecks.Clear();
        _folderChecks.Clear();
        var allDb = _config.SqlServer.Databases.ToList();
        var allFolders = _config.Folders.ToList();

        if (allDb.Count == 0 && allFolders.Count == 0)
        {
            EmptyItemsText.Text = "Нет элементов для копирования. Добавьте базы SQL и папки в «Настройках».";
            EmptyItemsText.Visibility = Visibility.Visible;
            ItemsScroll.Visibility = Visibility.Collapsed;
            BackupEstimateText.Text = "";
            _spaceInsufficient = false;
            UpdateBackupButtonState(_destStatus.IsReady);
            UpdateSelectedCount();
            if (!_sqlSyncInProgress)
                UpdateDiscoveryIndicators(searching: false);
            return;
        }
        EmptyItemsText.Visibility = Visibility.Collapsed;
        ItemsScroll.Visibility = Visibility.Visible;

        UserSelection? saved = _config.Ui.RememberLastSelection ? _configService.LoadSelection() : null;

        foreach (var db in allDb)
        {
            var label = string.IsNullOrWhiteSpace(db.DisplayName) ? db.Name : db.DisplayName;
            var cb = CreateItemCheckBox(
                $"SQL: {label}",
                db.Name,
                saved != null ? saved.Sql.Contains(db.Name) : true);
            _sqlChecks[db.Name] = cb;
            ItemsPanel.Children.Add(cb);
        }

        foreach (var folder in allFolders)
        {
            var parts = new List<string>();
            if (folder.Archive) parts.Add("ZIP");
            if (folder.Has1CDatabase) parts.Add(folder.CopyOnly1cdBody ? "1С · только база" : "1С");
            var suffix = parts.Count > 0 ? "  [" + string.Join(", ", parts) + "]" : "  [папка]";
            var label = string.IsNullOrWhiteSpace(folder.DisplayName) ? folder.Path : folder.DisplayName;
            var cb = CreateItemCheckBox(
                $"Папка: {label}{suffix}",
                folder.Id,
                IsFolderCheckedOnMain(folder, saved));
            _folderChecks[folder.Id] = cb;
            ItemsPanel.Children.Add(cb);
        }

        ScheduleBackupEstimateUpdate();
        UpdateBackupButtonState(_destStatus.IsReady);
        UpdateSelectedCount();
        if (!_sqlSyncInProgress)
            UpdateDiscoveryIndicators(searching: false);
    }

    private bool IsFolderCheckedOnMain(FolderEntry folder, UserSelection? saved)
    {
        if (!_config.Ui.RememberLastSelection)
            return true;

        if (_newFolderIdsUncheckedOnMain.Contains(folder.Id))
            return false;

        return saved?.Folders.Contains(folder.Id) ?? false;
    }

    private void RegisterNewMainListFolderIds(IEnumerable<string> newFolderIds)
    {
        foreach (var id in newFolderIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
                _newFolderIdsUncheckedOnMain.Add(id);
        }
    }

    private void RefreshNewFolderIdsAfterSelectionSaved()
    {
        if (!_config.Ui.RememberLastSelection)
            return;

        var saved = _configService.LoadSelection();
        _newFolderIdsUncheckedOnMain.RemoveWhere(id => saved.Folders.Contains(id));
    }

    private CheckBox CreateItemCheckBox(string content, string tag, bool isChecked)
    {
        var cb = new CheckBox
        {
            Content = content,
            Tag = tag,
            IsChecked = isChecked,
            Margin = new Thickness(0, 4, 0, 4),
            FontSize = 13
        };
        cb.Checked += (_, _) => { UpdateSelectedCount(); ScheduleBackupEstimateUpdate(); };
        cb.Unchecked += (_, _) => { UpdateSelectedCount(); ScheduleBackupEstimateUpdate(); };
        return cb;
    }

    private List<string> GetSelectedSql() =>
        _sqlChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();

    private List<string> GetSelectedFolderIds() =>
        _folderChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();

    private List<string> GetSelectedFileIds() =>
        _config.Files.Select(f => f.Id).ToList();

    private void ScheduleBackupEstimateUpdate()
    {
        _estimateCts?.Cancel();
        _estimateCts?.Dispose();
        _estimateCts = new CancellationTokenSource();
        var token = _estimateCts.Token;
        _ = UpdateBackupEstimateAsync(token);
    }

    private async Task UpdateBackupEstimateAsync(CancellationToken ct)
    {
        var selectedSql = GetSelectedSql();
        var selectedFolders = GetSelectedFolderIds();
        var selectedFiles = GetSelectedFileIds();

        if (selectedSql.Count == 0 && selectedFolders.Count == 0 && selectedFiles.Count == 0)
        {
            UiThread.Run(() =>
            {
                BackupEstimateText.Text = _destStatus.IsReady
                    ? "Отметьте, что копировать — покажем нужный объём"
                    : "";
                BackupEstimateText.Foreground = (Brush)FindResource("MutedBrush");
                _spaceInsufficient = false;
                if (string.IsNullOrWhiteSpace(_infoError))
                    RenderInfoBanner();
                UpdateBackupButtonState(_destStatus.IsReady);
            });
            return;
        }

        UiThread.Run(() =>
        {
            BackupEstimateText.Text = "Подсчёт размера копии…";
            BackupEstimateText.Foreground = (Brush)FindResource("MutedBrush");
            if (string.IsNullOrWhiteSpace(_infoError))
                RenderInfoBanner();
        });

        try
        {
            var estimate = await _orchestrator.EstimateSelectionAsync(
                _config, selectedSql, selectedFolders, selectedFiles, ct);

            if (ct.IsCancellationRequested)
                return;

            var free = _destStatus.FreeBytes > 0
                ? _destStatus.FreeBytes
                : BackupSpaceHelper.GetAvailableBytes(_destStatus.BackupBasePath);

            _spaceInsufficient = free > 0 && free < estimate.RequiredBytes;
            var line = BackupSpaceHelper.FormatSizeLine(
                estimate.RequiredBytes, free, _destStatus.UseUsbTarget);

            UiThread.Run(() =>
            {
                BackupEstimateText.Text = line;
                BackupEstimateText.Foreground = (Brush)FindResource("SuccessBrush");
                if (_spaceInsufficient)
                {
                    _infoErrorFromSpaceEstimate = true;
                    SetInfoError(line);
                }
                else if (_infoErrorFromSpaceEstimate)
                {
                    _infoErrorFromSpaceEstimate = false;
                    ClearInfoError();
                    ApplyHistoryToInfoBanner(_historyService.Load());
                }
                else
                    RenderInfoBanner();
                UpdateBackupButtonState(_destStatus.IsReady);
            });
        }
        catch (OperationCanceledException)
        {
            // новый запрос оценки
        }
        catch (Exception ex)
        {
            LogService.Warn($"Size estimate UI: {ex.Message}");
            UiThread.Run(() =>
            {
                BackupEstimateText.Text = "";
                _spaceInsufficient = false;
                SetInfoError("Не удалось оценить размер копии.");
                UpdateBackupButtonState(_destStatus.IsReady);
            });
        }
    }

    private void UpdateBackupButtonState(bool destinationReady)
    {
        var anyChecked = _sqlChecks.Values.Any(c => c.IsChecked == true)
                         || _folderChecks.Values.Any(c => c.IsChecked == true)
                         || _config.Files.Count > 0;
        var copying = BackupButton.Content.ToString() == "Копирование...";
        BackupButton.IsEnabled = destinationReady && anyChecked && !_spaceInsufficient && !copying;
    }

    private async void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedSql = GetSelectedSql();
        var selectedFolders = GetSelectedFolderIds();
        var selectedFiles = GetSelectedFileIds();

        if (selectedSql.Count == 0 && selectedFolders.Count == 0 && selectedFiles.Count == 0)
        {
            AppDialog.Info(this, "Выберите хотя бы один элемент для копирования.");
            return;
        }

        if (!_destStatus.IsReady)
        {
            var useUsb = _config.BackupTarget?.UseUsbTarget ?? true;
            var title = useUsb ? "USB не готов" : "Папка назначения";
            AppDialog.Warning(this, _destStatus.Message, title);
            return;
        }

        try
        {
            var estimate = await _orchestrator.EstimateSelectionAsync(
                _config, selectedSql, selectedFolders, selectedFiles);
            var spaceMsg = BackupOrchestrator.TryGetInsufficientSpaceMessage(_destStatus, estimate.RequiredBytes);
            if (spaceMsg != null)
            {
                AppDialog.Warning(this, spaceMsg, "Недостаточно места");
                ScheduleBackupEstimateUpdate();
                return;
            }
        }
        catch (Exception ex)
        {
            AppDialog.Warning(this,
                $"Не удалось проверить место на диске:\n{ex.Message}\n\nБэкап можно попробовать снова.",
                "Оценка размера");
        }

        BackupButton.IsEnabled = false;
        BackupButton.Content = "Копирование...";

        BackupProgressWindow? progressWindow = null;
        using var backupCts = new CancellationTokenSource();
        try
        {
            progressWindow = new BackupProgressWindow(backupCts) { Owner = this };
            progressWindow.Show();

            var progress = new Progress<BackupProgressReport>(r => progressWindow.Update(r));
            progressWindow.BeginBackupRun();

            var result = await _orchestrator.RunAsync(
                _config, selectedSql, selectedFolders, selectedFiles, progress, ct: backupCts.Token);
            if (!backupCts.IsCancellationRequested
                && result.IsFolderConflict
                && !string.IsNullOrWhiteSpace(result.AlternateArchivePath))
            {
                if (AppDialog.Question(this,
                        $"{result.Message}\n\nСоздать копию в отдельной папке?\n{result.AlternateArchivePath}",
                        "Папка уже существует"))
                {
                    progressWindow.BeginBackupRun();
                    result = await _orchestrator.RunAsync(
                        _config, selectedSql, selectedFolders, selectedFiles, progress,
                        archivePathOverride: result.AlternateArchivePath,
                        ct: backupCts.Token);
                }
            }

            progressWindow.Close();
            progressWindow = null;

            if (result.IsSuccess)
            {
                var useUsb = _config.BackupTarget?.UseUsbTarget ?? true;
                var done = useUsb ? "Резервная копия создана. Можно извлечь USB." : "Резервная копия создана.";
                ClearInfoError();
                SetInfoNotice(done);
                var usbHint = useUsb ? "\n\nМожно извлечь USB." : "";
                AppDialog.Success(this,
                    $"Резервная копия создана.\n\n{result.Message}{usbHint}\n\n{CompanyInfo.SupportHint}",
                    "Готово");
            }
            else if (result.IsCancelled)
            {
                RenderInfoBanner();
                AppDialog.Info(this, result.Message, "Отменено");
            }
            else
            {
                SetInfoError(result.Message);
                AppDialog.Error(this, result.Message, result.LogFolderPath ?? AppPaths.DataDirectory);
            }
        }
        catch (OperationCanceledException)
        {
            RenderInfoBanner();
            AppDialog.Info(this, "Резервное копирование отменено.", "Отменено");
        }
        catch (Exception ex)
        {
            LogService.Error($"Backup: {ex}");
            SetInfoError(ex.Message);
            AppDialog.Error(this, ex.Message, AppPaths.DataDirectory);
        }
        finally
        {
            progressWindow?.Close();
            BackupButton.Content = "Сделать резервную копию";
            RefreshNewFolderIdsAfterSelectionSaved();
            RefreshUsbAndHistory();
            RebuildItems();
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) =>
        UriHelper.Hyperlink_RequestNavigate(sender, e);

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsWindow dlg;
        try
        {
            dlg = new SettingsWindow(_config) { Owner = this };
        }
        catch (Exception ex)
        {
            LogService.Error($"Settings open: {ex}");
            AppDialog.Error(this, $"Не удалось открыть настройки:\n{ex.Message}", AppPaths.DataDirectory);
            return;
        }

        if (dlg.ShowDialog() == true)
        {
            var folderIdsBefore = _config.Folders.Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _config = dlg.Config;
            if (_config.Ui.AutoAddOneCDatabases)
            {
                var oneCResult = _oneCSync.Sync(_config);
                RegisterNewMainListFolderIds(oneCResult.NewFolderIds);
            }

            foreach (var id in _config.Folders
                         .Where(f => !folderIdsBefore.Contains(f.Id))
                         .Select(f => f.Id))
                _newFolderIdsUncheckedOnMain.Add(id);

            _configService.Save(_config);
            ClearInfoError();
            RebuildItems();
            RefreshUsbAndHistory();
            _foldersDiscoveryTooltip = BuildFoldersDiscoveryTooltip();
            UpdateDiscoveryIndicators(searching: false);
        }
    }
}
