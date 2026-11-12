using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestEngineer.Desktop.Gnss;

namespace TestEngineer.Desktop.ViewModels;

public sealed partial class NmeaColorPickerRow : ObservableObject
{
    public NmeaColorPickerRow(string sourcePath, string displayName, KmlColorEntry initial)
    {
        SourcePath = sourcePath;
        DisplayName = displayName;
        _selectedColor = initial;
    }

    public string SourcePath { get; }

    public string DisplayName { get; }

    public IReadOnlyList<KmlColorEntry> Palette { get; } = KmlColorUiPalette.Entries;

    [ObservableProperty] private KmlColorEntry _selectedColor;

    public string KmlColorAabbggrr => SelectedColor.KmlAabbggrr;

    [RelayCommand]
    private void Randomize(ISet<string>? exclude)
    {
        var kml = KmlColorUiPalette.PickRandom(exclude);
        SelectedColor = KmlColorUiPalette.FindByKml(kml) ?? KmlColorUiPalette.Entries[0];
    }
}

public sealed partial class NmeaColorPickerViewModel : ObservableObject
{
    public NmeaColorPickerViewModel(IEnumerable<(string Path, string DisplayName)> files)
    {
        var list = files.ToList();
        var colors = KmlColorUiPalette.AllocateDistinct(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            var entry = KmlColorUiPalette.FindByKml(colors[i]) ?? KmlColorUiPalette.Entries[0];
            Rows.Add(new NmeaColorPickerRow(list[i].Path, list[i].DisplayName, entry));
        }
    }

    public ObservableCollection<NmeaColorPickerRow> Rows { get; } = [];

    public IReadOnlyDictionary<string, string> BuildResult()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows)
        {
            map[row.SourcePath] = row.KmlColorAabbggrr;
            used.Add(row.KmlColorAabbggrr);
        }

        return map;
    }

    [RelayCommand]
    private void RandomizeRow(NmeaColorPickerRow? row)
    {
        if (row is null)
        {
            return;
        }

        var used = Rows
            .Where(r => !ReferenceEquals(r, row))
            .Select(r => r.KmlColorAabbggrr)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kml = KmlColorUiPalette.PickRandom(used);
        row.SelectedColor = KmlColorUiPalette.FindByKml(kml) ?? KmlColorUiPalette.Entries[0];
    }

    [RelayCommand]
    private void RandomizeAll()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows)
        {
            var kml = KmlColorUiPalette.PickRandom(used);
            row.SelectedColor = KmlColorUiPalette.FindByKml(kml) ?? KmlColorUiPalette.Entries[0];
            used.Add(kml);
        }
    }
}
