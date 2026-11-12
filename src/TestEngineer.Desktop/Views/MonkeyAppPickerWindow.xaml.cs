using System.Windows;
using System.Windows.Input;
using TestEngineer.Core.Models;

namespace TestEngineer.Desktop.Views;

public partial class MonkeyAppPickerWindow : Window
{
    private readonly List<AppPackage> _all;

    public MonkeyAppPickerWindow(IReadOnlyList<AppPackage> apps, string? currentPackage)
    {
        InitializeComponent();
        _all = apps.ToList();
        ApplyFilter(string.Empty);
        if (!string.IsNullOrWhiteSpace(currentPackage))
        {
            var match = _all.FirstOrDefault(p => string.Equals(p.PackageName, currentPackage, StringComparison.Ordinal));
            if (match is not null)
            {
                AppList.SelectedItem = match;
                AppList.ScrollIntoView(match);
            }
        }
    }

    public string? SelectedPackageName => (AppList.SelectedItem as AppPackage)?.PackageName;

    public string? SelectedDisplayName => (AppList.SelectedItem as AppPackage)?.DisplayName;

    private void OnFilterChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => ApplyFilter(FilterBox.Text);

    private void ApplyFilter(string text)
    {
        var query = text.Trim();
        IEnumerable<AppPackage> shown = _all;
        if (query.Length > 0)
        {
            shown = _all.Where(p =>
                p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || p.PackageName.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var list = shown.ToList();
        AppList.ItemsSource = list;
        var withIcon = list.Count(p => p.HasIcon);
        CountText.Text = query.Length == 0
            ? $"共 {list.Count} 个，其中 {withIcon} 个有图标"
            : $"匹配 {list.Count} 个";
    }

    private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => OkButton.IsEnabled = AppList.SelectedItem is AppPackage;

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AppList.SelectedItem is AppPackage)
        {
            DialogResult = true;
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is AppPackage)
        {
            DialogResult = true;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
