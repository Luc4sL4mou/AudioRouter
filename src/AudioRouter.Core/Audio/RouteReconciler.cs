using AudioRouter.Core.Settings;

namespace AudioRouter.Core.Audio;

/// <summary>Reaplica as rotas salvas aos processos que têm sessão de áudio.</summary>
public sealed class RouteReconciler
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private readonly SettingsStore _store;
    // Evita martelar a API se um processo não aceitar o roteamento: (pid, dispositivo) -> última tentativa.
    private readonly Dictionary<(int Pid, string DeviceId), DateTime> _attempts = new();

    public RouteReconciler(SettingsStore store) => _store = store;

    /// <summary>Retorna quantos processos foram roteados nesta passada.</summary>
    public int Apply(AudioSnapshot snapshot)
    {
        var routes = _store.Routes;
        if (routes.Count == 0) return 0;

        var devices = snapshot.Devices.Select(d => d.Info).ToList();
        var now = DateTime.UtcNow;
        var livePids = new HashSet<int>();
        int applied = 0;

        var groups = snapshot.Sessions
            .Where(s => !s.Info.IsSystemSounds && s.Info.ProcessId != 0)
            .GroupBy(s => AppRouteKey.From(s.Info.ProcessName));

        foreach (var group in groups)
        {
            if (!routes.TryGetValue(group.Key, out var entry)) continue;

            // Dispositivo ausente (desconectado): nada a fazer agora; reaplica quando voltar.
            var target = devices.FirstOrDefault(d => string.Equals(d.Id, entry.DeviceId, StringComparison.OrdinalIgnoreCase))
                         ?? devices.FirstOrDefault(d => d.Name == entry.DeviceName);
            if (target is null) continue;

            // O ID mudou mas o nome bateu: atualiza o arquivo.
            if (!string.Equals(target.Id, entry.DeviceId, StringComparison.OrdinalIgnoreCase))
                _store.SetRoute(group.Key, target.Id, target.Name);

            foreach (var pid in group.Select(s => s.Info.ProcessId).Distinct())
            {
                livePids.Add(pid);

                string? current;
                try { current = AppRouter.GetDevice(pid); }
                catch { continue; }
                if (string.Equals(current, target.Id, StringComparison.OrdinalIgnoreCase)) continue;

                var attempt = (pid, target.Id);
                if (_attempts.TryGetValue(attempt, out var last) && now - last < RetryAfter) continue;
                _attempts[attempt] = now;

                try { AppRouter.SetDevice(pid, target.Id); applied++; }
                catch { /* processo encerrou ou a API recusou: tenta de novo depois do intervalo */ }
            }
        }

        foreach (var stale in _attempts.Keys.Where(k => !livePids.Contains(k.Pid)).ToList())
            _attempts.Remove(stale);

        return applied;
    }
}
