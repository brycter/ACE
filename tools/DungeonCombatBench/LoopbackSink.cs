using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

// Drain actual datagrams in a separate process so simulated client work is outside server CPU.
internal sealed class LoopbackSink : IDisposable
{
    private readonly Process child;
    internal IPEndPoint Endpoint { get; }

    internal LoopbackSink()
    {
        var info = new ProcessStartInfo(Environment.ProcessPath) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true
        };
        if (Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        info.ArgumentList.Add("--receive");
        child = Process.Start(info);
        int port = int.Parse(child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult());
        Endpoint = new IPEndPoint(IPAddress.Loopback, port);
    }

    internal long Finish()
    {
        child.StandardInput.Close();
        string count = child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        if (!child.WaitForExit(5000) || child.ExitCode != 0) throw new InvalidOperationException("UDP sink failed.");
        return long.Parse(count);
    }

    public void Dispose()
    {
        if (!child.HasExited) child.StandardInput.Close();
        child.Dispose();
    }

    internal static void Receive()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.ReceiveBufferSize = 4 * 1024 * 1024;
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        Console.WriteLine(((IPEndPoint)socket.LocalEndPoint).Port);
        var stop = Task.Run(() => Console.ReadLine());
        var buffer = new byte[2048];
        long received = 0;
        // After the parent closes stdin, drain all queued loopback datagrams.
        while (socket.Poll(50000, SelectMode.SelectRead) || !stop.IsCompleted)
        {
            if (socket.Available == 0) continue;
            socket.Receive(buffer);
            received++;
        }
        Console.WriteLine(received);
    }
}
