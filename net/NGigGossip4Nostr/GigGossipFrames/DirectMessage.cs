using System;
namespace GigGossipFrames;

[Serializable]
public class DirectMessage
{
    public required string[] Relays { get; set; }
    public required string Kind { get; set; } 
    public required byte[] Data { get; set; }
}

