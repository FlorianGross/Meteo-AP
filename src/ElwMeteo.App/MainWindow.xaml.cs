using System.Windows;
using System.Windows.Input;
using ElwMeteo.Presentation.ViewModels;

namespace ElwMeteo.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Preview events, so an interaction inside the embedded browser or a text
        // box counts too. Handled input is included for the same reason: typing
        // into a field is exactly when the rotation must not pull the view away.
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler((_, _) => NoteInteraction()), handledEventsToo: true);
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) => NoteInteraction()), handledEventsToo: true);
        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler((_, _) => NoteInteraction()), handledEventsToo: true);
        AddHandler(PreviewTouchDownEvent, new EventHandler<TouchEventArgs>((_, _) => NoteInteraction()), handledEventsToo: true);
    }

    private void NoteInteraction()
    {
        if (DataContext is MainViewModel main)
        {
            main.NoteInteraction();
        }
    }
}
