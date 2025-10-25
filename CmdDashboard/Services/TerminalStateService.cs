using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CmdDashboard.Models;

namespace CmdDashboard.Services;

public sealed class TerminalStateService
{
    private const string FileName = "terminals.json";
    private readonly string _statePath;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public TerminalStateService()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CmdDashboard");

        Directory.CreateDirectory(baseDir);
        _statePath = Path.Combine(baseDir, FileName);
    }

    public IReadOnlyList<TerminalSessionSnapshot> LoadSessions()
    {
        if (!File.Exists(_statePath))
        {
            return Array.Empty<TerminalSessionSnapshot>();
        }

        try
        {
            using var stream = File.OpenRead(_statePath);
            var snapshots = JsonSerializer.Deserialize<List<TerminalSessionSnapshot>>(stream, SerializerOptions);
            return snapshots ?? new List<TerminalSessionSnapshot>();
        }
        catch
        {
            return Array.Empty<TerminalSessionSnapshot>();
        }
    }

    public void SaveSessions(IEnumerable<TerminalSessionSnapshot> sessions)
    {
        try
        {
            var list = sessions?.ToList() ?? new List<TerminalSessionSnapshot>();
            using var stream = File.Create(_statePath);
            JsonSerializer.Serialize(stream, list, SerializerOptions);
        }
        catch
        {
            // ignored
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                File.Delete(_statePath);
            }
        }
        catch
        {
            // ignored
        }
    }
}
