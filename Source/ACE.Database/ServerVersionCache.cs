using Microsoft.EntityFrameworkCore;

namespace ACE.Database;

internal sealed class ServerVersionCache
{
    private readonly object gate = new();
    private string connection;
    private ServerVersion version;

    internal ServerVersion Get(string connectionString)
    {
        lock (gate)
        {
            if (connection != connectionString)
            {
                // A transient failure leaves the last successful entry intact.
                var detected = ServerVersion.AutoDetect(connectionString);
                version = detected;
                connection = connectionString;
            }
            return version;
        }
    }
}
