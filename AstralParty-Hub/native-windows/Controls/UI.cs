using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Data;
using System.Globalization;

namespace XingShu.Native.Controls;

public sealed class ContentWidth : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is double width && double.IsFinite(width) && width > 0 ? Math.Max(120, width - 100) : double.PositiveInfinity;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public static class UI
{
    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    public static TextBlock Text(string text, double size = 13, string color = "Ink", bool bold = false) => new()
    { Text = text, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap,
      FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 0, 0, 7), LineHeight = size * 1.55 };
    public static StackPanel Stack(params UIElement[] children)
    { var stack = new StackPanel(); foreach (var c in children) stack.Children.Add(c); return stack; }
    public static WrapPanel Row(params UIElement[] children)
    { var row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center }; foreach (var c in children) row.Children.Add(c); return row; }
    public static Border Card(UIElement child, Thickness? padding = null) => new()
    { Child = child, Padding = padding ?? new Thickness(19), Background = Brush("Surface"), BorderBrush = Brush("Line"),
      BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 12, 14) };
    public static Button Button(string label, Action click, bool primary = false)
    {
        var b = new Button { Content = label, Style = (Style)Application.Current.Resources[primary ? "Primary" : typeof(Button)] };
        AutomationProperties.SetName(b, label); b.Click += (_, _) => click(); return b;
    }
    public static TextBox Input(string hint, string value = "", double width = 280)
    {
        var box = new TextBox { Text = value, Width = width, ToolTip = hint };
        AutomationProperties.SetName(box, hint); return box;
    }
    public static ComboBox Select(IEnumerable<string> labels, string hint, double width = 200)
    {
        var combo = new ComboBox { Width = width, ItemsSource = labels.ToArray(), SelectedIndex = 0, ToolTip = hint };
        AutomationProperties.SetName(combo, hint); return combo;
    }
    public static Border Badge(string label, string color = "Accent") => new()
    { Background = Brush("Raised"), CornerRadius = new CornerRadius(5), Padding = new Thickness(9, 4, 9, 1),
      Margin = new Thickness(0, 0, 8, 8), Child = Text(label, 11, color, true) };
    public static Image Image(ImageSource? source, double width, double height, string name = "")
    {
        var image = new Image { Source = source, Width = width, Height = height, Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true, UseLayoutRounding = true };
        AutomationProperties.SetName(image, name); return image;
    }
    public static Border Empty(string title, string description) => Card(Stack(Text(title, 18, "Ink", true), Text(description, 13, "Muted")), new Thickness(28));
    public static void Section(Panel panel, string title, UIElement content)
    { panel.Children.Add(Text(title, 18, "Ink", true)); panel.Children.Add(content); }
    public static DataGrid Table(IEnumerable<Dictionary<string, string>> rows, params (string Key, string Title)[] columns)
    {
        var table = new DataGrid { ItemsSource = rows.ToList(), MinHeight = 110, MaxHeight = 580, Margin = new Thickness(0, 0, 12, 14) };
        foreach (var (key, title) in columns)
            table.Columns.Add(new DataGridTextColumn { Header = title, Binding = new System.Windows.Data.Binding($"[{key}]"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 85 });
        return table;
    }
}
