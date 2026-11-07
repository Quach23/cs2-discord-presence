using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DiscordRPC;

const string Port = "3000";
const string CfgName = "gamestate_integration_discord.cfg";

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("=== CS2 → Discord Presence ===");

// ---------- Discord Application ID ----------
// Priority: command-line argument > appid.txt next to the exe > built-in default.
// An Application ID is public (not a secret). Use your own to get your own name/art assets.
const string DefaultAppId = "1568479596029149196";
string idFile = Path.Combine(AppContext.BaseDirectory, "appid.txt");
string fileId = File.Exists(idFile) ? File.ReadAllText(idFile).Trim() : "";
string appId = args.Length > 0 ? args[0] : fileId != "" ? fileId : DefaultAppId;

// ---------- Установка GSI-конфига в папку CS2 ----------
InstallConfig();

// ---------- Discord ----------
var discord = new DiscordRpcClient(appId);
discord.Logger = new DiscordRPC.Logging.ConsoleLogger(DiscordRPC.Logging.LogLevel.Warning, true);
discord.OnReady += (_, e) => Console.WriteLine($"[Discord] ПОДКЛЮЧЕНО как {e.User.Username}");
discord.OnError += (_, e) => Console.WriteLine($"[Discord] ОШИБКА: {e.Message}");
discord.OnConnectionFailed += (_, e) => Console.WriteLine($"[Discord] Не удалось подключиться (pipe {e.FailedPipe}). Discord запущен? Это десктопная версия?");
discord.Initialize();
Console.WriteLine("Application ID: " + appId);
SetWaiting();

// ---------- Локальный HTTP-сервер для данных от CS2 ----------
var http = new HttpListener();
http.Prefixes.Add($"http://127.0.0.1:{Port}/");
http.Start();
Console.WriteLine($"Слушаю CS2 на http://127.0.0.1:{Port}/  (Ctrl+C — выход)");

DateTime lastData = DateTime.UtcNow;
string lastMap = "";
DateTime mapStart = DateTime.UtcNow;

Console.CancelKeyPress += (_, e) => { discord.ClearPresence(); discord.Dispose(); http.Stop(); };

// Если CS2 замолчал — очищаем статус
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(5000);
        if (lastMap != "" && (DateTime.UtcNow - lastData).TotalSeconds > 30) { lastMap = ""; SetWaiting(); }
    }
});

while (http.IsListening)
{
    HttpListenerContext ctx;
    try { ctx = await http.GetContextAsync(); } catch { break; }
    using var ms = new MemoryStream();
    await ctx.Request.InputStream.CopyToAsync(ms);
    var bytes = ms.ToArray();
    var enc = ctx.Request.Headers["Content-Encoding"] ?? "";
    if (enc.Contains("gzip", StringComparison.OrdinalIgnoreCase) && bytes.Length > 0)
    {
        using var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        using var o = new MemoryStream();
        gz.CopyTo(o);
        bytes = o.ToArray();
    }
    string body = Encoding.UTF8.GetString(bytes);
    ctx.Response.StatusCode = 200;
    ctx.Response.Close();

    if (string.IsNullOrWhiteSpace(body) || !body.TrimStart().StartsWith("{"))
    {
        Console.WriteLine($"[{DateTime.Now:T}] Пустой запрос: {ctx.Request.HttpMethod}, Content-Length={ctx.Request.ContentLength64}, от {ctx.Request.UserAgent}");
        continue;
    }
    lastData = DateTime.UtcNow;

    try { Handle(body); }
    catch (Exception ex) { Console.WriteLine("Ошибка разбора: " + ex.Message); }
}

// ================= логика =================
void SetWaiting() => discord.SetPresence(new RichPresence
{
    Details = "CS2Presence запущен",
    State = "Жду данные от CS2",
    Assets = new Assets { LargeImageKey = "cs2", LargeImageText = "Counter-Strike 2" }
});

void Handle(string json)
{
    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;

    if (!root.TryGetProperty("map", out var map))
    {
        discord.SetPresence(new RichPresence
        {
            Details = "В главном меню",
            State = "Выбирает режим",
            Assets = new Assets { LargeImageKey = "cs2", LargeImageText = "Counter-Strike 2" }
        });
        lastMap = "";
        return;
    }

    string mapName = Str(map, "name");
    string mode = Str(map, "mode");
    string phase = Str(map, "phase");
    int roundsPlayed = Int(map, "round");
    int ct = Score(map, "team_ct");
    int t = Score(map, "team_t");

    if (mapName != lastMap) { lastMap = mapName; mapStart = DateTime.UtcNow; }

    // Играет ли это сам пользователь (или он наблюдатель)
    bool me = true;
    string team = "", kda = "";
    if (root.TryGetProperty("player", out var pl))
    {
        string pSteam = Str(pl, "steamid");
        string mySteam = root.TryGetProperty("provider", out var pv) ? Str(pv, "steamid") : "";
        me = pSteam == "" || pSteam == mySteam;
        team = Str(pl, "team");
        if (pl.TryGetProperty("match_stats", out var ms))
            kda = $"{Int(ms, "kills")}/{Int(ms, "assists")}/{Int(ms, "deaths")}";
    }

    string pretty = Pretty(mapName);
    string details = $"{pretty} • {ModeName(mode)}";
    string state;

    if (phase == "warmup") state = "Разминка";
    else if (phase == "gameover") state = $"Матч окончен • {ct}:{t}";
    else
    {
        int current = roundsPlayed + 1;
        string side = team == "CT" ? "CT" : team == "T" ? "T" : "";
        string score = team == "T" ? $"{t}:{ct}" : $"{ct}:{t}"; // свой счёт первым
        state = !me ? $"Наблюдает • Раунд {current} • {ct}:{t}"
              : $"Раунд {current} • {side} {score}" + (kda != "" ? $" • K/A/D {kda}" : "");
    }

    discord.SetPresence(new RichPresence
    {
        Details = details,
        State = state.Trim(),
        Timestamps = new Timestamps(mapStart),
        Assets = new Assets
        {
            LargeImageKey = mapName.ToLowerInvariant(),   // загрузи арт с таким именем (de_mirage и т.д.)
            LargeImageText = pretty,
            SmallImageKey = "cs2",
            SmallImageText = "Counter-Strike 2"
        }
    });
    Console.WriteLine($"[{DateTime.Now:T}] {details} | {state}");
}

static string Str(JsonElement e, string n) =>
    e.TryGetProperty(n, out var v) ? v.ToString() : "";
static int Int(JsonElement e, string n) =>
    e.TryGetProperty(n, out var v) && v.TryGetInt32(out var i) ? i : 0;
static int Score(JsonElement map, string team) =>
    map.TryGetProperty(team, out var t) ? Int(t, "score") : 0;

static string Pretty(string m)
{
    m = Regex.Replace(m, @"^(de|cs|ar|dz|gd)_", "");
    return m.Length == 0 ? "Неизвестная карта" : char.ToUpper(m[0]) + m[1..].Replace('_', ' ');
}

static string ModeName(string m) => m switch
{
    "competitive" => "Соревновательный",
    "casual" => "Обычный",
    "deathmatch" => "Бой насмерть",
    "scrimcomp2v2" => "Напарники",
    "gungameprogressive" => "Гонка вооружений",
    "survival" => "Опасная зона",
    "custom" => "Пользовательский",
    _ => m == "" ? "Матч" : m
};

// ================= установка конфига =================
void InstallConfig()
{
    string cfg = $$"""
    "Discord Presence"
    {
        "uri"       "http://127.0.0.1:{{Port}}/"
        "timeout"   "5.0"
        "buffer"    "0.1"
        "throttle"  "0.5"
        "heartbeat" "10.0"
        "data"
        {
            "provider"           "1"
            "map"                "1"
            "round"              "1"
            "player_id"          "1"
            "player_state"       "1"
            "player_match_stats" "1"
        }
    }
    """;

    foreach (var lib in SteamLibraries())
    {
        string dir = Path.Combine(lib, "steamapps", "common", "Counter-Strike Global Offensive", "game", "csgo", "cfg");
        if (!Directory.Exists(dir)) continue;
        File.WriteAllText(Path.Combine(dir, CfgName), cfg);
        Console.WriteLine("Конфиг установлен: " + dir);
        return;
    }
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, CfgName), cfg);
    Console.WriteLine($"Папку CS2 не нашёл. Скопируй {CfgName} (лежит рядом с exe) в:");
    Console.WriteLine(@"  ...\Counter-Strike Global Offensive\game\csgo\cfg\");
}

static IEnumerable<string> SteamLibraries()
{
    var list = new List<string>();
    string? steam = Microsoft.Win32.Registry.GetValue(
        @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
    if (steam != null)
    {
        steam = steam.Replace('/', '\\');
        list.Add(steam);
        string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                list.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
    }
    return list.Distinct();
}
