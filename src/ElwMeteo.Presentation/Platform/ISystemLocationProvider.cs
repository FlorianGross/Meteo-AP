using ElwMeteo.Core.Models;

namespace ElwMeteo.Presentation.Platform;

/// <summary>What the operating system's location service currently is.</summary>
public enum SystemLocationState
{
    /// <summary>Not asked yet.</summary>
    Unknown,
    /// <summary>This build has no implementation for the running system.</summary>
    Unsupported,
    /// <summary>Switched off system-wide, or blocked for desktop applications.</summary>
    Disabled,
    /// <summary>The operator declined the permission prompt.</summary>
    Denied,
    /// <summary>Usable.</summary>
    Allowed
}

/// <summary>
/// The position the operating system reports, if it has one.
///
/// This sits alongside the NMEA receiver rather than replacing it. On a vehicle
/// with a real GPS puck on a serial port, that receiver stays the better source:
/// metre accuracy, no network, no permission dialogue. What the system service
/// adds is the machine that has no puck — a tablet or a laptop with a built-in
/// GNSS chip, where Windows already knows where it is and nothing else does.
///
/// Deliberately an interface with no Windows types in sight. The Avalonia head
/// runs on three systems whose location APIs share nothing, and the view models
/// must not learn which one they are on.
/// </summary>
public interface ISystemLocationProvider
{
    /// <summary>Name of the underlying service, for the settings page.</summary>
    string Name { get; }

    /// <summary>Last known state; updated by <see cref="RequestAccessAsync"/> and reads.</summary>
    SystemLocationState State { get; }

    /// <summary>Wording for the settings page, including why it is unusable.</summary>
    string StatusText { get; }

    /// <summary>
    /// Asks for permission. Must be called from the interface thread — Windows
    /// shows a consent dialogue the first time, and doing that from a background
    /// thread throws.
    /// </summary>
    Task<SystemLocationState> RequestAccessAsync();

    /// <summary>The current position, or null when there is none to be had.</summary>
    Task<GeoPosition?> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Stands in on systems without an implementation, so the composition roots and
/// the resolver need no null checks and no conditional wiring.
/// </summary>
public sealed class UnsupportedSystemLocationProvider(string reason) : ISystemLocationProvider
{
    public string Name => "Systemortung";

    public SystemLocationState State => SystemLocationState.Unsupported;

    public string StatusText => reason;

    public Task<SystemLocationState> RequestAccessAsync() =>
        Task.FromResult(SystemLocationState.Unsupported);

    public Task<GeoPosition?> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<GeoPosition?>(null);
}
