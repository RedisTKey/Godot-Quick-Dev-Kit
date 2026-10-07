using Godot;

namespace QuickDev.Database.Examples;

/// <summary>F6 this scene. Uses the plugin singleton when enabled, otherwise owns a local service.</summary>
public partial class BasicUsage : Control
{
    [Export] public StringName SlotName { get; set; } = "example";
    [Export(PropertyHint.File, "*.cfg")] public string SavePath { get; set; } = "user://quick_database_example.cfg";
    [Export] public string InitialTitle { get; set; } = "Quick Database";

    private QuickDatabaseService _database = null!;
    private Label _summary = null!;
    private Label _status = null!;
    private LineEdit _title = null!;

    public override void _EnterTree()
    {
        if (_database is not null && GodotObject.IsInstanceValid(_database)) Subscribe();
    }

    public override void _Ready()
    {
        _summary = GetNode<Label>("Panel/Margin/Layout/Summary");
        _status = GetNode<Label>("Panel/Margin/Layout/Status");
        _title = GetNode<LineEdit>("Panel/Margin/Layout/Title");
        var existing = GetNodeOrNull<Node>("/root/QuickDatabase");
        if (existing is not null && existing is not QuickDatabaseService)
        {
            _status.Text = "QuickDatabase 当前为 GDScript 版，请关闭它后启用 C# 版";
            GetNode<Control>("Panel/Margin/Layout/Buttons").MouseFilter = MouseFilterEnum.Ignore;
            return;
        }
        _database = existing as QuickDatabaseService ?? new QuickDatabaseService { Name = "LocalDatabase" };
        if (existing is null) AddChild(_database);
        Subscribe();
        _database.OpenSlot<ExampleDocument>(SlotName, SavePath);
        using var initialDocument = _database.GetDocument<ExampleDocument>(SlotName);
        var currentTitle = initialDocument?.Title;
        _title.Text = string.IsNullOrEmpty(currentTitle) ? InitialTitle : currentTitle;
        GetNode<Button>("Panel/Margin/Layout/Buttons/Increment").Pressed += Increment;
        GetNode<Button>("Panel/Margin/Layout/Buttons/Commit").Pressed += CommitTitle;
        GetNode<Button>("Panel/Margin/Layout/Buttons/Flush").Pressed += () => _database.FlushSlot(SlotName);
        GetNode<Button>("Panel/Margin/Layout/Buttons/Reload").Pressed += () => _database.ReloadSlot(SlotName);
        GetNode<Button>("Panel/Margin/Layout/Buttons/Erase").Pressed += () =>
        {
            var error = _database.EraseSlot(SlotName);
            _status.Text = $"Erase: {error}";
            Refresh();
        };
        Refresh();
    }

    public void Increment()
    {
        _database.UpdateSlot(SlotName, "count", document => ((ExampleDocument)document).Count++);
        Refresh();
    }

    public void CommitTitle() => _database.CommitSlot(SlotName, "title", document => ((ExampleDocument)document).Title = _title.Text);

    private void OnDataChanged(StringName slot, StringName field) { if (slot == SlotName) Refresh(); }
    private void OnDatabaseLoaded(StringName slot, int state)
    {
        if (slot != SlotName) return;
        _status.Text = $"Loaded: {(QuickDatabaseStore.LoadStatus)state}";
        Refresh();
    }
    private void OnSaveSucceeded(StringName slot, string path)
    {
        if (slot != SlotName) return;
        _status.Text = $"Saved: {path}";
        Refresh();
    }
    private void OnSaveFailed(StringName slot, string path, Error error)
    {
        if (slot == SlotName) _status.Text = $"Save failed: {error} ({path})";
    }

    private void Subscribe()
    {
        _database.DataChanged += OnDataChanged;
        _database.DatabaseLoaded += OnDatabaseLoaded;
        _database.SaveSucceeded += OnSaveSucceeded;
        _database.SaveFailed += OnSaveFailed;
    }

    public override void _ExitTree()
    {
        if (_database is null || !GodotObject.IsInstanceValid(_database)) return;
        _database.DataChanged -= OnDataChanged;
        _database.DatabaseLoaded -= OnDatabaseLoaded;
        _database.SaveSucceeded -= OnSaveSucceeded;
        _database.SaveFailed -= OnSaveFailed;
    }

    private void Refresh()
    {
        using var document = _database?.GetDocument<ExampleDocument>(SlotName);
        if (document is null) return;
        _summary.Text = $"Count: {document.Count}   Title: {document.Title}\nDirty: {_database!.IsDirty(SlotName)}   Slot: {SlotName}";
    }
}
