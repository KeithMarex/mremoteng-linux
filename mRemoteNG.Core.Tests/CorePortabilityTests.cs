using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace mRemoteNG.Core.Tests
{
    /// <summary>
    /// Guards the rule that mRemoteNG.Core stays usable on Linux:
    /// it may not depend on Windows-only assemblies.
    /// </summary>
    public class CorePortabilityTests
    {
        private static readonly string[] WindowsOnlyAssemblies =
        [
            "System.Windows.Forms",
            "System.Windows.Forms.Primitives",
            "System.Drawing.Common",
            "System.Design",
            "PresentationCore",
            "PresentationFramework",
            "WindowsBase",
            "Microsoft.Win32.Registry",
            "System.Management",
            "System.DirectoryServices",
            "System.Security.Cryptography.ProtectedData",
            "WeifenLuo.WinFormsUI.Docking",
            "ObjectListView",
            "log4net",
        ];

        [Test]
        public void CoreDoesNotReferenceWindowsOnlyAssemblies()
        {
            Assembly core = Assembly.Load(new AssemblyName("mRemoteNG.Core"));

            string[] offending = core.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name => WindowsOnlyAssemblies.Contains(name))
                .ToArray();

            Assert.That(offending, Is.Empty);
        }
    }
}
