using Microsoft.Win32;

namespace AudioRouter.Core.Settings;

/// <summary>"Iniciar com o Windows" via HKCU\...\Run (sem privilégio de administrador).</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "AudioRouter";

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>Faz o registro refletir a preferência salva (também corrige o caminho se o exe mudou de lugar).</summary>
    public static void Sync(bool enabled, string exePath)
    {
        if (enabled) Enable(exePath);
        else Disable();
    }

    public static void Enable(string exePath)
    {
        var command = $"\"{exePath}\"";
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (run.GetValue(ValueName) as string != command)
                run.SetValue(ValueName, command, RegistryValueKind.String);
        }

        // Se o usuário havia desativado o item no Gerenciador de Tarefas, reativar aqui significa remover essa marca.
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        if (approved?.GetValue(ValueName) is not null) approved.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static void Disable()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (run?.GetValue(ValueName) is not null) run.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
