using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CmdDashboard.Models;
using CmdDashboard.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CmdDashboard.ViewModels;

public partial class MainViewModel : ObservableObject
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

    private int _maxVisibleNotes = 3;

    public IEnumerable<NoteViewModel> VisibleNotes => Notes.Take(MaxVisibleNotes);
    public IEnumerable<NoteViewModel> OverflowNotes => Notes.Skip(MaxVisibleNotes);
    public bool HasOverflow => Notes.Count > MaxVisibleNotes;

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

    public MainViewModel()
    {
        _notesService = new NotesService();
        CloseSessionCommand = new RelayCommand<TerminalSessionViewModel>(CloseSession, session => session != null);
        AddNoteCommand = new RelayCommand(AddNote);
        SaveSelectedNoteCommand = new RelayCommand(SaveSelectedNote, CanSaveSelectedNote);
        DeleteSelectedNoteCommand = new RelayCommand(DeleteSelectedNote, () => SelectedNote != null);
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

    partial void OnSelectedNoteChanged(NoteViewModel? value)
    {
        SaveSelectedNoteCommand.NotifyCanExecuteChanged();
        DeleteSelectedNoteCommand.NotifyCanExecuteChanged();
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
    }

    private bool CanSaveSelectedNote()
    {
        return SelectedNote?.HasChanges == true;
    }

    private void DeleteSelectedNote()
    {
        if (SelectedNote is null)
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
        OnPropertyChanged(nameof(VisibleNotes));
        OnPropertyChanged(nameof(OverflowNotes));
        OnPropertyChanged(nameof(HasOverflow));
    }
}
