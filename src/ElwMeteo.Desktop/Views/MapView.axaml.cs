using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ElwMeteo.Core.Configuration;
using ElwMeteo.Presentation.Platform;
using ElwMeteo.Presentation.ViewModels;

namespace ElwMeteo.Desktop.Views;

/// <summary>
/// The map, opened in the system browser.
///
/// There is no embedded browser that works the same way on Windows, macOS and
/// Linux, and shipping one would mean bundling a second rendering engine per
/// platform. The Leaflet page is unchanged from the Windows head — it is written
/// out with the current state already applied and handed to whatever browser the
/// machine uses. The controls on the left keep working exactly as before, so the
/// next hand-off carries whatever was just set.
/// </summary>
public partial class MapView : UserControl
{
    private readonly IShellLauncher _shell = new SystemShellLauncher();
    private MapViewModel? _viewModel;

    /// <summary>Latest state pushed by the view model, ready to bake into the page.</summary>
    private string? _state;

    public MapView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Looked up rather than generated: the page is loaded through
    /// AvaloniaXamlLoader, which does not emit fields for x:Name.
    /// </summary>
    private TextBlock Status => this.FindControl<TextBlock>("LauncherStatus")!;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.StateChanged -= OnStateChanged;
        }

        _viewModel = DataContext as MapViewModel;

        if (_viewModel is not null)
        {
            _viewModel.StateChanged += OnStateChanged;

            // The page is written on demand, so the first push has to be asked
            // for rather than waited on.
            _viewModel.NotifyPageReady();
        }
    }

    private void OnStateChanged(string json) => _state = json;

    private void OnOpenMap(object? sender, RoutedEventArgs e)
    {
        try
        {
            string source = Path.Combine(AppContext.BaseDirectory, "Assets", "map.html");

            if (!File.Exists(source))
            {
                Status.Text = $"Kartendatei fehlt: {source}";
                return;
            }

            string html = File.ReadAllText(source);

            if (_state is not null)
            {
                // The page normally waits for the host to call window.elwMeteo.apply.
                // With no host there is nothing to call it, so the state is baked in
                // ahead of the page's own scripts and replayed once it is ready.
                string bootstrap =
                    "<script>window.__elwMeteoState = " + ToJsString(_state) + ";" +
                    "document.addEventListener('DOMContentLoaded', function () {" +
                    "  var tries = 0;" +
                    "  var t = setInterval(function () {" +
                    "    if (window.elwMeteo && window.elwMeteo.apply) {" +
                    "      clearInterval(t); window.elwMeteo.apply(window.__elwMeteoState);" +
                    "    } else if (++tries > 100) { clearInterval(t); }" +
                    "  }, 50);" +
                    "});</script>";

                html = html.Replace("</head>", bootstrap + "</head>", StringComparison.OrdinalIgnoreCase);
            }

            string target = Path.Combine(AppSettings.DefaultDirectory, "Karte");
            Directory.CreateDirectory(target);

            // Beside the page so its relative asset references keep resolving.
            string page = Path.Combine(target, "karte.html");
            File.WriteAllText(page, html, Encoding.UTF8);

            Status.Text = _shell.TryOpen(new Uri(page).AbsoluteUri, out string? error)
                ? string.Empty
                : $"Browser konnte nicht geöffnet werden: {error}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status.Text = $"Karte konnte nicht geschrieben werden: {ex.Message}";
        }
    }

    /// <summary>Escapes the state so it can sit inside a script tag.</summary>
    private static string ToJsString(string value) =>
        System.Text.Json.JsonSerializer.Serialize(value)
            // A literal </script> inside the string would end the tag early.
            .Replace("</", "<\\/", StringComparison.Ordinal);
}
