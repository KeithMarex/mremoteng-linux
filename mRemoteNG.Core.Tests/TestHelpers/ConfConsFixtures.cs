using System.IO;
using System.Reflection;

namespace mRemoteNGTests.Properties
{
    /// <summary>
    /// The confCons.xml fixtures from mRemoteNGTests/Resources, embedded as plain files so Core.Tests needs
    /// no WinForms resx. Same names as the generated Resources class in mRemoteNGTests, so the moved tests
    /// are unchanged. The fixtures are ASCII, so UTF-8 reads them exactly like the resx's Windows-1252.
    /// </summary>
    internal static class Resources
    {
        internal static string confCons_v2_5 => Read("confCons_v2_5.xml");
        internal static string confCons_v2_5_fullencryption => Read("confCons_v2_5_fullencryption.xml");
        internal static string confCons_v2_5_passwordis_Password_fullencryption => Read("confCons_v2_5_passwordis_Password_fullencryption.xml");
        internal static string confCons_v2_6 => Read("confCons_v2_6.xml");
        internal static string confCons_v2_6_5k_iterations => Read("confCons_v2_6_5k-iterations.xml");
        internal static string confCons_v2_6_fullencryption => Read("confCons_v2_6_fullencryption.xml");
        internal static string confCons_v2_6_passwordis_Password => Read("confCons_v2_6_passwordis_Password.xml");
        internal static string confCons_v2_6_passwordis_Password_fullencryption => Read("confCons_v2_6_passwordis_Password_fullencryption.xml");

        private static string Read(string fileName)
        {
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ConfConsFixtures." + fileName)
                                  ?? throw new FileNotFoundException("Embedded fixture not found", fileName);
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }
    }
}
