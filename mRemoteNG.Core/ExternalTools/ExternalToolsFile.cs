using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using mRemoteNG.Security;

namespace mRemoteNG.ExternalTools
{
    /// <summary>
    /// An external tool: a program run on a connection, with the connection's details in its arguments. The same fields
    /// as the Windows app's ExternalTool, which is tied to WinForms.
    /// </summary>
    public sealed class ExternalToolDefinition
    {
        public string DisplayName { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string Arguments { get; set; } = string.Empty;

        public string WorkingDir { get; set; } = string.Empty;

        public bool WaitForExit { get; set; }

        /// <summary>Show the tool in a tab (on Windows: its window inside the tab; on Linux: in a terminal tab).</summary>
        public bool TryIntegrate { get; set; }

        public bool RunElevated { get; set; }

        public bool ShowOnToolbar { get; set; } = true;

        /// <summary>Attributes this version does not know, kept so that saving does not lose them.</summary>
        public Dictionary<string, string> OtherAttributes { get; private set; } = new();

        public ExternalToolDefinition Clone()
        {
            ExternalToolDefinition clone = (ExternalToolDefinition)MemberwiseClone();
            clone.OtherAttributes = new Dictionary<string, string>(OtherAttributes);
            return clone;
        }
    }

    /// <summary>
    /// Reads and writes the external tools file (extApps.xml) in the Windows app's format:
    /// &lt;Apps&gt;&lt;App DisplayName="…" FileName="…" Arguments="…" … /&gt;&lt;/Apps&gt;.
    /// </summary>
    public static class ExternalToolsFile
    {
        public const string FileName = "extApps.xml";

        private static readonly string[] KnownAttributes =
        [
            "DisplayName", "Category", "FileName", "Arguments", "WorkingDir", "WaitForExit", "TryToIntegrate", "RunElevated",
            "ShowOnToolbar"
        ];

        /// <summary>The tools in <paramref name="path"/>; none when the file does not exist.</summary>
        /// <exception cref="XmlException">The file is not an external tools file.</exception>
        public static List<ExternalToolDefinition> Load(string path)
        {
            if (!File.Exists(path))
                return [];

            XmlDocument document = SecureXmlHelper.LoadXmlFromFile(path);
            if (document.DocumentElement is not { Name: "Apps" } apps)
                throw new XmlException($"{path} is not an external tools file.");

            return apps.ChildNodes.OfType<XmlElement>().Where(element => element.Name == "App").Select(Read).ToList();
        }

        public static void Save(string path, IEnumerable<ExternalToolDefinition> tools)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            XmlWriterSettings settings = new() { Indent = true, IndentChars = "    ", Encoding = new UTF8Encoding(false) };
            string temporary = path + ".tmp";
            using (XmlWriter writer = XmlWriter.Create(temporary, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("Apps");
                foreach (ExternalToolDefinition tool in tools)
                    Write(writer, tool);
                writer.WriteEndElement();
                writer.WriteEndDocument();
            }

            File.Move(temporary, path, overwrite: true);
        }

        private static ExternalToolDefinition Read(XmlElement element)
        {
            ExternalToolDefinition tool = new()
            {
                DisplayName = element.GetAttribute("DisplayName"),
                Category = element.GetAttribute("Category"),
                FileName = element.GetAttribute("FileName"),
                Arguments = element.GetAttribute("Arguments"),
                WorkingDir = element.GetAttribute("WorkingDir"),
                WaitForExit = Flag(element, "WaitForExit", false),
                TryIntegrate = Flag(element, "TryToIntegrate", false),
                RunElevated = Flag(element, "RunElevated", false),
                ShowOnToolbar = Flag(element, "ShowOnToolbar", true)
            };
            foreach (XmlAttribute attribute in element.Attributes)
            {
                if (!KnownAttributes.Contains(attribute.Name))
                    tool.OtherAttributes[attribute.Name] = attribute.Value;
            }

            return tool;
        }

        private static void Write(XmlWriter writer, ExternalToolDefinition tool)
        {
            writer.WriteStartElement("App");
            writer.WriteAttributeString("DisplayName", tool.DisplayName);
            writer.WriteAttributeString("Category", tool.Category);
            writer.WriteAttributeString("FileName", tool.FileName);
            writer.WriteAttributeString("Arguments", tool.Arguments);
            writer.WriteAttributeString("WorkingDir", tool.WorkingDir);
            // As Convert.ToString(bool) writes them on Windows.
            writer.WriteAttributeString("WaitForExit", tool.WaitForExit ? "True" : "False");
            writer.WriteAttributeString("TryToIntegrate", tool.TryIntegrate ? "True" : "False");
            writer.WriteAttributeString("RunElevated", tool.RunElevated ? "True" : "False");
            writer.WriteAttributeString("ShowOnToolbar", tool.ShowOnToolbar ? "True" : "False");
            foreach (KeyValuePair<string, string> attribute in tool.OtherAttributes)
                writer.WriteAttributeString(attribute.Key, attribute.Value);
            writer.WriteEndElement();
        }

        private static bool Flag(XmlElement element, string name, bool missing) =>
            element.HasAttribute(name) ? bool.TryParse(element.GetAttribute(name), out bool value) && value : missing;
    }
}
