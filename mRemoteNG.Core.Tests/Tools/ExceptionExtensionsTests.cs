using System;
using mRemoteNG.Tools;
using NUnit.Framework;

namespace mRemoteNG.Core.Tests.Tools
{
    public class ExceptionExtensionsTests
    {
        [Test]
        public void SingleExceptionReturnsItsMessage()
        {
            var ex = new InvalidOperationException("outer");

            Assert.That(ex.GetExceptionMessageRecursive(), Is.EqualTo("outer"));
        }

        [Test]
        public void InnerExceptionMessagesFollowOnePerLine()
        {
            var ex = new InvalidOperationException("outer",
                new ArgumentException("middle", new FormatException("inner")));

            Assert.That(ex.GetExceptionMessageRecursive(),
                Is.EqualTo(string.Join(Environment.NewLine, "outer", "middle", "inner")));
        }
    }
}
