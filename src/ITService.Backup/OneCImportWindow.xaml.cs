using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ITService.Backup.Helpers;
using ITService.Backup.Models;
using ITService.Backup.Services;

namespace ITService.Backup;

public partial class OneCImportWindow : Window
{
    private readonly List<OneCDiscoveredPath> _paths;
    private readonly OneCPathImportService _oneCImport = new();

    public IReadOnlyList<OneCDiscoveredPath> SelectedPaths =>
        _paths.Where(p => p.Selected).ToList();

    public OneCImportWindow(IReadOnlyList<OneCDiscoveredPath> paths)
    {
        InitializeComponent();
        _paths = paths.ToList();
        BuildList();
    }

    private void BuildList()
    {
        PathsList.Items.Clear();
        SummaryText.Text = _paths.Count == 0
            ? "Список баз не найден. Нажмите «Указать файл .v8i…» (например ibases.v8i в %AppData%\\1C\\1CEStart) или добавьте папки вручную в настройках."
            : $"Найдено: {_paths.Count}. Файловые базы можно добавить в бэкап; серверные (Srvr/Ref) — через вкладку SQL.";

        foreach (var item in _paths)
        {
            var row = new Border
            {
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6),
                Background = (Brush)FindResource("BgBrush"),
                BorderBrush = (Brush)FindResource("MutedBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var cb = new CheckBox
            {
                IsChecked = item.Selected,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 10, 0),
                IsEnabled = item.Kind == OneCPathKind.File
            };
            cb.Checked += (_, _) => item.Selected = true;
            cb.Unchecked += (_, _) => item.Selected = false;
            Grid.SetColumn(cb, 0);
            grid.Children.Add(cb);

            var text = new StackPanel();
            var title = new TextBlock { FontWeight = FontWeights.SemiBold };
            if (item.Kind == OneCPathKind.File)
                title.Text = item.Name;
            else
            {
                title.Text = $"{item.Name}  [{item.KindLabel}]";
                title.Foreground = (Brush)FindResource("MutedBrush");
            }
            text.Children.Add(title);
            text.Children.Add(new TextBlock
            {
                Text = item.Path,
                FontSize = 11,
                Foreground = (Brush)FindResource("MutedBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
            if (item.Kind != OneCPathKind.File)
            {
                text.Children.Add(new TextBlock
                {
                    Text = "Серверные базы — только вручную через SQL",
                    FontSize = 10,
                    Foreground = (Brush)FindResource("ErrorBrush"),
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }

            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            row.Child = grid;
            PathsList.Items.Add(row);
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var p in _paths.Where(p => p.Kind == OneCPathKind.File))
            p.Selected = true;
        BuildList();
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var p in _paths)
            p.Selected = false;
        BuildList();
    }

    private void BrowseV8iFile_Click(object sender, RoutedEventArgs e)
    {
        if (!OneCV8iFileDialog.TryPick(out var filePath))
            return;

        var fromFile = _oneCImport.DiscoverFromV8iFile(filePath!);
        if (fromFile.Count == 0)
        {
            AppDialog.Warning(this,
                $"В файле не найдено баз с распознанным Connect=:\n{filePath}",
                "Файл .v8i");
            return;
        }

        var added = OneCPathImportService.MergeInto(_paths, fromFile);
        _paths.Sort((a, b) =>
        {
            var kind = a.Kind.CompareTo(b.Kind);
            return kind != 0 ? kind : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        BuildList();

        if (added > 0)
            AppDialog.Info(this, $"Добавлено из файла: {added}. Всего в списке: {_paths.Count}.");
        else
            AppDialog.Info(this, "Все базы из этого файла уже есть в списке.");
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (!SelectedPaths.Any(p => p.Kind == OneCPathKind.File))
        {
            Helpers.AppDialog.Warning(this, "Выберите хотя бы одну файловую базу с путём к каталогу.");
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
