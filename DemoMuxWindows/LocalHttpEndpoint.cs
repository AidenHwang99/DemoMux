using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace DemoMux;

internal sealed record LocalHttpEndpoint(int Port, string? UpstreamHost = null)
{
    internal const string Loopback = "127.0.0.1";
    private const string Label = "[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?";
    private static readonly Regex LocalName = new(@"\A(?:" + Label + @"\.)*localhost\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Address = new(@"\Ahttp://(?<host>[a-z0-9.-]+):(?<port>[0-9]{1,5})/?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal string Host => UpstreamHost ?? Loopback;
    internal string Authority => $"{Host}:{Port}";
    internal string AddressText => $"http://{Authority}/";
    internal Uri Url => new(AddressText);
    internal bool Valid => Runtime.ValidProjectPort(Port) && (Host == Loopback || Host.Length <= 253
        && Host.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.') && LocalName.IsMatch(Host));

    internal static LocalHttpEndpoint? Parse(string input)
    {
        var text = input.Trim();
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port))
            return Runtime.ValidProjectPort(port) ? new(port) : null;
        var match = Address.Match(text);
        if (!match.Success || !int.TryParse(match.Groups["port"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out port)) return null;
        var host = match.Groups["host"].Value.ToLowerInvariant();
        var endpoint = new LocalHttpEndpoint(port, host == Loopback ? null : host);
        return endpoint.Valid ? endpoint : null;
    }

    internal HttpRequestMessage Request(Uri address) => new(HttpMethod.Get, new UriBuilder(address) { Host = Loopback, Port = Port }.Uri)
    {
        Headers = { Host = Authority }
    };

    internal bool MatchesOrigin(Uri address) => address.Scheme == "http" && address.Port == Port
        && address.Host.Equals(Host, StringComparison.OrdinalIgnoreCase) && address.UserInfo.Length == 0;

    internal static bool WithinPath(Uri address, string prefix)
    {
        if (!address.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var path = Uri.UnescapeDataString(address.AbsolutePath);
        return path.StartsWith(prefix, StringComparison.Ordinal)
            && !path.Contains('\\') && !path.Split('/').Any(segment => segment is "." or "..");
    }
}
