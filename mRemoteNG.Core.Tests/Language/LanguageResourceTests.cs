using System.Globalization;
using mRemoteNG.Resources.Language;
using NUnit.Framework;

namespace mRemoteNG.Core.Tests.Language
{
    /// <summary>
    /// The translations moved from the WinForms app into mRemoteNG.Core; the manifest resource name
    /// ("mRemoteNG.Language.Language") and the satellite assemblies must keep working from there.
    /// </summary>
    public class LanguageResourceTests
    {
        [TestCase("", "Connect")]
        [TestCase("en-US", "Connect")]
        [TestCase("nl", "Verbind")]
        [TestCase("de", "Verbinden")]
        public void ResolvesStringsForCulture(string cultureName, string expected)
        {
            string actual = mRemoteNG.Resources.Language.Language.ResourceManager
                .GetString(nameof(mRemoteNG.Resources.Language.Language.Connect), new CultureInfo(cultureName));

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void StronglyTypedAccessorIsPublic()
        {
            Assert.That(typeof(mRemoteNG.Resources.Language.Language).IsPublic, Is.True);
        }
    }
}
