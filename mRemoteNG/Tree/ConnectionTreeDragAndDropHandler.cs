using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using BrightIdeasSoftware;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Tree.Root;
using mRemoteNG.Resources.Language;
using System.Runtime.Versioning;

namespace mRemoteNG.Tree
{
    [SupportedOSPlatform("windows")]
    public class ConnectionTreeDragAndDropHandler
    {
        private readonly Color DropAllowedFeedbackColor = Color.Green;
        private readonly Color DropDeniedFeedbackColor = Color.Red;
        private string? _infoMessage;
        private Color _currentFeedbackColor;
        private bool _enableFeedback;


        public void HandleEvent_ModelDropped(object? sender, ModelDropEventArgs e)
        {
            if (!(e.TargetModel is ConnectionInfo dropTarget)) return;
            foreach(ConnectionInfo dropSource in e.SourceModels.Cast<ConnectionInfo>())
            {
                DropModel(dropSource, dropTarget, e.DropTargetLocation);
            }           
            
            e.Handled = true;
        }

        public void DropModel(ConnectionInfo dropSource,
                              ConnectionInfo dropTarget,
                              DropTargetLocation dropTargetLocation)
        {
            switch (dropTargetLocation)
            {
                case DropTargetLocation.Item:
                    ConnectionTreeMoveRules.Move(dropSource, dropTarget, MovePosition.Onto);
                    break;
                case DropTargetLocation.AboveItem:
                    ConnectionTreeMoveRules.Move(dropSource, dropTarget, MovePosition.Above);
                    break;
                case DropTargetLocation.BelowItem:
                    ConnectionTreeMoveRules.Move(dropSource, dropTarget, MovePosition.Below);
                    break;
            }
        }

        public void HandleEvent_ModelCanDrop(object? sender, ModelDropEventArgs e)
        {
            _enableFeedback = true;
            _currentFeedbackColor = DropDeniedFeedbackColor;
            _infoMessage = null;
            foreach (ConnectionInfo dropSource in e.SourceModels.Cast<ConnectionInfo>())
            {
                ConnectionInfo? dropTarget = e.TargetModel as ConnectionInfo;

                e.Effect = CanModelDrop(dropSource, dropTarget, e.DropTargetLocation);
                e.InfoMessage = _infoMessage;
                e.DropSink.EnableFeedback = _enableFeedback;
                e.DropSink.FeedbackColor = _currentFeedbackColor;
            }
            e.Handled = true;
        }

        public DragDropEffects CanModelDrop(ConnectionInfo dropSource,
                                            ConnectionInfo? dropTarget,
                                            DropTargetLocation dropTargetLocation)
        {
            DragDropEffects dragDropEffect = DragDropEffects.None;
            if (!NodeIsDraggable(dropSource))
            {
                _infoMessage = Language.NodeNotDraggable;
                _enableFeedback = false;
            }
            else
                switch (dropTargetLocation)
                {
                    case DropTargetLocation.Item:
                        dragDropEffect = HandleCanDropOnItem(dropSource, dropTarget);
                        break;
                    case DropTargetLocation.AboveItem:
                    case DropTargetLocation.BelowItem:
                        dragDropEffect = HandleCanDropBetweenItems(dropSource, dropTarget);
                        break;
                }

            return dragDropEffect;
        }

        private DragDropEffects HandleCanDropOnItem(ConnectionInfo dropSource, ConnectionInfo? dropTarget)
        {
            DragDropEffects dragDropEffect = DragDropEffects.None;
            if (dropTarget is ContainerInfo && !(dropTarget is RootPuttySessionsNodeInfo))
            {
                if (!IsValidDrag(dropSource, dropTarget)) return dragDropEffect;
                dragDropEffect = DragDropEffects.Move;
                _currentFeedbackColor = DropAllowedFeedbackColor;
            }
            else
            {
                _enableFeedback = false;
            }

            return dragDropEffect;
        }

        private DragDropEffects HandleCanDropBetweenItems(ConnectionInfo dropSource, ConnectionInfo? dropTarget)
        {
            DragDropEffects dragDropEffect = DragDropEffects.None;
            if (ConnectionTreeMoveRules.IsAncestorOf(dropSource, dropTarget))
                _infoMessage = Language.NodeCannotDragParentOnChild;
            else if (dropTarget is PuttySessionInfo || dropTarget is RootNodeInfo)
                _enableFeedback = false;
            else
            {
                dragDropEffect = DragDropEffects.Move;
                _currentFeedbackColor = DropAllowedFeedbackColor;
            }

            return dragDropEffect;
        }

        private bool IsValidDrag(ConnectionInfo dropSource, ConnectionInfo dropTarget)
        {
            _infoMessage = ConnectionTreeMoveRules.ReasonNotMovableOnto(dropSource, dropTarget);
            return _infoMessage == null;
        }

        private bool NodeIsDraggable(ConnectionInfo node)
        {
            return ConnectionTreeMoveRules.IsMovable(node) && node is not PuttySessionInfo;
        }
    }
}