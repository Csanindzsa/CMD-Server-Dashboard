using CommunityToolkit.Mvvm.ComponentModel;

namespace CmdDashboard.ViewModels;

public partial class NoteViewModel : ObservableObject
{
    public string Title { get; }
    public string FileName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    private string _content;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    private bool _isDirty;

    public bool HasChanges => IsDirty;

    public NoteViewModel(string title, string fileName, string content)
    {
        Title = title;
        FileName = fileName;
        _content = content;
        _isDirty = false;
    }

    partial void OnContentChanged(string value)
    {
        IsDirty = true;
    }

    public void MarkSaved()
    {
        IsDirty = false;
    }
}
