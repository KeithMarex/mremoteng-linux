using System.IO;
using System.Reflection;

namespace mRemoteNGTests.Properties
{
    /// <summary>
    /// The test fixtures from mRemoteNGTests/Resources, embedded as plain files so Core.Tests needs no
    /// WinForms resx. Same names as the generated Resources class in mRemoteNGTests, so the moved tests are
    /// unchanged. Text is read with byte-order-mark detection (some fixtures are UTF-16), like the resx.
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

        internal static string test_puttyConnectionManager_database => Read("test_puttyConnectionManager_database.dat");
        internal static string test_remotedesktopconnection_rdp => Read("test_remotedesktopconnection.rdp");
        internal static string test_rdcman_badVersionNumber => Read("test_rdcman_badVersionNumber.rdg");
        internal static string test_rdcman_noversion => Read("test_rdcman_noversion.rdg");
        internal static string test_rdcman_v2_2_badschemaversion => Read("test_rdcman_v2_2_badschemaversion.rdg");
        internal static string test_rdcman_v2_2_schema1 => Read("test_rdcman_v2_2_schema1.rdg");
        // The resx refers to "test_RDCMan_v2_7_schema3.rdg", which only resolves on case-insensitive file systems.
        internal static string test_rdcman_v2_7_schema3 => Read("test_rdcman_v2_7_schema3.rdg");
        internal static string test_rdcman_v2_7_schema3_empty_values => Read("test_rdcman_v2_7_schema3_empty_values.rdg");
        internal static string test_rdcman_v2_7_schema3_null_values => Read("test_rdcman_v2_7_schema3_null_values.rdg");
        internal static string test_securecrt => Read("test_securecrt.xml");

        private static string Read(string fileName)
        {
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Fixtures." + fileName)
                                  ?? throw new FileNotFoundException("Embedded fixture not found", fileName);
            using StreamReader reader = new(stream, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
    }
}
