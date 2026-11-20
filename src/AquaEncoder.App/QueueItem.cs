using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace AquaEncoder.App;

public enum ItemState { Pending, Running, Done, Failed, Cancelled }

/// <summary>Kuyruktaki tek bir video. Arayüz bunu doğrudan bağlar (INotifyPropertyChanged).</summary>
public sealed class QueueItem : INotifyPropertyChanged
{
    private static readonly IBrush Gray = new SolidColorBrush(Color.Parse("#8B929B"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#F5A524"));
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#EF4444"));

    public event PropertyChangedEventHandler? PropertyChanged;

    public QueueItem(string video) { Video = video; }

    public string Video { get; }
    public string Name => Path.GetFileName(Video);
    public string Folder => Path.GetDirectoryName(Video) ?? "";

    private string? _sub;
    public string? Sub
    {
        get => _sub;
        set { if (Set(ref _sub, value)) Raise(nameof(SubLabel)); }
    }
    public string SubLabel => string.IsNullOrEmpty(_sub) ? "— altyazı yok" : "✔ " + Path.GetFileName(_sub);

    private ItemState _state = ItemState.Pending;
    public ItemState State
    {
        get => _state;
        set
        {
            if (!Set(ref _state, value)) return;
            Raise(nameof(Glyph)); Raise(nameof(StatusBrush)); Raise(nameof(Detail));
        }
    }

    private double _progress;
    public double Progress { get => _progress; set => Set(ref _progress, value); }

    private string _statusText = "Bekliyor";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    private string? _outputFile;
    public string? OutputFile
    {
        get => _outputFile;
        set { if (Set(ref _outputFile, value)) Raise(nameof(Detail)); }
    }

    public string? Error { get; set; }

    public string Detail => _state == ItemState.Done && _outputFile != null ? "→ " + _outputFile : Folder;

    public string Glyph => _state switch
    {
        ItemState.Running => "▶",
        ItemState.Done => "✔",
        ItemState.Failed => "✖",
        ItemState.Cancelled => "■",
        _ => "●",
    };

    public IBrush StatusBrush => _state switch
    {
        ItemState.Running => Amber,
        ItemState.Done => Green,
        ItemState.Failed => Red,
        _ => Gray,
    };

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
