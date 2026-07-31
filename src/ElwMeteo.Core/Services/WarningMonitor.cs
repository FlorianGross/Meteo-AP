using ElwMeteo.Core.Models;

namespace ElwMeteo.Core.Services;

/// <summary>Why a warning is worth interrupting somebody for.</summary>
public enum AlertReason
{
    /// <summary>Was not in the previous set at all.</summary>
    New,
    /// <summary>Was already there but has moved up a level.</summary>
    Escalated
}

public sealed record WarningAlert(DwdWarning Warning, AlertReason Reason)
{
    public string Describe() => Reason == AlertReason.Escalated
        ? $"{Warning.Event} verschärft auf {Warning.LevelLabel}"
        : $"Neue Warnung: {Warning.Event} · {Warning.LevelLabel}";
}

/// <summary>
/// Decides which of the warnings currently in force are worth a sound and a
/// flash, and which have already been seen.
///
/// The distinction is the whole point. Warnings are re-fetched every few
/// minutes, and an alert on every fetch trains people to ignore it — at which
/// point the feature is worse than not having it. So only two things count: a
/// warning that was not there before, and one that has gone up a level.
///
/// The first observation after start deliberately alerts about nothing. It only
/// records what is in force. Somebody who has just started the application is
/// looking at the screen; the alert exists for the warning that arrives an hour
/// later, when nobody is.
/// </summary>
public sealed class WarningMonitor
{
    private readonly Dictionary<string, WarningLevel> _seen = [];
    private bool _primed;

    /// <summary>Lowest level that triggers an alert. Below it warnings are shown but stay quiet.</summary>
    public WarningLevel Threshold { get; set; } = WarningLevel.Moderate;

    /// <summary>Number of warnings currently being tracked, for the diagnostics tab.</summary>
    public int TrackedCount => _seen.Count;

    /// <summary>
    /// Folds the current set in and returns what should raise an alarm.
    /// Warnings that have expired are forgotten, so the same event next week
    /// alerts again.
    /// </summary>
    public IReadOnlyList<WarningAlert> Observe(IReadOnlyList<DwdWarning> current)
    {
        var alerts = new List<WarningAlert>();
        var stillActive = new HashSet<string>(StringComparer.Ordinal);

        foreach (DwdWarning warning in current)
        {
            string key = KeyOf(warning);
            stillActive.Add(key);

            bool known = _seen.TryGetValue(key, out WarningLevel previous);
            _seen[key] = warning.Level;

            if (!_primed || warning.Level < Threshold)
            {
                continue;
            }

            if (!known)
            {
                alerts.Add(new WarningAlert(warning, AlertReason.New));
            }
            else if (warning.Level > previous)
            {
                alerts.Add(new WarningAlert(warning, AlertReason.Escalated));
            }
        }

        // Drop what is no longer in force, so a repeat next week is a new warning
        // again and the dictionary does not grow for the life of the process.
        foreach (string gone in _seen.Keys.Where(k => !stillActive.Contains(k)).ToList())
        {
            _seen.Remove(gone);
        }

        _primed = true;

        // Worst first: if several arrive at once, that is the one to announce.
        return alerts.OrderByDescending(a => a.Warning.Level).ToList();
    }

    /// <summary>Forgets everything — used when the position changes far enough to matter.</summary>
    public void Reset()
    {
        _seen.Clear();
        _primed = false;
    }

    /// <summary>
    /// Identity of a warning across refreshes.
    ///
    /// Not the headline: providers reword it on an update while the warning is
    /// the same one. Not the level either, or an upgrade would read as a new
    /// warning and the escalation case could never be told apart. Event, region
    /// and start time are what stay put.
    /// </summary>
    internal static string KeyOf(DwdWarning warning) =>
        string.Join('|',
            warning.Event.Trim().ToLowerInvariant(),
            warning.RegionName.Trim().ToLowerInvariant(),
            warning.Start?.ToUniversalTime().ToString("O") ?? "-");
}
