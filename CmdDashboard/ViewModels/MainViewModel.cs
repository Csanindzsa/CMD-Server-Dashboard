using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using CmdDashboard.Models;
using CmdDashboard.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CmdDashboard.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    public ObservableCollection<TerminalSessionViewModel> Sessions { get; } = new();
    public ObservableCollection<NoteViewModel> Notes { get; } = new();

    [ObservableProperty]
    private TerminalSessionViewModel? _selectedSession;

    [ObservableProperty]
    private NoteViewModel? _selectedNote;

    public IRelayCommand<TerminalSessionViewModel> CloseSessionCommand { get; }
    public IRelayCommand AddNoteCommand { get; }
    public IRelayCommand SaveSelectedNoteCommand { get; }
    public IRelayCommand DeleteSelectedNoteCommand { get; }
    public IRelayCommand<NoteViewModel> SelectNoteCommand { get; }
    public IRelayCommand ClearSelectedNoteCommand { get; }

    private int _maxVisibleNotes = 3;
    private IReadOnlyList<NoteViewModel> _visibleNotes = Array.Empty<NoteViewModel>();
    private IReadOnlyList<NoteViewModel> _overflowNotes = Array.Empty<NoteViewModel>();

    public IReadOnlyList<NoteViewModel> VisibleNotes => _visibleNotes;
    public IReadOnlyList<NoteViewModel> OverflowNotes => _overflowNotes;
    public bool HasOverflow => _overflowNotes.Count > 0;

    public NoteViewModel? SelectedOverflowNote
    {
        get
        {
            if (SelectedNote is null)
            {
                return null;
            }

            return _overflowNotes.Contains(SelectedNote) ? SelectedNote : null;
        }
        set
        {
            if (value != null)
            {
                SelectedNote = value;
            }
        }
    }

    public int MaxVisibleNotes
    {
        get => _maxVisibleNotes;
        private set
        {
            if (value < 1)
            {
                value = 1;
            }

            if (SetProperty(ref _maxVisibleNotes, value))
            {
                RefreshNoteProjection();
            }
        }
    }

    private readonly NotesService _notesService;
    private readonly TerminalStateService _terminalStateService;
    private bool _isDisposed;

    public MainViewModel()
    {
        _notesService = new NotesService();
        _terminalStateService = new TerminalStateService();
        CloseSessionCommand = new RelayCommand<TerminalSessionViewModel>(CloseSession, session => session != null);
        AddNoteCommand = new RelayCommand(AddNote);
        SaveSelectedNoteCommand = new RelayCommand(SaveSelectedNote, CanSaveSelectedNote);
        DeleteSelectedNoteCommand = new RelayCommand(DeleteSelectedNote, CanDeleteSelectedNote);
        ClearSelectedNoteCommand = new RelayCommand(ClearSelectedNote, CanClearSelectedNote);
        SelectNoteCommand = new RelayCommand<NoteViewModel>(note =>
        {
            if (note != null)
            {
                SelectedNote = note;
            }
        });

        Notes.CollectionChanged += NotesOnCollectionChanged;

        LoadNotes();
        RefreshNoteProjection();
        LoadTerminalSessions();
    }

    public TerminalSessionViewModel AddSession(NewSessionOptions options)
    {
        var session = TerminalSessionViewModel.CreateInteractive(
            options.Title,
            options.WorkingDirectory,
            options.StartupCommand,
            options.RunAsAdministrator);
        Sessions.Add(session);
        SelectedSession = session;
        SaveTerminalState();
        return session;
    }

    public void SaveTerminalState()
    {
        _terminalStateService.SaveSessions(Sessions.Select(session => session.Capture()));
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

        SaveTerminalState();
    }

    public void ClearAllUserData()
    {
        var sessionsCopy = Sessions.ToList();
        foreach (var terminal in sessionsCopy)
        {
            terminal.Dispose();
        }
        Sessions.Clear();
        SelectedSession = null;
        _terminalStateService.Clear();

        var notesCopy = Notes.ToList();
        foreach (var note in notesCopy)
        {
            DetachNote(note);
        }

        Notes.Clear();
        var defaultNote = _notesService.ResetToDefaultNote();
        var defaultViewModel = new NoteViewModel(defaultNote.Title, defaultNote.FileName, defaultNote.Content);
        AttachNote(defaultViewModel);
        Notes.Add(defaultViewModel);
        SelectedNote = defaultViewModel;

        RefreshNoteProjection();
        SaveSelectedNoteCommand.NotifyCanExecuteChanged();
        DeleteSelectedNoteCommand.NotifyCanExecuteChanged();
        ClearSelectedNoteCommand.NotifyCanExecuteChanged();
        SaveTerminalState();
    }

    partial void OnSelectedNoteChanged(NoteViewModel? value)
    {
        SaveSelectedNoteCommand.NotifyCanExecuteChanged();
        DeleteSelectedNoteCommand.NotifyCanExecuteChanged();
        ClearSelectedNoteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SelectedOverflowNote));
    }

    private void LoadNotes()
    {
        foreach (var note in _notesService.LoadNotes().OrderBy(n => n.Title))
        {
            var vm = new NoteViewModel(note.Title, note.FileName, note.Content);
            AttachNote(vm);
            Notes.Add(vm);
        }

        SelectedNote = Notes.FirstOrDefault();
    }

    private void LoadTerminalSessions()
    {
        var readOnlyElevatedSessions = false;
        foreach (var snapshot in _terminalStateService.LoadSessions())
        {
            var requiresElevation = snapshot.RunAsAdministrator && !App.IsRunningAsAdministrator;
            var session = TerminalSessionViewModel.Restore(snapshot, interactive: !requiresElevation);
            if (requiresElevation)
            {
                readOnlyElevatedSessions = true;
            }

            Sessions.Add(session);
        }

        SelectedSession = Sessions.FirstOrDefault();

        if (readOnlyElevatedSessions)
        {
            System.Windows.MessageBox.Show(
                "Administrator terminals from previous sessions have been restored in read-only mode. Restart the application with administrator privileges to fully interact with them.",
                "Administrator Terminals",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void AttachNote(NoteViewModel note)
    {
        note.PropertyChanged += NoteOnPropertyChanged;
    }

    private void DetachNote(NoteViewModel note)
    {
        note.PropertyChanged -= NoteOnPropertyChanged;
    }

    private void NoteOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NoteViewModel.HasChanges) && sender == SelectedNote)
        {
            SaveSelectedNoteCommand.NotifyCanExecuteChanged();
        }
    }

    private void AddNote()
    {
        var noteData = _notesService.CreateNote();
        var vm = new NoteViewModel(noteData.Title, noteData.FileName, noteData.Content);
        AttachNote(vm);
        Notes.Add(vm);
        SelectedNote = vm;
    }

    private void SaveSelectedNote()
    {
        if (SelectedNote is null)
        {
            return;
        }

        _notesService.SaveNote(SelectedNote.FileName, SelectedNote.Content);
        SelectedNote.MarkSaved();
        SaveSelectedNoteCommand.NotifyCanExecuteChanged();
        DeleteSelectedNoteCommand.NotifyCanExecuteChanged();
        ClearSelectedNoteCommand.NotifyCanExecuteChanged();
    }

    private bool CanSaveSelectedNote()
    {
        return SelectedNote?.HasChanges == true;
    }

    private bool CanClearSelectedNote()
    {
        return SelectedNote != null;
    }

    private void ClearSelectedNote()
    {
        if (SelectedNote is null)
        {
            return;
        }

        SelectedNote.Content = string.Empty;
    }

    private void DeleteSelectedNote()
    {
        if (SelectedNote is null || !CanDeleteSelectedNote())
        {
            return;
        }

        var note = SelectedNote;
        _notesService.DeleteNote(note.FileName);
        var index = Notes.IndexOf(note);
        DetachNote(note);
        Notes.Remove(note);

        if (Notes.Count == 0)
        {
            SelectedNote = null;
            return;
        }

        var nextIndex = index >= Notes.Count ? Notes.Count - 1 : index;
        SelectedNote = Notes[nextIndex];
    }

    private bool CanDeleteSelectedNote()
    {
        return SelectedNote is { } note && !IsProtectedNote(note);
    }

    private static bool IsProtectedNote(NoteViewModel note)
    {
        return string.Equals(note.FileName, "note-1.txt", StringComparison.OrdinalIgnoreCase)
               || string.Equals(note.Title, "Note 1", StringComparison.OrdinalIgnoreCase);
    }

    public void UpdateVisibleNoteCapacity(double availableWidth)
    {
        const double minimumCardWidth = 220;
        const double tileSpacing = 12;

        if (double.IsNaN(availableWidth) || availableWidth <= 0)
        {
            MaxVisibleNotes = 1;
            return;
        }

        var capacity = Math.Max(1, (int)Math.Floor((availableWidth + tileSpacing) / (minimumCardWidth + tileSpacing)));
        MaxVisibleNotes = capacity;
    }

    private void NotesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshNoteProjection();
    }

    private void RefreshNoteProjection()
    {
        _visibleNotes = Notes.Take(MaxVisibleNotes).ToList();
        _overflowNotes = Notes.Skip(MaxVisibleNotes).ToList();
        OnPropertyChanged(nameof(VisibleNotes));
        OnPropertyChanged(nameof(OverflowNotes));
        OnPropertyChanged(nameof(HasOverflow));
        OnPropertyChanged(nameof(SelectedOverflowNote));
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Notes.CollectionChanged -= NotesOnCollectionChanged;

        foreach (var note in Notes)
        {
            DetachNote(note);
        }

        foreach (var session in Sessions)
        {
            session.Dispose();
        }
    }
}
