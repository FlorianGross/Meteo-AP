using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ElwMeteo.Presentation.ViewModels;

namespace ElwMeteo.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // Tunnelling handlers, so an interaction inside the embedded browser or a
        // text box still counts. Handled events are included for the same reason:
        // typing into a field is exactly the case where the rotation must not
        // pull the view away.
        AddHandler(KeyDownEvent, OnInteraction, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnInteraction, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, OnInteraction, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnInteraction(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel main)
        {
            main.NoteInteraction();
        }
    }
}
