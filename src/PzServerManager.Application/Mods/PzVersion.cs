namespace PzServerManager.Application.Mods;

internal readonly record struct PzVersion(int Major, int Minor, int Patch) : IComparable<PzVersion>
{
    public int CompareTo(PzVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public static bool TryParse(string? value, out PzVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Trim().Split('.');
        if (parts.Length is < 1 or > 3) return false;
        var numbers = new int[3];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], out numbers[index]) || numbers[index] < 0) return false;
        }
        version = new(numbers[0], numbers[1], numbers[2]);
        return true;
    }
}
