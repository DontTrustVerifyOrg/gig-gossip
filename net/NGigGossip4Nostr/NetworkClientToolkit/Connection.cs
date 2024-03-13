using System;
namespace NetworkClientToolkit;

public enum ServerConnectionState
{
    Open = 0,
    Connecting = 1,
    Closed = 2,
}

public class ServerConnectionStateEventArgs : EventArgs
{
    public required ServerConnectionState State;
    public Uri Uri = null;
}

