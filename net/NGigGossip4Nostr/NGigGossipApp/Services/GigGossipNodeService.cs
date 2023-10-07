using System;
using CryptoToolkit;

namespace GigMobile.Services
{
	public static class GigGossipNodeService
	{
		public static GigGossipNode GigGossipNode()
		{
            return new GigGossipNode(
                Path.Combine(FileSystem.AppDataDirectory, "GigGossip.db3"),
                SecureDatabase.GetPrivateKeyAsync().Result.AsECPrivKey(),
                new string[] { ""},
                2048
            );
        }
	}
}

