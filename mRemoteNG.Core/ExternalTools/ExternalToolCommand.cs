using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using mRemoteNG.Connection;

namespace mRemoteNG.ExternalTools
{
    /// <summary>
    /// What to run for an external tool on a connection: its program, arguments and working directory with the
    /// connection's details filled in. Unlike the Windows app, which builds one command-line string and escapes the
    /// values for cmd.exe, the arguments are split first (as a shell would, but no shell runs) and the variables are
    /// filled in afterwards, so a value can never become an extra argument or option.
    /// </summary>
    public sealed record ExternalToolCommand(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory)
    {
        /// <param name="environment">Reads an environment variable; by default the process's own.</param>
        public static ExternalToolCommand For(ExternalToolDefinition tool, ConnectionInfo connection, Func<string, string> environment = null)
        {
            environment ??= Environment.GetEnvironmentVariable;
            return new ExternalToolCommand(
                Expand(tool.FileName.Trim(), connection, environment),
                Split(tool.Arguments).Select(argument => Expand(argument, connection, environment)).ToList(),
                Expand(tool.WorkingDir.Trim(), connection, environment));
        }

        /// <summary>The command as shown to the user; it never shows the password, even when an argument holds it.</summary>
        public string Describe(ConnectionInfo connection)
        {
            string password = connection?.Password;
            IEnumerable<string> parts = new[] { FileName }.Concat(Arguments)
                .Select(part => string.IsNullOrEmpty(password) ? part : part.Replace(password, "***", StringComparison.Ordinal))
                .Select(Quote);
            return string.Join(' ', parts);
        }

        // As a shell would need it: plain, or in single quotes.
        private static string Quote(string part) =>
            part.Length > 0 && !part.Any(c => char.IsWhiteSpace(c) || c is '\'' or '"') ? part : $"'{part.Replace("'", @"'\''")}'";

        /// <summary>
        /// Splits <paramref name="text"/> into arguments: whitespace separates them, '…' keeps its content as it is,
        /// "…" keeps spaces (a backslash in it escapes " and \), and a backslash outside quotes escapes the next character.
        /// </summary>
        public static List<string> Split(string text)
        {
            List<string> arguments = [];
            StringBuilder current = new();
            bool inArgument = false;
            char quote = '\0';
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quote == '\'')
                {
                    if (c == '\'')
                        quote = '\0';
                    else
                        current.Append(c);
                }
                else if (quote == '"')
                {
                    if (c == '"')
                        quote = '\0';
                    else if (c == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\')
                        current.Append(text[++i]);
                    else
                        current.Append(c);
                }
                else if (char.IsWhiteSpace(c))
                {
                    if (inArgument)
                        arguments.Add(current.ToString());
                    current.Clear();
                    inArgument = false;
                }
                else
                {
                    inArgument = true;
                    if (c is '\'' or '"')
                        quote = c;
                    else if (c == '\\' && i + 1 < text.Length)
                        current.Append(text[++i]);
                    else
                        current.Append(c);
                }
            }

            if (inArgument)
                arguments.Add(current.ToString());
            return arguments;
        }

        /// <summary>
        /// Fills in the variables as the Windows app does: %NAME%, %HOSTNAME%, %PORT%, %USERNAME%, %PASSWORD%, %DOMAIN%,
        /// %DESCRIPTION%, %MACADDRESS% and %USERFIELD% from the connection, any other %VARIABLE% from the environment;
        /// \%VARIABLE\% only from the environment, and ^%VARIABLE^% stays as the literal %VARIABLE%. The escaping
        /// prefixes %-…% and %!…% are accepted and mean the same here, since no shell reads the result.
        /// </summary>
        public static string Expand(string text, ConnectionInfo connection, Func<string, string> environment)
        {
            StringBuilder result = new();
            int index = 0;
            while (index < text.Length)
            {
                int start = text.IndexOf('%', index);
                int end = start < 0 ? -1 : text.IndexOf('%', start + 1);
                if (end < 0)
                    break;

                char before = start > 0 ? text[start - 1] : '\0';
                char beforeEnd = text[end - 1];
                string name = text[(start + 1)..end];
                string replacement = null;
                int tokenStart = start;
                if (before == '^' && beforeEnd == '^' && end - start > 1)
                {
                    tokenStart--;
                    replacement = $"%{name[..^1]}%";
                }
                else if (before == '\\' && beforeEnd == '\\' && end - start > 1)
                {
                    tokenStart--;
                    replacement = environment(name[..^1]);
                }
                else
                {
                    if (name.Length > 0 && name[0] is '-' or '!')
                        name = name[1..];
                    if (name.Length > 0)
                        replacement = Variable(name, connection) ?? environment(name);
                }

                if (replacement is null)
                {
                    // Not a variable: keep the text, and let the closing % start the next one.
                    result.Append(text, index, end - index);
                    index = end;
                    continue;
                }

                result.Append(text, index, tokenStart - index).Append(replacement);
                index = end + 1;
            }

            return result.Append(text, index, text.Length - index).ToString();
        }

        private static string Variable(string name, ConnectionInfo connection) => name.ToLowerInvariant() switch
        {
            "name" => connection?.Name ?? string.Empty,
            "hostname" => connection?.Hostname ?? string.Empty,
            "port" => connection?.Port.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            "username" => connection?.Username ?? string.Empty,
            "password" => connection?.Password ?? string.Empty,
            "domain" => connection?.Domain ?? string.Empty,
            "description" => connection?.Description ?? string.Empty,
            "macaddress" => connection?.MacAddress ?? string.Empty,
            "userfield" => connection?.UserField ?? string.Empty,
            _ => null
        };
    }
}
