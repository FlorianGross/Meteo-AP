using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ElwMeteo.Desktop.Views;

public partial class ClockView : UserControl
{
    public ClockView() => AvaloniaXamlLoader.Load(this);
}
