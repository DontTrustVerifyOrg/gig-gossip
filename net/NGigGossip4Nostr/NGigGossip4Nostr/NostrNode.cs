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

    private Queue<Message> message_queue = new();
    private Thread thread = null;

    public void SendMessage(string targetNodeName, object frame)
    {
        var targetNode = (NostrNode)NamedEntity.GetByEntityName(targetNodeName);
        lock (targetNode.message_queue)
        {
            targetNode.message_queue.Enqueue(new Message() { senderNodeName = this.Name, frame = frame });
            Monitor.PulseAll(targetNode.message_queue);
        }
    }

    public abstract void OnMessage(string senderNodeName, object frame);


    public void Start()
    {
        thread = new Thread(new ThreadStart(() =>
        {
            while (true)
            {
                lock (message_queue)
                {
                    while (message_queue.Count > 0)
                    {
                        var message = message_queue.Dequeue();
                        if (message == null)
                            return;
                        try
                        {
                            Monitor.Exit(message_queue);
                            OnMessage(message.senderNodeName, message.frame);
                        }
                        finally
                        {
                            Monitor.Enter(message_queue);
                        }
                    }
                    Monitor.Wait(message_queue);
                }
            }
        }));
        thread.Start();
    }

    public void Stop()
    {
        lock (message_queue)
        {
            message_queue.Enqueue(null);
            Monitor.PulseAll(message_queue);
        }
    }
    public void Join()
    {
        thread.Join();
    }
}

