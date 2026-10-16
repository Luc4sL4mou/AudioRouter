using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AudioRouter.App.ViewModels;

namespace AudioRouter.App.Views;

public partial class AppRowView : UserControl
{
    public AppRowView() => InitializeComponent();

    private AppRowViewModel? Vm => DataContext as AppRowViewModel;

    private void OnMuteClick(object sender, RoutedEventArgs e) => Vm?.ToggleMute();

    private void OnSliderDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm is { } vm) vm.IsDragging = true;
    }

    private void OnSliderUp(object sender, MouseEventArgs e)
    {
        if (Vm is { } vm) vm.IsDragging = false;
    }
}
