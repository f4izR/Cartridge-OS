using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CartridgeOS.Core.Models;

namespace CartridgeOS.Core.Scanning;

/// <summary>
/// Downloads box art once for games the local scanners couldn't find any for, and caches it to
/// disk so it's never re-fetched. Steam games use Valve's own public CDN (keyed by appid, no auth).
/// Everything else is looked up by title, trying SteamGridDB first then falling back to TheGamesDB
/// (capped at 1000 requests/month — exactly why it's the fallback, not the primary) if SteamGridDB
/// has no match or is rate-limited.
/// </summary>
public static class ArtworkFetcher
{
    // Both API keys, not secrets in any way that matters here: this is a closed-source desktop app,
    // so the key ships inside every install's compiled DLL regardless of whether it's a literal here
    // or read from an env var — an env var buys no real protection, it just adds a manual setup step
    // that breaks artwork for every new install until someone remembers to configure it. Same call as
    // the Discord Client ID in DiscordRichPresence.cs.
    private const string SteamGridDbApiKey = "c9fa5c51eeb057878f6ac31eb2cf80ad";
    private const string TheGamesDbApiKey = "f75af6a40def2957555e427d83ecde8d2c43afd53d94bbe02edf34b7dca7b6c3";

    private const string SteamGridDbSource = "SteamGridDB";
    private const string TheGamesDbSource = "TheGamesDB";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CartridgeOS", "ArtworkCache", "downloaded");

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CartridgeOS", "artwork.log");

    public static async Task<string?> FetchAndCacheAsync(Game game)
    {
        string cachePath = Path.Combine(CacheDir, $"{Sanitize(game.Title)}_{game.Id}.jpg");
        if (File.Exists(cachePath))
        {
            Log($"{game.Title}: already cached at {cachePath}");
            return cachePath;
        }

        string? sourceUrl = TryGetSteamAppId(game.ExecutablePath, out string appId)
            ? $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_600x900.jpg"
            : await FindSteamGridDbUrlAsync(game.Title) ?? await FindTheGamesDbUrlAsync(game.Title);

        if (sourceUrl is null)
        {
            Log($"{game.Title}: no source URL found from any source");
            return null;
        }

        Log($"{game.Title}: downloading {sourceUrl}");
        try
        {
            byte[] bytes = await Http.GetByteArrayAsync(sourceUrl);
            Directory.CreateDirectory(CacheDir);
            await File.WriteAllBytesAsync(cachePath, bytes);
            Log($"{game.Title}: saved to {cachePath} ({bytes.Length} bytes)");
            return cachePath;
        }
        catch (HttpRequestException ex)
        {
            Log($"{game.Title}: download failed — {ex.Message}");
            return null; // 404 (no art for this appid/title) or network issue — keep the placeholder tile
        }
    }

    /// <summary>
    /// Wide banner image for the Home screen's full-screen backdrop — SteamGridDB's "hero" asset type,
    /// purpose-built for exactly this (landscape, ~3:1), unlike the portrait boxart ArtworkPath points to.
    /// SteamGridDB-only (no TheGamesDB fallback here) — fetched lazily per-game by the caller, not eagerly
    /// for the whole library, so this alone doesn't multiply request volume the way boxart fetching would.
    /// </summary>
    public static async Task<string?> FetchHeroAndCacheAsync(Game game)
    {
        string cachePath = Path.Combine(CacheDir, $"{Sanitize(game.Title)}_{game.Id}_hero.jpg");
        if (File.Exists(cachePath))
        {
            Log($"{game.Title}: hero already cached at {cachePath}");
            return cachePath;
        }

        if (!RateLimiter.IsAvailable(SteamGridDbSource))
        {
            Log($"{game.Title}: SteamGridDB is cooling down after a recent 429, skipping hero fetch");
            return null;
        }

        string? url;
        try
        {
            // Steam games can be looked up directly by their real appid — skips the fuzzy title-search step
            // entirely and is more reliable than it for anything with an ambiguous or common title.
            if (TryGetSteamAppId(game.ExecutablePath, out string appId))
            {
                url = await FetchSteamGridDbHeroUrlAsync($"steam/{appId}", game.Title);
            }
            else
            {
                int? gameId = await FindSteamGridDbGameIdAsync(game.Title);
                url = gameId is null ? null : await FetchSteamGridDbHeroUrlAsync($"game/{gameId}", game.Title);
            }
        }
        catch (HttpRequestException ex)
        {
            Log($"{game.Title}: SteamGridDB hero request failed — {ex.Message}");
            return null;
        }

        if (url is null) return null;

        Log($"{game.Title}: downloading hero {url}");
        try
        {
            byte[] bytes = await Http.GetByteArrayAsync(url);
            Directory.CreateDirectory(CacheDir);
            await File.WriteAllBytesAsync(cachePath, bytes);
            Log($"{game.Title}: hero saved to {cachePath} ({bytes.Length} bytes)");
            return cachePath;
        }
        catch (HttpRequestException ex)
        {
            Log($"{game.Title}: hero download failed — {ex.Message}");
            return null; // no hero for this game/appid, or a network issue — caller falls back to boxart
        }
    }

    private static async Task<string?> FetchSteamGridDbHeroUrlAsync(string platformPath, string title)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://www.steamgriddb.com/api/v2/heroes/{platformPath}?dimensions=1920x620,3840x1240");
        request.Headers.Authorization = new("Bearer", SteamGridDbApiKey);
        using var response = await Http.SendAsync(request);
        if (HandleTooManyRequests(response, SteamGridDbSource, title)) return null;
        if (!response.IsSuccessStatusCode)
        {
            Log($"{title}: SteamGridDB heroes/{platformPath} returned {(int)response.StatusCode} {response.ReasonPhrase}");
            return null;
        }
        // Same {data:[{url:...}]} shape as the grids response — reused rather than duplicating an identical DTO.
        var heroes = await response.Content.ReadFromJsonAsync<SteamGridDbGridsResponse>();
        string? url = heroes?.Data?.FirstOrDefault()?.Url;
        if (url is null) Log($"{title}: SteamGridDB matched but has no hero image for {platformPath}");
        return url;
    }

    // ponytail: plain append-to-file log, no rotation — this file stays tiny (one line per game per scan).
    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
    }

    private static bool TryGetSteamAppId(string executablePath, out string appId)
    {
        const string prefix = "steam://rungameid/";
        if (executablePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            appId = executablePath[prefix.Length..];
            return true;
        }
        appId = "";
        return false;
    }

    /// <summary>
    /// True (and cooldown recorded) if this was a 429 — caller should treat it exactly like "no match"
    /// and either fall back to the next source or give up, never retry inline and block the caller.
    /// </summary>
    private static bool HandleTooManyRequests(HttpResponseMessage response, string source, string title)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests) return false;

        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        RateLimiter.SetCooldown(source, retryAfter);
        Log($"{title}: {source} returned 429 (rate-limited), backing off {(retryAfter ?? RateLimiter.DefaultCooldown).TotalSeconds:F0}s");
        return true;
    }

    private static async Task<string?> FindSteamGridDbUrlAsync(string title)
    {
        if (!RateLimiter.IsAvailable(SteamGridDbSource))
        {
            Log($"{title}: SteamGridDB is cooling down after a recent 429, skipping — will fall back");
            return null;
        }

        try
        {
            int? gameId = await FindSteamGridDbGameIdAsync(title);
            if (gameId is null) return null;

            using var gridsRequest = new HttpRequestMessage(HttpMethod.Get,
                $"https://www.steamgriddb.com/api/v2/grids/game/{gameId}?dimensions=600x900");
            gridsRequest.Headers.Authorization = new("Bearer", SteamGridDbApiKey);
            using var gridsResponse = await Http.SendAsync(gridsRequest);
            if (HandleTooManyRequests(gridsResponse, SteamGridDbSource, title)) return null;
            if (!gridsResponse.IsSuccessStatusCode)
            {
                Log($"{title}: SteamGridDB grids returned {(int)gridsResponse.StatusCode} {gridsResponse.ReasonPhrase}");
                return null;
            }
            var grids = await gridsResponse.Content.ReadFromJsonAsync<SteamGridDbGridsResponse>();
            string? url = grids?.Data?.FirstOrDefault()?.Url;
            if (url is null) Log($"{title}: SteamGridDB matched game id {gameId} but it has no grid image");
            return url;
        }
        catch (HttpRequestException ex)
        {
            Log($"{title}: SteamGridDB request failed — {ex.Message}");
            return null;
        }
    }

    /// <summary>Shared by the grids (boxart) and heroes (Home banner) lookups — SteamGridDB's fuzzy title
    /// search to resolve its own internal game id. Callers must check RateLimiter.IsAvailable first.</summary>
    private static async Task<int?> FindSteamGridDbGameIdAsync(string title)
    {
        using var searchRequest = new HttpRequestMessage(HttpMethod.Get,
            $"https://www.steamgriddb.com/api/v2/search/autocomplete/{Uri.EscapeDataString(title)}");
        searchRequest.Headers.Authorization = new("Bearer", SteamGridDbApiKey);
        using var searchResponse = await Http.SendAsync(searchRequest);
        if (HandleTooManyRequests(searchResponse, SteamGridDbSource, title)) return null;
        if (!searchResponse.IsSuccessStatusCode)
        {
            Log($"{title}: SteamGridDB search returned {(int)searchResponse.StatusCode} {searchResponse.ReasonPhrase}");
            return null;
        }
        var search = await searchResponse.Content.ReadFromJsonAsync<SteamGridDbSearchResponse>();
        int? gameId = search?.Data?.FirstOrDefault()?.Id;
        if (gameId is null) Log($"{title}: SteamGridDB search returned no matches");
        return gameId;
    }

    private static async Task<string?> FindTheGamesDbUrlAsync(string title)
    {
        if (!RateLimiter.IsAvailable(TheGamesDbSource))
        {
            Log($"{title}: TheGamesDB is cooling down after a recent 429, skipping");
            return null;
        }

        try
        {
            string searchUrl = $"https://api.thegamesdb.net/v1/Games/ByGameName?apikey={TheGamesDbApiKey}&name={Uri.EscapeDataString(title)}";
            using var searchResponse = await Http.GetAsync(searchUrl);
            if (HandleTooManyRequests(searchResponse, TheGamesDbSource, title)) return null;
            if (!searchResponse.IsSuccessStatusCode)
            {
                Log($"{title}: ByGameName returned {(int)searchResponse.StatusCode} {searchResponse.ReasonPhrase} — {await searchResponse.Content.ReadAsStringAsync()}");
                return null;
            }
            var search = await searchResponse.Content.ReadFromJsonAsync<TheGamesDbSearchResponse>();
            int? gameId = search?.Data?.Games?.FirstOrDefault()?.Id;
            if (gameId is null)
            {
                Log($"{title}: ByGameName returned no matches");
                return null;
            }

            string imagesUrl = $"https://api.thegamesdb.net/v1/Games/Images?apikey={TheGamesDbApiKey}&games_id={gameId}&filter[type]=boxart";
            using var imagesResponse = await Http.GetAsync(imagesUrl);
            if (HandleTooManyRequests(imagesResponse, TheGamesDbSource, title)) return null;
            if (!imagesResponse.IsSuccessStatusCode)
            {
                Log($"{title}: Games/Images returned {(int)imagesResponse.StatusCode} {imagesResponse.ReasonPhrase} — {await imagesResponse.Content.ReadAsStringAsync()}");
                return null;
            }
            var images = await imagesResponse.Content.ReadFromJsonAsync<TheGamesDbImagesResponse>();
            var boxart = images?.Data?.Images?.GetValueOrDefault(gameId.Value.ToString())
                ?.FirstOrDefault(i => i.Side is null or "front");
            if (boxart is null)
            {
                Log($"{title}: matched game id {gameId} but it has no front boxart");
                return null;
            }

            return images!.Data!.BaseUrl!.Large + boxart.Filename;
        }
        catch (HttpRequestException ex)
        {
            Log($"{title}: TheGamesDB request failed — {ex.Message}");
            return null;
        }
    }

    private static string Sanitize(string title)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            title = title.Replace(c, '_');
        return title;
    }

    /// <summary>
    /// Tracks a per-source cooldown after a 429, shared across every concurrent fetch in this process
    /// (games are fetched fire-and-forget in a loop after a scan — without this, a burst of 20+ new
    /// games would otherwise fire 20+ concurrent requests at a source that just told us to back off).
    /// </summary>
    private static class RateLimiter
    {
        public static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(60);

        private static readonly Dictionary<string, DateTime> CooldownUntilUtc = [];
        private static readonly Lock Gate = new();

        public static bool IsAvailable(string source)
        {
            lock (Gate)
                return !CooldownUntilUtc.TryGetValue(source, out var until) || until <= DateTime.UtcNow;
        }

        public static void SetCooldown(string source, TimeSpan? retryAfter)
        {
            lock (Gate)
                CooldownUntilUtc[source] = DateTime.UtcNow + (retryAfter ?? DefaultCooldown);
        }
    }

    private sealed class SteamGridDbSearchResponse
    {
        [JsonPropertyName("data")] public List<SteamGridDbGame>? Data { get; set; }
    }

    private sealed class SteamGridDbGame
    {
        [JsonPropertyName("id")] public int Id { get; set; }
    }

    private sealed class SteamGridDbGridsResponse
    {
        [JsonPropertyName("data")] public List<SteamGridDbGrid>? Data { get; set; }
    }

    private sealed class SteamGridDbGrid
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
    }

    private sealed class TheGamesDbSearchResponse
    {
        [JsonPropertyName("data")] public TheGamesDbSearchData? Data { get; set; }
    }

    private sealed class TheGamesDbSearchData
    {
        [JsonPropertyName("games")] public List<TheGamesDbGame>? Games { get; set; }
    }

    private sealed class TheGamesDbGame
    {
        [JsonPropertyName("id")] public int Id { get; set; }
    }

    private sealed class TheGamesDbImagesResponse
    {
        [JsonPropertyName("data")] public TheGamesDbImagesData? Data { get; set; }
    }

    private sealed class TheGamesDbImagesData
    {
        [JsonPropertyName("base_url")] public TheGamesDbBaseUrl? BaseUrl { get; set; }
        [JsonPropertyName("images")] public Dictionary<string, List<TheGamesDbImage>>? Images { get; set; }
    }

    private sealed class TheGamesDbBaseUrl
    {
        [JsonPropertyName("large")] public string? Large { get; set; }
    }

    private sealed class TheGamesDbImage
    {
        [JsonPropertyName("filename")] public string? Filename { get; set; } = "";
        [JsonPropertyName("side")] public string? Side { get; set; }
    }
}
