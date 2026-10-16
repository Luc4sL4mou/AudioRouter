using System.Collections.Concurrent;
using System.Diagnostics;

namespace AudioRouter.Core.Audio;

/// <summary>Nome do processo, caminho e nome amigável do app (resolvidos uma vez por PID).</summary>
public sealed record ProcessDetails(string ProcessName, string? ExecutablePath, string FriendlyName);

/// <summary>Cache por PID: Process.MainModule/FileVersionInfo são caros para consultar a cada atualização.</summary>
public static class ProcessInfoCache
{
    private static readonly ConcurrentDictionary<int, ProcessDetails?> Cache = new();

    public static ProcessDetails? Resolve(int pid)
    {
        if (Cache.TryGetValue(pid, out var cached) && cached is not null) return cached;
        var details = Load(pid);
        if (details is not null) Cache[pid] = details;
        return details;
    }

    /// <summary>Descarta PIDs que não aparecem mais (evita reaproveitar um PID reciclado).</summary>
    public static void Retain(IReadOnlySet<int> livePids)
    {
        foreach (var pid in Cache.Keys)
            if (!livePids.Contains(pid)) Cache.TryRemove(pid, out _);
    }

    private static ProcessDetails? Load(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            string? path = null;
            string? friendly = null;
            try
            {
                path = p.MainModule?.FileName;
                if (path is not null)
                {
                    var vi = FileVersionInfo.GetVersionInfo(path);
                    friendly = !string.IsNullOrWhiteSpace(vi.FileDescription) ? vi.FileDescription
                             : !string.IsNullOrWhiteSpace(vi.ProductName) ? vi.ProductName : null;
                }
            }
            catch { /* acesso negado (processo elevado/protegido) */ }
            return new ProcessDetails(p.ProcessName, path, friendly?.Trim() ?? p.ProcessName);
        }
        catch { return null; }
    }
}
