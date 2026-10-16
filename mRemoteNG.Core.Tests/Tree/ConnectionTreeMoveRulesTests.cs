using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Tree;
using mRemoteNG.Tree.Root;
using NUnit.Framework;

namespace mRemoteNG.Core.Tests.Tree
{
    public class ConnectionTreeMoveRulesTests
    {
        private RootNodeInfo _root;
        private ContainerInfo _folderA;
        private ContainerInfo _folderB;
        private ConnectionInfo _connection1;
        private ConnectionInfo _connection2;

        [SetUp]
        public void Setup()
        {
            _root = new RootNodeInfo(RootNodeType.Connection);
            _folderA = new ContainerInfo { Name = "A" };
            _folderB = new ContainerInfo { Name = "B" };
            _connection1 = new ConnectionInfo { Name = "1" };
            _connection2 = new ConnectionInfo { Name = "2" };
            _root.AddChild(_folderA);
            _root.AddChild(_folderB);
            _folderA.AddChild(_connection1);
            _folderA.AddChild(_connection2);
        }

        [Test]
        public void TheRootNodeCannotBeMoved()
        {
            Assert.That(ConnectionTreeMoveRules.CanMove(_root, _folderA, MovePosition.Onto), Is.False);
        }

        [Test]
        public void ConnectionsCanMoveIntoAnotherFolder()
        {
            Assert.That(ConnectionTreeMoveRules.CanMove(_connection1, _folderB, MovePosition.Onto), Is.True);
        }

        [Test]
        public void NothingMovesOntoAConnection()
        {
            Assert.That(ConnectionTreeMoveRules.CanMove(_connection1, _connection2, MovePosition.Onto), Is.False);
        }

        [Test]
        public void AFolderCannotMoveIntoItselfOrItsChildren()
        {
            ContainerInfo nested = new();
            _folderA.AddChild(nested);

            Assert.That(ConnectionTreeMoveRules.CanMove(_folderA, _folderA, MovePosition.Onto), Is.False);
            Assert.That(ConnectionTreeMoveRules.CanMove(_folderA, nested, MovePosition.Onto), Is.False);
            Assert.That(ConnectionTreeMoveRules.CanMove(_folderA, _connection1, MovePosition.Below), Is.False);
        }

        [Test]
        public void MovingIntoTheCurrentFolderIsRefusedWithAReason()
        {
            Assert.That(ConnectionTreeMoveRules.ReasonNotMovableOnto(_connection1, _folderA), Is.Not.Null);
            Assert.That(ConnectionTreeMoveRules.CanMove(_connection1, _folderA, MovePosition.Onto), Is.False);
        }

        [Test]
        public void NothingGoesAboveOrBelowTheRoot()
        {
            Assert.That(ConnectionTreeMoveRules.CanMove(_connection1, _root, MovePosition.Above), Is.False);
        }

        [Test]
        public void MovingOntoAFolderReparents()
        {
            ConnectionTreeMoveRules.Move(_connection1, _folderB, MovePosition.Onto);

            Assert.That(_connection1.Parent, Is.SameAs(_folderB));
            Assert.That(_folderA.Children, Does.Not.Contain(_connection1));
        }

        [Test]
        public void MovingBelowASiblingReorders()
        {
            ConnectionTreeMoveRules.Move(_connection1, _connection2, MovePosition.Below);

            Assert.That(_folderA.Children, Is.EqualTo(new[] { _connection2, _connection1 }));
        }

        [Test]
        public void MovingAboveANodeInAnotherFolderMovesThere()
        {
            ConnectionInfo other = new();
            _folderB.AddChild(other);

            ConnectionTreeMoveRules.Move(_connection1, other, MovePosition.Above);

            Assert.That(_folderB.Children, Is.EqualTo(new[] { _connection1, other }));
        }

        [Test]
        public void ARootGroupMovedIntoAFolderBecomesANormalFolder()
        {
            _folderB.IsRootGroup = true;

            ConnectionTreeMoveRules.Move(_folderB, _folderA, MovePosition.Onto);

            Assert.That(_folderB.IsRootGroup, Is.False);
        }
    }
}
