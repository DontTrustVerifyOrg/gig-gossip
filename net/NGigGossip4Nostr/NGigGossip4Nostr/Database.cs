using System;

using NGigGossip4Nostr;


public class GigGossipNodeDatabase
{
    public GigGossipNodeContext Context;
    public GigGossipNodeDatabase(string connectionString)
    {
        Context = new GigGossipNodeContext(connectionString.Replace("$HOME", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        Context.Database.EnsureCreated();
    }

    public void EnsureDeleted()
    {
        Context.Database.EnsureDeleted();
    }
}
