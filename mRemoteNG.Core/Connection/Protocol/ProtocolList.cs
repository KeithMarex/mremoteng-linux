using System;
using System.Collections;
using System.Collections.Specialized;

// ReSharper disable ArrangeAccessorOwnerBody

namespace mRemoteNG.Connection.Protocol
{
    public class ProtocolList : CollectionBase, INotifyCollectionChanged
    {
        public IProtocolSession this[object index]
        {
            get
            {
                IProtocolSession @base = index as IProtocolSession;
                if (@base != null)
                    return @base;
                if (index is int)
                    return (IProtocolSession)List[Convert.ToInt32(index)];
                return null;
            }
        }

        public new int Count => List.Count;

        public void Add(IProtocolSession cProt)
        {
            List.Add(cProt);
            RaiseCollectionChangedEvent(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, cProt));
        }

        public void AddRange(IProtocolSession[] cProt)
        {
            foreach (IProtocolSession cP in cProt)
            {
                List.Add(cP);
            }

            RaiseCollectionChangedEvent(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, cProt));
        }

        public void Remove(IProtocolSession cProt)
        {
            try
            {
                if (!List.Contains(cProt))
                    return;

                List.Remove(cProt);
                RaiseCollectionChangedEvent(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, cProt));
            }
            catch (Exception)
            {
            }
        }

        public new void Clear()
        {
            if (Count == 0)
                return;

            List.Clear();
            RaiseCollectionChangedEvent(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public event NotifyCollectionChangedEventHandler? CollectionChanged; // Fix for CS8612: Declare the event as nullable to match the interface.

        private void RaiseCollectionChangedEvent(object sender, NotifyCollectionChangedEventArgs args)
        {
            CollectionChanged?.Invoke(sender, args);
        }
    }
}