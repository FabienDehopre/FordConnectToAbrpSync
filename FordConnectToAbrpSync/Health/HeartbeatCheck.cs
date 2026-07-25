using System.Globalization;

namespace FordConnectToAbrpSync.Health;

internal static class HeartbeatCheck
{
    public static bool Run(string path, DateTimeOffset now)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var content = File.ReadAllText(path);
        return DateTimeOffset.TryParse(content, CultureInfo.InvariantCulture, DateTimeStyles.None, out var deadline)
            && now < deadline;
    }
}
