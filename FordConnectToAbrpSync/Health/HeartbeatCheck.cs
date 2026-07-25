using System.Globalization;

namespace FordConnectToAbrpSync.Health;

internal static class HeartbeatCheck
{
    public static bool Run(string path, DateTimeOffset now)
    {
        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return DateTimeOffset.TryParseExact(content, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var deadline)
            && now < deadline;
    }
}
