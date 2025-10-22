using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CmdDashboard.Services;

public sealed class NotesService
{
    private readonly string _notesDirectory;

    public NotesService()
    {
        var baseDir = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "CmdDashboard",
            "Notes");

        _notesDirectory = baseDir;
        Directory.CreateDirectory(_notesDirectory);
    }

    public IEnumerable<Models.NoteData> LoadNotes()
    {
        if (!Directory.Exists(_notesDirectory))
        {
            yield break;
        }

        foreach (var file in Directory.GetFiles(_notesDirectory, "*.txt"))
        {
            var title = Path.GetFileNameWithoutExtension(file) ?? "Note";
            var content = File.ReadAllText(file);
            yield return new Models.NoteData(FormatTitle(title), Path.GetFileName(file)!, content);
        }
    }

    public Models.NoteData CreateNote()
    {
        var index = 1;
        var existing = new HashSet<string>(Directory.GetFiles(_notesDirectory, "*.txt"),
            System.StringComparer.OrdinalIgnoreCase);

        string fileName;
        do
        {
            fileName = $"note-{index}.txt";
            index++;
        } while (existing.Contains(Path.Combine(_notesDirectory, fileName)));

        var title = FormatTitle(Path.GetFileNameWithoutExtension(fileName)!);
        var path = Path.Combine(_notesDirectory, fileName);
        File.WriteAllText(path, string.Empty);
        return new Models.NoteData(title, fileName, string.Empty);
    }

    public void SaveNote(string fileName, string content)
    {
        var path = Path.Combine(_notesDirectory, SanitizeFileName(fileName));
        File.WriteAllText(path, content);
    }

    public void DeleteNote(string fileName)
    {
        var path = Path.Combine(_notesDirectory, SanitizeFileName(fileName));
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '_');
        }

        return fileName;
    }

    private static string FormatTitle(string raw)
    {
        var cleaned = raw.Replace('-', ' ').Replace('_', ' ');
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(cleaned);
    }
}
