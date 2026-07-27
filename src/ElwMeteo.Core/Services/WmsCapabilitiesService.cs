using System.Xml.Linq;

namespace ElwMeteo.Core.Services;

/// <summary>A layer a WMS server advertises in its capabilities document.</summary>
public sealed record WmsLayerInfo(string Name, string Title, string? Abstract)
{
    /// <summary>Name without the workspace prefix, e.g. "Niederschlagsradar".</summary>
    public string ShortName
    {
        get
        {
            int colon = Name.IndexOf(':');
            return colon >= 0 && colon < Name.Length - 1 ? Name[(colon + 1)..] : Name;
        }
    }
}

/// <summary>
/// Reads a WMS GetCapabilities document and reports which layers the server
/// actually offers.
///
/// The DWD renames layers when a product changes, and a wrong name fails
/// invisibly: the server answers with an error graphic that Leaflet accepts as a
/// perfectly good tile. Asking the server what it has turns that silent failure
/// into a statement, and keeps working across renames without a code change.
/// </summary>
public sealed class WmsCapabilitiesService(HttpClient httpClient)
{
    public async Task<IReadOnlyList<WmsLayerInfo>> GetLayersAsync(
        string wmsBaseUrl,
        CancellationToken cancellationToken = default)
    {
        string url = $"{wmsBaseUrl}?service=WMS&version=1.3.0&request=GetCapabilities";

        try
        {
            await using var stream = await httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
            XDocument document = await XDocument
                .LoadAsync(stream, LoadOptions.None, cancellationToken)
                .ConfigureAwait(false);

            return ParseLayers(document);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Xml.XmlException or TaskCanceledException)
        {
            throw new CapabilitiesException($"Layerliste nicht abrufbar: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Extracts every named layer. Element names are matched without their
    /// namespace, because WMS 1.1.1 and 1.3.0 disagree on it and servers are
    /// inconsistent about which they return for a given request.
    /// </summary>
    internal static List<WmsLayerInfo> ParseLayers(XDocument document)
    {
        var layers = new List<WmsLayerInfo>();

        if (document.Root is null)
        {
            return layers;
        }

        foreach (XElement element in document.Root.Descendants()
                     .Where(e => e.Name.LocalName == "Layer"))
        {
            // Container layers carry a title but no name and cannot be requested.
            string? name = Child(element, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            layers.Add(new WmsLayerInfo(
                name.Trim(),
                Child(element, "Title")?.Trim() ?? name.Trim(),
                Child(element, "Abstract")?.Trim()));
        }

        return layers
            .GroupBy(l => l.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(l => l.Title, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>Direct child element value by local name.</summary>
    private static string? Child(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;
}

public sealed class CapabilitiesException(string message, Exception? inner = null)
    : Exception(message, inner);
