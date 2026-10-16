using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AudioRouter.App.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Opção de dispositivo nos seletores. Id vazio = "Padrão do sistema" (sem roteamento).</summary>
public sealed class DeviceOption : ObservableObject
{
    private string _name;

    public DeviceOption(string id, string name)
    {
        Id = id;
        _name = name;
    }

    public string Id { get; }
    public string Name { get => _name; set => Set(ref _name, value); }

    // Usado como nome de acessibilidade (UI Automation) dos itens do ComboBox.
    public override string ToString() => _name;
}

internal static class Glyphs
{
    public const string Mute = "";

    public static string Volume(double percent, bool muted) =>
        muted ? Mute : percent <= 0 ? "" : percent < 33 ? "" : percent < 66 ? "" : "";
}
