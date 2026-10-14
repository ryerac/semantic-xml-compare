using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace XmlCompare.Models;

public enum DifferenceKind { Equal, Changed, Missing, Container }

public sealed class CompareNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isVisible = true;

    public string Name { get; init; } = "";
    public string Label { get; set; } = "";
    public string Path { get; init; } = "";
    public string?[] Values { get; } = new string?[3];
    public bool[] Present { get; } = new bool[3];
    public ObservableCollection<CompareNode> Children { get; } = [];
    public CompareNode? Parent { get; set; }
    public DifferenceKind Kind { get; set; }
    public int DifferenceCount { get; set; }
    public string DisplayName => DifferenceCount > 0 && Children.Count > 0 ? $"{Label}  ({DifferenceCount})" : Label;
    public string Value1 => Values[0] ?? "";
    public string Value2 => Values[1] ?? "";
    public string Value3 => Values[2] ?? "";
    public string ToolTipText => $"{Path}\n{StatusText}";
    public string StatusText => Kind switch
    {
        DifferenceKind.Equal => "Equal",
        DifferenceKind.Changed => "Changed",
        DifferenceKind.Missing => "Not in every file",
        _ when DifferenceCount > 0 => $"{DifferenceCount} difference(s)",
        _ => "Equal"
    };
    public Brush RowBrush => Kind switch
    {
        DifferenceKind.Changed => new SolidColorBrush(Color.FromRgb(255, 247, 214)),
        DifferenceKind.Missing => new SolidColorBrush(Color.FromRgb(255, 226, 226)),
        DifferenceKind.Container when DifferenceCount > 0 => new SolidColorBrush(Color.FromRgb(237, 242, 255)),
        _ => Brushes.Transparent
    };
    public Brush StatusBrush => Kind switch
    {
        DifferenceKind.Changed => new SolidColorBrush(Color.FromRgb(180, 83, 9)),
        DifferenceKind.Missing => new SolidColorBrush(Color.FromRgb(190, 24, 93)),
        DifferenceKind.Container when DifferenceCount > 0 => new SolidColorBrush(Color.FromRgb(37, 99, 235)),
        _ => new SolidColorBrush(Color.FromRgb(22, 163, 74))
    };
    public Brush StatusBackground => Kind switch
    {
        DifferenceKind.Changed => new SolidColorBrush(Color.FromRgb(254, 243, 199)),
        DifferenceKind.Missing => new SolidColorBrush(Color.FromRgb(252, 231, 243)),
        DifferenceKind.Container when DifferenceCount > 0 => new SolidColorBrush(Color.FromRgb(219, 234, 254)),
        _ => new SolidColorBrush(Color.FromRgb(220, 252, 231))
    };

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set { _isVisible = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
