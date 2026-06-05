using System.ComponentModel;
using SkillzWin.ViewModels;
using Wpf.Ui.Controls;

namespace SkillzWin.Views;

public partial class OnboardingWindow : FluentWindow
{
    public OnboardingWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    // Not interactively dismissable — must click "Get Started".
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is OnboardingViewModel vm && !vm.Settings.HasCompletedOnboarding)
            e.Cancel = true;
    }
}
