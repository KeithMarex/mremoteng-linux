using System.Linq;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Resources.Language;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.Tree
{
    /// <summary>Where a dragged node lands relative to the node it is dropped on.</summary>
    public enum MovePosition
    {
        Onto,
        Above,
        Below
    }

    /// <summary>
    /// Which moves are allowed in the connection tree, and making them; shared by the WinForms drag-and-drop
    /// handler (which adds its PuTTY-session checks) and the Linux app.
    /// </summary>
    public static class ConnectionTreeMoveRules
    {
        public static bool IsMovable(ConnectionInfo node) => node is not null && node is not RootNodeInfo;

        public static bool IsAncestorOf(ConnectionInfo source, ConnectionInfo? target) =>
            target is not null && source is ContainerInfo sourceAsContainer &&
            sourceAsContainer.GetRecursiveChildList().Contains(target);

        /// <summary>Why <paramref name="source"/> cannot be moved into the folder <paramref name="target"/>; null if it can.</summary>
        public static string? ReasonNotMovableOnto(ConnectionInfo source, ConnectionInfo target)
        {
            if (source.Equals(target))
                return Language.NodeCannotDragOnSelf;
            if (IsAncestorOf(source, target))
                return Language.NodeCannotDragParentOnChild;
            if (target is ContainerInfo targetAsContainer && targetAsContainer.Children.Contains(source))
                return Language.NodeAlreadyInFolder;
            return null;
        }

        public static bool CanMove(ConnectionInfo source, ConnectionInfo? target, MovePosition position)
        {
            if (!IsMovable(source) || target is null)
                return false;

            return position == MovePosition.Onto
                ? target is ContainerInfo and not RootPuttySessionsNodeInfo && ReasonNotMovableOnto(source, target) is null
                : target is not RootNodeInfo && !IsAncestorOf(source, target);
        }

        public static void Move(ConnectionInfo source, ConnectionInfo target, MovePosition position)
        {
            switch (position)
            {
                case MovePosition.Onto:
                    if (target is not ContainerInfo targetAsContainer)
                        return;
                    source.SetParent(targetAsContainer);
                    DemoteRootGroupIfNested(source, targetAsContainer);
                    break;
                case MovePosition.Above:
                    if (!source.Parent.Equals(target.Parent))
                        target.Parent.AddChildAbove(source, target);
                    else
                        target.Parent.SetChildAbove(source, target);
                    DemoteRootGroupIfNested(source, target.Parent);
                    break;
                case MovePosition.Below:
                    if (!source.Parent.Equals(target.Parent))
                        target.Parent.AddChildBelow(source, target);
                    else
                        target.Parent.SetChildBelow(source, target);
                    DemoteRootGroupIfNested(source, target.Parent);
                    break;
            }
        }

        /// <summary>
        /// A container flagged as a top-level root group should only keep that flag while it
        /// remains a direct child of the master root node. If it has been dropped into any
        /// other container it becomes a normal nested folder again.
        /// </summary>
        private static void DemoteRootGroupIfNested(ConnectionInfo source, ContainerInfo newParent)
        {
            if (source is ContainerInfo { IsRootGroup: true } droppedContainer && newParent is not RootNodeInfo)
                droppedContainer.IsRootGroup = false;
        }
    }
}
