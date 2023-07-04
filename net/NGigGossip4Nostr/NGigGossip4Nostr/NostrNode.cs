using System;
using System.Diagnostics;

namespace NGigGossip4Nostr;

public abstract class NostrNode : NamedEntity
{
	public NostrNode(string name) : base(name)
	{
	}

    class Message
    {
        public string senderNodeName;
        public object frame;
    }

    private object locker = new object();
    private Queue<Message> message_queue = new();
    private Thread thread = null;

    public void SendMessage(string targetNodeName, object frame)
    {
        var targetNode = (NostrNode)NamedEntity.GetByEntityName(targetNodeName);
        lock (targetNode.locker)
        {
            targetNode.message_queue.Enqueue(new Message() { senderNodeName = this.Name, frame = frame });
            Monitor.PulseAll(targetNode.locker);
        }
    }

    public abstract void OnMessage(string senderNodeName, object frame);


    public void Start()
    {
        thread = new Thread(new ThreadStart(() =>
        {
            while (true)
            {
                lock (locker)
                {
                    while (message_queue.Count > 0)
                    {
                        var message = message_queue.Dequeue();
                        if (message == null)
                            return;
                        try
                        {
                            Monitor.Exit(locker);
                            OnMessage(message.senderNodeName, message.frame);
                        }
                        finally
                        {
                            Monitor.Enter(locker);
                        }
                    }
                    Monitor.Wait(locker);
                }
            }
        }));
        thread.Start();
    }

    public void Stop()
    {
        lock (locker)
        {
            message_queue.Enqueue(null);
        }
    }
    public void Join()
    {
        thread.Join();
    }
}

