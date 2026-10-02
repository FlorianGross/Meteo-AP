namespace ElwMeteo.Core.Kiosk;

/// <summary>
/// The unattended rotation: which station is due next, and whether it is due at
/// all right now.
///
/// Two things are worth having outside the view model. The first is that the
/// rotation must not fight the operator — somebody reading a wind direction off
/// the map should not have the view yanked away under their hand, so an
/// interaction pauses the rotation and it only resumes after a stretch of quiet.
/// The second is that the pause has to end on its own: a kiosk that stops
/// rotating for good because a sleeve brushed the touchscreen is a kiosk that
/// shows a stale clock for the rest of the shift.
///
/// Both are decisions about time and state, which is to say both are testable
/// without a window. The view model keeps a timer; this keeps the answer.
/// </summary>
public sealed class TabCarousel
{
    private readonly List<KioskStation> _stations = [];

    /// <summary>The configured rotation, in order.</summary>
    public IReadOnlyList<KioskStation> Stations => _stations;

    /// <summary>Index into <see cref="Stations"/>; 0 when the rotation is empty.</summary>
    public int Position { get; private set; }

    /// <summary>The station currently due, or null when nothing is configured.</summary>
    public KioskStation? Current =>
        _stations.Count == 0 ? null : _stations[Position];

    /// <summary>A rotation needs at least two stations; one station is not a rotation.</summary>
    public bool CanRotate => _stations.Count > 1;

    /// <summary>
    /// Replaces the rotation. The position is kept on the same station where
    /// possible, so changing the dwell time in the settings page does not jump
    /// the view back to the first tab.
    /// </summary>
    public void Configure(IEnumerable<KioskStation> stations)
    {
        string? previous = Current?.Id;

        _stations.Clear();
        _stations.AddRange(stations);

        int index = previous is null
            ? -1
            : _stations.FindIndex(s => s.Id == previous);

        Position = index >= 0 ? index : 0;
    }

    /// <summary>Advances one station and returns it; null when nothing is configured.</summary>
    public KioskStation? Next()
    {
        if (_stations.Count == 0)
        {
            return null;
        }

        Position = (Position + 1) % _stations.Count;
        return _stations[Position];
    }

    /// <summary>
    /// Points the rotation at whichever station shows this tab, so that after a
    /// manual switch the next automatic step continues from where the operator
    /// left off rather than from wherever the rotation happened to be. A tab
    /// outside the rotation — diagnostics, settings — leaves the position alone.
    /// </summary>
    public void SyncToTab(int tabIndex)
    {
        int index = _stations.FindIndex(s => s.TabIndex == tabIndex);

        if (index >= 0)
        {
            Position = index;
        }
    }

    // ----------------------------------------------------- interaction clock

    /// <summary>When the operator last did something; null means nobody has.</summary>
    public DateTimeOffset? LastInteraction { get; private set; }

    /// <summary>True between <see cref="Advance"/> and <see cref="CompleteAdvance"/>.</summary>
    public bool IsAdvancing { get; private set; }

    /// <summary>
    /// Records that somebody is operating the application, which holds the
    /// rotation for the quiet stretch.
    ///
    /// Ignored while the rotation is mid-step, and that is the whole reason this
    /// clock lives here rather than in the view model: switching tabs raises the
    /// same change notification a person's click does. Without the suppression
    /// the rotation would register its own first step as an interaction and never
    /// take a second one — a kiosk that shows one view for the rest of the shift,
    /// which is precisely the failure nobody notices until it is in a vehicle.
    /// </summary>
    public void NoteInteraction(DateTimeOffset now)
    {
        if (IsAdvancing)
        {
            return;
        }

        LastInteraction = now;
    }

    /// <summary>
    /// Takes the next station when one is due, or null to hold. Opens the
    /// suppression window, which <see cref="CompleteAdvance"/> closes.
    /// </summary>
    public KioskStation? Advance(DateTimeOffset now, TimeSpan idleGrace)
    {
        if (!ShouldAdvance(now, LastInteraction, idleGrace))
        {
            return null;
        }

        IsAdvancing = true;
        return Next();
    }

    /// <summary>
    /// Closes the suppression window. The interaction stamp is deliberately left
    /// alone: it is already older than the quiet stretch — that is why the step
    /// happened — so it will not hold the next one either, and leaving it means
    /// the suppression above is the single thing keeping the rotation moving
    /// rather than one of two overlapping mechanisms.
    /// </summary>
    public void CompleteAdvance() => IsAdvancing = false;

    /// <summary>Whether the rotation is currently held by an interaction.</summary>
    public bool IsHeld(DateTimeOffset now, TimeSpan idleGrace) =>
        CanRotate && !ShouldAdvance(now, LastInteraction, idleGrace);

    /// <summary>
    /// Whether the next station is due.
    ///
    /// <paramref name="lastInteraction"/> is when somebody last touched the
    /// interface; null means nobody has. The rotation holds until
    /// <paramref name="idleGrace"/> has passed since then — long enough that
    /// clicking through three tabs by hand does not get interrupted halfway,
    /// and short enough that a screen left alone goes back to rotating within
    /// the same minute.
    /// </summary>
    public bool ShouldAdvance(
        DateTimeOffset now,
        DateTimeOffset? lastInteraction,
        TimeSpan idleGrace)
    {
        if (!CanRotate)
        {
            return false;
        }

        if (lastInteraction is null)
        {
            return true;
        }

        TimeSpan sinceInteraction = now - lastInteraction.Value;

        // A negative span means the clock moved backwards — a GPS time fix
        // landing, or daylight saving. Treat it as "just interacted" rather
        // than as an hour of idleness: holding the view for one interval is a
        // harmless wrong answer, rotating away from somebody mid-sentence is not.
        if (sinceInteraction < TimeSpan.Zero)
        {
            return false;
        }

        return sinceInteraction >= idleGrace;
    }

    /// <summary>Clamps a configured dwell time to something a person can read.</summary>
    public static TimeSpan ClampInterval(int seconds) =>
        TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 3600));

    /// <summary>Clamps the quiet stretch after an interaction.</summary>
    public static TimeSpan ClampIdleGrace(int seconds) =>
        TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 3600));
}
