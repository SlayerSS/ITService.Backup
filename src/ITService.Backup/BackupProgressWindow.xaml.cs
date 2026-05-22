using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ITService.Backup.Helpers;
using ITService.Backup.Models;

namespace ITService.Backup;

public partial class BackupProgressWindow : Window
{
    private const int FactIntervalSeconds = 5;
    private const int ProgressThrottleMs = 150;

    private readonly CancellationTokenSource _cancellation;
    private readonly ItServiceFacts.FactRotator _factRotator;
    private readonly DispatcherTimer _factTimer;
    private readonly DispatcherTimer _progressFlushTimer;

    private bool _finished;
    private BackupProgressReport? _pendingProgress;
    private DateTime _lastProgressApplied = DateTime.MinValue;

    public BackupProgressWindow(CancellationTokenSource cancellation)
    {
        _cancellation = cancellation;
        _factRotator = ItServiceFacts.CreateRotator();
        InitializeComponent();

        _factTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(FactIntervalSeconds)
        };
        _factTimer.Tick += (_, _) => RotateFact();

        _progressFlushTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(ProgressThrottleMs)
        };
        _progressFlushTimer.Tick += (_, _) => FlushPendingProgress();

        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _factTimer.Stop();
            _progressFlushTimer.Stop();
        };
    }

    /// <summary>Сброс UI перед новым запуском (в т.ч. повтор после «папка уже существует»).</summary>
    public void BeginBackupRun()
    {
        UiThread.Run(() =>
        {
            _finished = false;
            _pendingProgress = null;
            _lastProgressApplied = DateTime.MinValue;

            CancelButton.IsEnabled = true;
            CancelButton.Content = "Отменить копирование";

            _factTimer.Stop();
            _progressFlushTimer.Stop();
            _factTimer.Start();
            _progressFlushTimer.Start();

            ShowNextFact(animate: false);
        });
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => BeginBackupRun();

    public void Update(BackupProgressReport report) => UiThread.Run(() => QueueProgress(report));

    private void QueueProgress(BackupProgressReport report)
    {
        if (_finished && !report.IsFinished)
            return;

        if (report.IsFinished)
        {
            _pendingProgress = null;
            ApplyProgress(report);
            return;
        }

        _pendingProgress = report;
        var elapsed = (DateTime.UtcNow - _lastProgressApplied).TotalMilliseconds;
        if (elapsed >= ProgressThrottleMs)
            FlushPendingProgress();
    }

    private void FlushPendingProgress()
    {
        if (_pendingProgress == null || _finished)
            return;

        var elapsed = (DateTime.UtcNow - _lastProgressApplied).TotalMilliseconds;
        if (elapsed < ProgressThrottleMs)
            return;

        ApplyProgress(_pendingProgress);
        _pendingProgress = null;
    }

    private void ApplyProgress(BackupProgressReport report)
    {
        if (_finished && !report.IsFinished)
            return;

        _lastProgressApplied = DateTime.UtcNow;

        var percent = report.IsFinished ? 100 : report.Percent;
        PercentText.Text = $"{percent}%";
        ProgressBar.Value = percent;
        CurrentActionText.Text = report.CurrentAction;
        CompletedList.ItemsSource = report.CompletedActions.ToList();

        if (report.IsFinished && !_finished)
        {
            _finished = true;
            _pendingProgress = null;
            CancelButton.IsEnabled = false;
            _factTimer.Stop();

            FactCaptionText.Text = report.IsSuccess ? "Готово!" : "Нужна помощь?";
            SetFactText(
                report.IsSuccess
                    ? $"{CompanyInfo.Vendor}, {CompanyInfo.City} — всем помогаем, и сегодня тоже."
                    : $"{CompanyInfo.Vendor} на связи: {CompanyInfo.Phone}",
                animate: false);
            FactText.Foreground = (Brush)FindResource(report.IsSuccess ? "SuccessBrush" : "ErrorBrush");
            FactPanel.BorderBrush = (Brush)FindResource(report.IsSuccess ? "SuccessBrush" : "ErrorBrush");
        }
    }

    private void RotateFact()
    {
        if (_finished)
            return;

        ShowNextFact(animate: true);
    }

    private void ShowNextFact(bool animate)
    {
        if (_finished)
            return;

        SetFactText(_factRotator.Next(), animate);
        FactText.Foreground = (Brush)FindResource("TextBrush");
        FactPanel.BorderBrush = (Brush)FindResource("PrimaryBrush");
        FactCaptionText.Text = "Факты о нас · IT Service";
        FactScroll.ScrollToTop();
    }

    private void SetFactText(string text, bool animate)
    {
        FactText.BeginAnimation(UIElement.OpacityProperty, null);

        if (!animate)
        {
            FactText.Opacity = 1;
            FactText.Text = text;
            FactScroll.ScrollToTop();
            return;
        }

        FactText.Opacity = 0.35;
        FactText.Text = text;
        var fadeIn = new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(350))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        fadeIn.Completed += (_, _) =>
        {
            FactText.Opacity = 1;
            FactScroll.ScrollToTop();
        };
        FactText.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_finished)
            return;

        CancelButton.IsEnabled = false;
        CancelButton.Content = "Отменяем…";
        _factTimer.Stop();
        FactCaptionText.Text = "Отмена";
        SetFactText("Останавливаем копирование…", animate: false);
        _cancellation.Cancel();
    }
}
