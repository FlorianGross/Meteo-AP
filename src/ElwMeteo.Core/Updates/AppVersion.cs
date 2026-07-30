using System.Globalization;

namespace ElwMeteo.Core.Updates;

/// <summary>
/// A release version, as it appears in a git tag.
///
/// Only the three numbers and an optional pre-release suffix are kept — that is
/// everything the tags carry, and comparing anything else would invent meaning
/// the repository does not have.
/// </summary>
public readonly record struct AppVersion(int Major, int Minor, int Patch, string? PreRelease)
    : IComparable<AppVersion>
{
    public bool IsPreRelease => !string.IsNullOrEmpty(PreRelease);

    public static AppVersion Zero => new(0, 0, 0, null);

    /// <summary>
    /// Parses "1.2.3", "v1.2.3" or "1.2.3-beta.1". Also accepts a fourth part
    /// and drops it: .NET assembly versions are four-part, tags are three.
    /// </summary>
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = Zero;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text.Trim();

        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        string? preRelease = null;
        int dash = value.IndexOf('-', StringComparison.Ordinal);

        if (dash >= 0)
        {
            preRelease = value[(dash + 1)..];
            value = value[..dash];
        }

        // Build metadata is not part of the ordering.
        int plus = value.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            value = value[..plus];
        }

        string[] parts = value.Split('.');

        if (parts.Length < 2 || parts.Length > 4)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor))
        {
            return false;
        }

        int patch = 0;

        if (parts.Length >= 3 &&
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch))
        {
            return false;
        }

        version = new AppVersion(major, minor, patch, string.IsNullOrWhiteSpace(preRelease) ? null : preRelease);
        return true;
    }

    public int CompareTo(AppVersion other)
    {
        int result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);
        if (result != 0)
        {
            return result;
        }

        // 1.2.0 is newer than 1.2.0-beta.1: a release outranks its own pre-releases.
        return (IsPreRelease, other.IsPreRelease) switch
        {
            (false, true) => 1,
            (true, false) => -1,
            (true, true) => string.CompareOrdinal(PreRelease, other.PreRelease),
            _ => 0
        };
    }

    public static bool operator <(AppVersion left, AppVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(AppVersion left, AppVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(AppVersion left, AppVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(AppVersion left, AppVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        PreRelease is null
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-{PreRelease}";
}
