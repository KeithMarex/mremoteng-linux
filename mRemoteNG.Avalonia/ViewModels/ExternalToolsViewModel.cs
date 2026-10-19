using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mRemoteNG.ExternalTools;

namespace mRemoteNG.Avalonia.ViewModels;

/// <summary>The External Tools window: the list of tools and the selected one's settings, edited on copies until OK.</summary>
public sealed partial class ExternalToolsViewModel : ObservableObject
{
    public ExternalToolsViewModel(IEnumerable<ExternalToolDefinition> tools)
    {
        foreach (ExternalToolDefinition tool in tools)
            Tools.Add(new ExternalToolItemViewModel(tool.Clone()));
        Selected = Tools.FirstOrDefault();
    }

    public ObservableCollection<ExternalToolItemViewModel> Tools { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private ExternalToolItemViewModel? _selected;

    public bool HasSelection => Selected is not null;

    /// <summary>The tools as edited.</summary>
    public IReadOnlyList<ExternalToolDefinition> Result => Tools.Select(item => item.Tool).ToList();

    [RelayCommand]
    private void New()
    {
        ExternalToolItemViewModel item = new(new ExternalToolDefinition { DisplayName = UiText.NewExternalTool });
        Tools.Add(item);
        Selected = item;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        int index = Tools.IndexOf(Selected!);
        Tools.RemoveAt(index);
        Selected = Tools.Count == 0 ? null : Tools[Math.Min(index, Tools.Count - 1)];
    }
}

/// <summary>One tool in the External Tools window; its settings write through to the (copied) tool.</summary>
public sealed class ExternalToolItemViewModel(ExternalToolDefinition tool) : ObservableObject
{
    public ExternalToolDefinition Tool { get; } = tool;

    public string DisplayName
    {
        get => Tool.DisplayName;
        set => SetProperty(Tool.DisplayName, value ?? string.Empty, Tool, (t, v) => t.DisplayName = v);
    }

    public string Category
    {
        get => Tool.Category;
        set => SetProperty(Tool.Category, value ?? string.Empty, Tool, (t, v) => t.Category = v);
    }

    public string FileName
    {
        get => Tool.FileName;
        set => SetProperty(Tool.FileName, value ?? string.Empty, Tool, (t, v) => t.FileName = v);
    }

    public string Arguments
    {
        get => Tool.Arguments;
        set => SetProperty(Tool.Arguments, value ?? string.Empty, Tool, (t, v) => t.Arguments = v);
    }

    public string WorkingDir
    {
        get => Tool.WorkingDir;
        set => SetProperty(Tool.WorkingDir, value ?? string.Empty, Tool, (t, v) => t.WorkingDir = v);
    }

    public bool WaitForExit
    {
        get => Tool.WaitForExit;
        set => SetProperty(Tool.WaitForExit, value, Tool, (t, v) => t.WaitForExit = v);
    }

    public bool TryIntegrate
    {
        get => Tool.TryIntegrate;
        set => SetProperty(Tool.TryIntegrate, value, Tool, (t, v) => t.TryIntegrate = v);
    }

    public bool RunElevated
    {
        get => Tool.RunElevated;
        set => SetProperty(Tool.RunElevated, value, Tool, (t, v) => t.RunElevated = v);
    }
}
