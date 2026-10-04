namespace Aegis.Infrastructure.Screening;

using Aegis.Application.Screening;
using Aegis.Modules.Screening.Domain;
using Microsoft.Extensions.Configuration;

/// <summary>Reads Screening:Sources:{UN|OFAC}:Path when set (offline installs, tests), else downloads :Url.</summary>
public sealed class WatchlistDownloader : IWatchlistDownloader
{
    public const string HttpClientName = "watchlists";
    public const long MaxBytes = 100 * 1024 * 1024;

    private static readonly Dictionary<WatchlistSource, string> DefaultUrls = new()
    {
        [WatchlistSource.UN] = "https://scsanctions.un.org/resources/xml/en/consolidated.xml",
        [WatchlistSource.OFAC] = "https://sanctionslistservice.ofac.treas.gov/api/PublicationPreview/exports/SDN.XML"
    };

    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;

    public WatchlistDownloader(IHttpClientFactory http, IConfiguration config)
    {
        _http = http;
        _config = config;
    }

    public async Task<byte[]> DownloadAsync(WatchlistSource source, CancellationToken cancellationToken)
    {
        var path = _config[$"Screening:Sources:{source}:Path"];
        if (!string.IsNullOrWhiteSpace(path)) return await File.ReadAllBytesAsync(path, cancellationToken);

        var url = _config[$"Screening:Sources:{source}:Url"];
        if (string.IsNullOrWhiteSpace(url)) url = DefaultUrls[source];
        using var response = await _http.CreateClient(HttpClientName)
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxBytes)
            throw new InvalidOperationException($"{source} list is larger than {MaxBytes / 1024 / 1024} MB.");
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}
