using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using XmlCompare.Models;
using XmlCompare.Services;

namespace XmlCompare;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly string?[] _files = new string?[3];
    private readonly List<CompareNode> _allNodes = [];
    private readonly List<CompareNode> _navigationNodes = [];
    private ComparisonProfile? _selectedProfile;
    private string _status = "Ready";
    private string _absoluteTolerance = "0";
    private string _relativeTolerance = "0";
    private int _navigationIndex = -1;

    public ObservableCollection<string> FileLabels { get; } = ["Choose file 1…", "Choose file 2…", "Optional file 3…"];
    public ObservableCollection<string> ShortFileLabels { get; } = ["File 1", "File 2", "File 3 (optional)"];
    public ObservableCollection<ComparisonProfile> Profiles { get; } = [];
    public ObservableCollection<CompareNode> Roots { get; } = [];

    public ComparisonProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            _selectedProfile = value;
            if (value is not null)
            {
                AbsoluteTolerance = value.AbsoluteTolerance.ToString("G", CultureInfo.InvariantCulture);
                RelativeTolerance = value.RelativeTolerance.ToString("G", CultureInfo.InvariantCulture);
            }
            OnPropertyChanged();
        }
    }
    public string AbsoluteTolerance { get => _absoluteTolerance; set { _absoluteTolerance = value; OnPropertyChanged(); } }
    public string RelativeTolerance { get => _relativeTolerance; set { _relativeTolerance = value; OnPropertyChanged(); } }
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        var directory = Path.Combine(AppContext.BaseDirectory, "Profiles");
        foreach (var profile in ComparisonProfile.LoadAll(directory)) Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault(x => x.Name.Equals("Imago", StringComparison.OrdinalIgnoreCase)) ?? Profiles.FirstOrDefault();
        if (Profiles.Count == 0)
        {
            Profiles.Add(new ComparisonProfile());
            SelectedProfile = Profiles[0];
            Status = "No profile files found; using built-in generic defaults.";
        }
    }

    private void File1_Click(object sender, RoutedEventArgs e) => ChooseFile(0);
    private void File2_Click(object sender, RoutedEventArgs e) => ChooseFile(1);
    private void File3_Click(object sender, RoutedEventArgs e) => ChooseFile(2);

    private void ChooseFile(int slot)
    {
        var dialog = new OpenFileDialog { Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) SetFile(slot, dialog.FileName);
    }

    private void SetFile(int slot, string path)
    {
        _files[slot] = path;
        FileLabels[slot] = path;
        ShortFileLabels[slot] = Path.GetFileName(path);
        Status = $"Loaded {Path.GetFileName(path)}";
    }

    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_files[0]) || string.IsNullOrWhiteSpace(_files[1]))
        {
            MessageBox.Show(this, "Choose file 1 and file 2. File 3 is optional.", "XML Compare", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var paths = _files.Take(string.IsNullOrWhiteSpace(_files[2]) ? 2 : 3).Cast<string>().ToList();
        if (SelectedProfile is null ||
            !double.TryParse(AbsoluteTolerance, NumberStyles.Float, CultureInfo.InvariantCulture, out var absolute) || absolute < 0 ||
            !double.TryParse(RelativeTolerance, NumberStyles.Float, CultureInfo.InvariantCulture, out var relative) || relative < 0)
        {
            MessageBox.Show(this, "Tolerances must be non-negative numbers using a decimal point.", "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsEnabled = false;
            Status = "Parsing and comparing…";
            var profile = SelectedProfile;
            var root = await Task.Run(() => new ComparisonEngine().Compare(paths, profile, absolute, relative));
            Roots.Clear();
            Roots.Add(root);
            root.IsExpanded = true;
            RebuildIndexes();
            EmptyMessage.Visibility = Visibility.Collapsed;
            Status = $"{root.DifferenceCount:N0} difference(s) across {_allNodes.Count:N0} logical nodes · {profile.Name} profile";
            ApplyFilter();
            if (_navigationNodes.Count > 0) Navigate(1);
        }
        catch (Exception exception)
        {
            Status = "Comparison failed";
            MessageBox.Show(this, exception.Message, "Unable to compare XML", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void RebuildIndexes()
    {
        _allNodes.Clear();
        foreach (var root in Roots) Flatten(root, _allNodes);
        _navigationIndex = -1;
    }

    private static void Flatten(CompareNode node, List<CompareNode> target)
    {
        target.Add(node);
        foreach (var child in node.Children) Flatten(child, target);
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();
    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (!IsLoaded) return;
        var term = SearchBox.Text.Trim();
        var differencesOnly = DifferencesOnlyBox.IsChecked == true;
        foreach (var root in Roots) ApplyFilter(root, term, differencesOnly);
        _navigationNodes.Clear();
        _navigationNodes.AddRange(_allNodes.Where(x => x.IsVisible && x.Kind is DifferenceKind.Changed or DifferenceKind.Missing));
        _navigationIndex = -1;
    }

    private static bool ApplyFilter(CompareNode node, string term, bool differencesOnly)
    {
        var selfMatchesText = string.IsNullOrEmpty(term) || node.Label.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                              node.Path.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                              node.Values.Any(x => x?.Contains(term, StringComparison.OrdinalIgnoreCase) == true);
        var childVisible = false;
        foreach (var child in node.Children) childVisible |= ApplyFilter(child, term, differencesOnly);
        var selfMatchesDifference = !differencesOnly || node.DifferenceCount > 0;
        node.IsVisible = childVisible || (selfMatchesText && selfMatchesDifference);
        if (childVisible && !string.IsNullOrEmpty(term)) node.IsExpanded = true;
        return node.IsVisible;
    }

    private void Previous_Click(object sender, RoutedEventArgs e) => Navigate(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => Navigate(1);

    private void Navigate(int direction)
    {
        if (_navigationNodes.Count == 0) return;
        _navigationIndex = (_navigationIndex + direction + _navigationNodes.Count) % _navigationNodes.Count;
        var node = _navigationNodes[_navigationIndex];
        foreach (var item in _allNodes) item.IsSelected = false;
        for (var parent = node.Parent; parent is not null; parent = parent.Parent) parent.IsExpanded = true;
        node.IsSelected = true;
        Status = $"Difference {_navigationIndex + 1:N0} of {_navigationNodes.Count:N0} · {node.Path}";
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        var xml = paths.Where(x => Path.GetExtension(x).Equals(".xml", StringComparison.OrdinalIgnoreCase)).Take(3).ToArray();
        for (var i = 0; i < xml.Length; i++) SetFile(i, xml[i]);
        if (xml.Length >= 2) Compare_Click(sender, e);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
