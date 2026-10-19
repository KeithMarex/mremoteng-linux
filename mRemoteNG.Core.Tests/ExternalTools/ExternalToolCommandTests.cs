using System.Collections.Generic;
using mRemoteNG.Connection;
using mRemoteNG.ExternalTools;
using NUnit.Framework;

namespace mRemoteNG.Core.Tests.ExternalTools
{
    public class ExternalToolCommandTests
    {
        private static readonly Dictionary<string, string> Environment = new() { ["HOME"] = "/home/koen", ["EDITOR"] = "vim" };

        private ConnectionInfo _connection;

        [SetUp]
        public void Setup() => _connection = new ConnectionInfo
        {
            Name = "Web 1", Hostname = "web1.example.com", Port = 2222, Username = "deploy", Password = "s3cret",
            Domain = "CORP", Description = "front end", MacAddress = "00:11:22:33:44:55", UserField = "rack 4"
        };

        [TestCase("a b  c", new[] { "a", "b", "c" })]
        [TestCase("\"a b\" 'c d'", new[] { "a b", "c d" })]
        [TestCase("-p \"\" x", new[] { "-p", "", "x" })]
        [TestCase(@"a\ b ""q\""uote"" 'back\slash'", new[] { "a b", "q\"uote", @"back\slash" })]
        [TestCase("", new string[0])]
        public void ArgumentsAreSplitLikeAShellWould(string text, string[] expected)
        {
            Assert.That(ExternalToolCommand.Split(text), Is.EqualTo(expected));
        }

        [TestCase("%HOSTNAME%", "web1.example.com")]
        [TestCase("%hostname%:%PORT%", "web1.example.com:2222")]
        [TestCase("%USERNAME%@%HostName%", "deploy@web1.example.com")]
        [TestCase("%PASSWORD% %DOMAIN% %NAME%", "s3cret CORP Web 1")]
        [TestCase("%DESCRIPTION%|%MACADDRESS%|%USERFIELD%", "front end|00:11:22:33:44:55|rack 4")]
        [TestCase("%-HOSTNAME% %!HOSTNAME%", "web1.example.com web1.example.com")]
        [TestCase("%HOME%/bin", "/home/koen/bin")]
        [TestCase(@"\%EDITOR\%", "vim")]
        [TestCase("^%HOSTNAME^%", "%HOSTNAME%")]
        [TestCase("100% %UNKNOWN% 50%", "100% %UNKNOWN% 50%")]
        [TestCase("%%", "%%")]
        public void VariablesAreFilledInAsOnWindows(string text, string expected)
        {
            Assert.That(ExternalToolCommand.Expand(text, _connection, name => Environment.GetValueOrDefault(name)), Is.EqualTo(expected));
        }

        [Test]
        public void AValueNeverBecomesAnExtraArgument()
        {
            _connection.Hostname = "host; rm -rf ~ --option";
            ExternalToolDefinition tool = new() { FileName = "ping", Arguments = "-c 3 %HOSTNAME%" };

            ExternalToolCommand command = ExternalToolCommand.For(tool, _connection, _ => null);

            Assert.That(command.Arguments, Is.EqualTo(new[] { "-c", "3", "host; rm -rf ~ --option" }));
        }

        [Test]
        public void TheProgramAndWorkingDirectoryAreFilledInToo()
        {
            ExternalToolDefinition tool = new() { FileName = " %HOME%/bin/tool ", Arguments = "%NAME%", WorkingDir = "%HOME%" };

            ExternalToolCommand command = ExternalToolCommand.For(tool, _connection, name => Environment.GetValueOrDefault(name));

            Assert.That((command.FileName, command.WorkingDirectory), Is.EqualTo(("/home/koen/bin/tool", "/home/koen")));
            Assert.That(command.Arguments, Is.EqualTo(new[] { "Web 1" }));
        }

        [Test]
        public void TheDescriptionNeverShowsThePassword()
        {
            ExternalToolDefinition tool = new() { FileName = "sshpass", Arguments = "-p %PASSWORD% ssh %HOSTNAME%" };

            string shown = ExternalToolCommand.For(tool, _connection, _ => null).Describe(_connection);

            Assert.That(shown, Is.EqualTo("sshpass -p *** ssh web1.example.com"));
        }

        [Test]
        public void TheDescriptionQuotesAsAShellWould()
        {
            ExternalToolDefinition tool = new() { FileName = "sh", Arguments = "-c 'echo \"$0\"' %NAME% \"\" it\\'s" };

            string shown = ExternalToolCommand.For(tool, _connection, _ => null).Describe(_connection);

            Assert.That(shown, Is.EqualTo("sh -c 'echo \"$0\"' 'Web 1' '' 'it'\\''s'"));
        }
    }
}
