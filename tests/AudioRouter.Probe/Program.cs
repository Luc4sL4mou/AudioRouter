using AudioRouter.Core.Audio;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Uso:
//   (sem args)                       lista dispositivos e sessões
//   route <processo> <parte-do-nome> roteia todas as instâncias do processo para o dispositivo
//   clear <processo>                 remove o roteamento
//   get <processo>                   mostra o roteamento atual
if (args.Length >= 2 && args[0] is "route" or "clear" or "get")
{
    var devices = AudioEnumerator.GetRenderDevices();
    var sessions = AudioEnumerator.GetSessions()
        .Where(s => !s.IsSystemSounds && s.ProcessId != 0 &&
                    s.ProcessName.Equals(args[1], StringComparison.OrdinalIgnoreCase))
        .DistinctBy(s => s.ProcessId).ToList();

    if (sessions.Count == 0)
    {
        Console.WriteLine($"Nenhuma sessão de áudio para '{args[1]}'. O app precisa estar aberto e já ter tocado som.");
        return 1;
    }

    AudioDeviceInfo? target = null;
    if (args[0] == "route")
    {
        if (args.Length < 3) { Console.WriteLine("Informe parte do nome do dispositivo."); return 1; }
        target = devices.FirstOrDefault(d => d.Name.Contains(args[2], StringComparison.OrdinalIgnoreCase));
        if (target is null) { Console.WriteLine($"Dispositivo '{args[2]}' não encontrado."); return 1; }
    }

    foreach (var s in sessions)
    {
        if (args[0] == "route") AppRouter.SetDevice(s.ProcessId, target!.Id);
        else if (args[0] == "clear") AppRouter.ClearDevice(s.ProcessId);

        var cur = AppRouter.GetDevice(s.ProcessId);
        var curName = devices.FirstOrDefault(d => d.Id == cur)?.Name ?? cur ?? "(padrão do sistema)";
        Console.WriteLine($"{s.ProcessName} pid={s.ProcessId}: roteamento = {curName}");
        if (args[0] == "route")
            Console.WriteLine(cur == target!.Id ? "  OK: leitura de volta confere." : "  FALHA: leitura de volta diferente do esperado.");
    }
    Console.WriteLine("\nObs.: o app normalmente só migra de dispositivo ao reabrir o stream (pause/play ou reiniciar a faixa).");
    return 0;
}

if (args.Length >= 1 && args[0] == "bench")
{
    var store = new AudioRouter.Core.Settings.SettingsStore();
    var reconciler = new RouteReconciler(store);
    for (int i = 0; i < 6; i++)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var snap = AudioSnapshot.Capture();
        long capture = sw.ElapsedMilliseconds;
        sw.Restart();
        reconciler.Apply(snap);
        Console.WriteLine($"#{i}: capture={capture} ms (devices={snap.Devices.Count}, sessions={snap.Sessions.Count})  reconcile={sw.ElapsedMilliseconds} ms");
    }
    return 0;
}

Console.WriteLine("=== Dispositivos de saída ===");
foreach (var d in AudioEnumerator.GetRenderDevices())
    Console.WriteLine($"{(d.IsDefault ? "*" : " ")} {d.Name}  vol={d.Volume:P0}{(d.IsMuted ? " [MUDO]" : "")}\n    id={d.Id}");

Console.WriteLine("\n=== Sessões de áudio ===");
foreach (var s in AudioEnumerator.GetSessions().OrderBy(s => s.ProcessName))
    Console.WriteLine($"{(s.IsActive ? "▶" : "·")} {s.ProcessName,-22} pid={s.ProcessId,-6} vol={s.Volume,-5:P0}{(s.IsMuted ? " [MUDO]" : "")} peak={s.Peak:0.00} -> {s.DeviceName}\n    {s.ExecutablePath ?? "(sem caminho)"}");
return 0;
