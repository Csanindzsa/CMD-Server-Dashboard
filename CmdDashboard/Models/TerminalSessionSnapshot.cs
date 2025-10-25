namespace CmdDashboard.Models;

public record TerminalSessionSnapshot(
    string Title,
    string? WorkingDirectory,
    string? StartupCommand,
    string Output);
