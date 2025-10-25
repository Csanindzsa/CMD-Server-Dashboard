namespace CmdDashboard.Models;

public record TerminalSessionSnapshot(
    string Title,
    string? WorkingDirectory,
    string? StartupCommand,
    bool RunAsAdministrator,
    string Output);
