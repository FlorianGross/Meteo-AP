using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ElwMeteo.Desktop.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => AvaloniaXamlLoader.Load(this);
}
