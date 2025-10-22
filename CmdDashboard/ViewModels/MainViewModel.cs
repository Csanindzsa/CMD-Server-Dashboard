using System.Collections.ObjectModel;
using System.Linq;
using CmdDashboard.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CmdDashboard.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<TerminalSessionViewModel> Sessions { get; } = new();

    [ObservableProperty]
    private TerminalSessionViewModel? _selectedSession;

    public IRelayCommand<TerminalSessionViewModel> CloseSessionCommand { get; }

    public MainViewModel()
    {
        CloseSessionCommand = new RelayCommand<TerminalSessionViewModel>(CloseSession, session => session != null);
    }

    public TerminalSessionViewModel AddSession(NewSessionOptions options)
    {
        var session = TerminalSessionViewModel.CreateInteractive(
            options.Title,
            options.WorkingDirectory,
            options.StartupCommand);
        Sessions.Add(session);
        SelectedSession = session;
        return session;
    }

    private void CloseSession(TerminalSessionViewModel? session)
    {
        if (session == null)
        {
            return;
        }

        session.Dispose();
        Sessions.Remove(session);

        if (SelectedSession == session)
        {
            SelectedSession = Sessions.LastOrDefault();
        }
    }
}
