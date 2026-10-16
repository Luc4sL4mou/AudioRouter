using System.Text.Json;

namespace AudioRouter.Core.Settings;

/// <summary>Dispositivo lembrado para um app. O nome serve de reserva caso o ID do endpoint mude (ex.: reinstalação de driver).</summary>
public sealed class RouteEntry
{
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public bool StartWithWindows { get; set; }

    /// <summary>Chave = nome do executável em minúsculas (ex.: "brave.exe").</summary>
    public Dictionary<string, RouteEntry> Routes { get; set; } = new();
}

/// <summary>Chave de rota: nome do executável em minúsculas, com ".exe".</summary>
public static class AppRouteKey
{
    public static string From(string processName)
    {
        var name = processName.Trim().ToLowerInvariant();
        return name.EndsWith(".exe", StringComparison.Ordinal) ? name : name + ".exe";
    }
}

/// <summary>Configurações em JSON (%AppData%\AudioRouter\settings.json). Seguro entre threads.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _gate = new();
    private AppSettings _settings;

    public SettingsStore(string? path = null)
    {
        _path = path ?? DefaultPath;
        _settings = Load();
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioRouter", "settings.json");

    public string FilePath => _path;

    public bool StartWithWindows
    {
        get { lock (_gate) return _settings.StartWithWindows; }
        set { lock (_gate) { _settings.StartWithWindows = value; Save(); } }
    }

    /// <summary>Cópia das rotas salvas.</summary>
    public IReadOnlyDictionary<string, RouteEntry> Routes
    {
        get
        {
            lock (_gate)
                return _settings.Routes.ToDictionary(
                    p => p.Key, p => new RouteEntry { DeviceId = p.Value.DeviceId, DeviceName = p.Value.DeviceName },
                    StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SetRoute(string key, string deviceId, string deviceName)
    {
        lock (_gate)
        {
            _settings.Routes[key] = new RouteEntry { DeviceId = deviceId, DeviceName = deviceName };
            Save();
        }
    }

    public void RemoveRoute(string key)
    {
        lock (_gate)
        {
            if (_settings.Routes.Remove(key)) Save();
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return Normalize(new AppSettings());
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path));
            return Normalize(loaded ?? new AppSettings());
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Arquivo corrompido: preserva uma cópia para inspeção e recomeça vazio.
            try { File.Move(_path, _path + ".bad", overwrite: true); } catch { }
            return Normalize(new AppSettings());
        }
    }

    private static AppSettings Normalize(AppSettings s)
    {
        s.Routes = new Dictionary<string, RouteEntry>(
            (s.Routes ?? new()).Where(p => p.Value is not null && !string.IsNullOrWhiteSpace(p.Value.DeviceId))
                               .Select(p => KeyValuePair.Create(AppRouteKey.From(p.Key), p.Value)),
            StringComparer.OrdinalIgnoreCase);
        return s;
    }

    /// <summary>Grava em arquivo temporário e troca, para nunca deixar o JSON pela metade.</summary>
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_settings, JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sem permissão/disco cheio: segue com o estado em memória.
        }
    }
}
