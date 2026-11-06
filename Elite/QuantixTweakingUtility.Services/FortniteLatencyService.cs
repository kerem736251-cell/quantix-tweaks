using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace QuantixTweakingUtility.Services;

public record FortniteEndpoint(string Region, string Location, string Host);
public record FortniteLatencyResult(int Sent, int Received, double? Average, long? Minimum, long? Maximum)
{
    public double LossPercent => Sent == 0 ? 0 : 100.0 * (Sent - Received) / Sent;
    public string Summary => Average.HasValue
        ? $"Average {Average:0} ms · min {Minimum} / max {Maximum} ms · {LossPercent:0}% loss ({Received}/{Sent} replies)."
        : $"No replies from the test endpoint ({Sent} probes). ICMP may be blocked.";
}

public sealed class FortniteLatencyService
{
    // Epic's published subregion test endpoints; these are not the server for a particular match.
    public static IReadOnlyList<FortniteEndpoint> Endpoints { get; } = Array.AsReadOnly(new[]
    {
        new FortniteEndpoint("Europe", "Germany", "ping-de.ds.on.epicgames.com"),
        new FortniteEndpoint("Europe", "France", "ping-fr.ds.on.epicgames.com"),
        new FortniteEndpoint("Europe", "United Kingdom", "ping-gb.ds.on.epicgames.com"),
        new FortniteEndpoint("North America", "Oregon", "ping-or.ds.on.epicgames.com"),
        new FortniteEndpoint("North America", "N. California", "ping-ncal.ds.on.epicgames.com"),
        new FortniteEndpoint("North America", "Iowa", "ping-ia.ds.on.epicgames.com"),
        new FortniteEndpoint("North America", "Texas", "ping-tx.ds.on.epicgames.com"),
        new FortniteEndpoint("North America", "Mexico", "ping-mx.ds.on.epicgames.com"),
        new FortniteEndpoint("North America", "Ohio", "ping-oh.ds.on.epicgames.com"),
        new FortniteEndpoint("North America", "Virginia", "ping-va.ds.on.epicgames.com"),
        new FortniteEndpoint("Brazil", "São Paulo", "ping-sao.ds.on.epicgames.com"),
        new FortniteEndpoint("Asia", "Tokyo", "ping-tok.ds.on.epicgames.com"),
        new FortniteEndpoint("Oceania", "Sydney", "ping-syd.ds.on.epicgames.com"),
        new FortniteEndpoint("Middle East", "Bahrain", "ping-bah.ds.on.epicgames.com"),
        new FortniteEndpoint("Middle East", "Mumbai", "ping-mum.ds.on.epicgames.com"),
        new FortniteEndpoint("Middle East", "Israel", "ping-il.ds.on.epicgames.com")
    });
    private readonly Func<string, Task<long?>> probe;
    public FortniteLatencyService(Func<string, Task<long?>>? probe = null) => this.probe = probe ?? Probe;
    public async Task<FortniteLatencyResult> TestAsync(FortniteEndpoint endpoint)
    {
        if (!Endpoints.Any(known => known == endpoint)) throw new ArgumentException("Choose a published Fortnite test endpoint.", nameof(endpoint));
        var replies = new List<long>();
        for (int index = 0; index < 3; index++)
        {
            long? value = await probe(endpoint.Host);
            if (value is >= 0) replies.Add(value.Value);
            if (index < 2) await Task.Delay(150);
        }
        return new(3, replies.Count, replies.Count > 0 ? replies.Average() : null,
            replies.Count > 0 ? replies.Min() : null, replies.Count > 0 ? replies.Max() : null);
    }
    private static async Task<long?> Probe(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 2000);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (PingException) { return null; }
        catch (SocketException) { return null; }
    }
}
