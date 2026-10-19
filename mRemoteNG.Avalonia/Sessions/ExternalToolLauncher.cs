using mRemoteNG.Connection;
using mRemoteNG.ExternalTools;

namespace mRemoteNG.Avalonia.Sessions;

/// <summary>
/// Runs external tools on connections, as the Windows app does: in a tab when the tool asks to be integrated (here a
/// terminal tab, since Linux has no window to adopt into a tab), otherwise as a program of its own.
/// </summary>
public sealed class ExternalToolLauncher(SessionPrograms programs)
{
    /// <summary>What to run for <paramref name="tool"/> on <paramref name="connection"/>.</summary>
    /// <exception cref="SessionException">The tool cannot be run, with the reason.</exception>
    public SessionPlan Plan(ExternalToolDefinition tool, ConnectionInfo connection)
    {
        ExternalToolCommand command = ExternalToolCommand.For(tool, connection);
        if (command.FileName.Length == 0)
            throw new SessionException(UiText.ExternalToolWithoutProgram(tool.DisplayName));
        string program = programs.Find(command.FileName) ?? throw new SessionException(UiText.ProgramNotFound(command.FileName, null));

        List<string> warnings = [];
        if (tool.RunElevated)
            warnings.Add(UiText.RunElevatedIgnored(tool.DisplayName));
        string? directory = command.WorkingDirectory.Length > 0 ? command.WorkingDirectory : null;
        if (directory is not null && !Directory.Exists(directory))
        {
            warnings.Add(UiText.WorkingDirectoryMissing(directory));
            directory = null;
        }

        return new SessionPlan(program, command.Arguments)
        {
            Warnings = warnings,
            Description = command.Describe(connection),
            WorkingDirectory = directory
        };
    }

    /// <summary>Starts a tool that has a window of its own; the task ends with its exit code.</summary>
    /// <exception cref="SessionException">The program could not be started.</exception>
    public Task<int> Start(SessionPlan plan)
    {
        try
        {
            return programs.StartDetached(plan);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            throw new SessionException(UiText.CouldNotStart(Path.GetFileName(plan.Program), ex.Message));
        }
    }
}
