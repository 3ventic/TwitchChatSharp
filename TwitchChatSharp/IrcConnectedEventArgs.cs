using System;

namespace TwitchChatSharp 
{
    public class IrcConnectedEventArgs : EventArgs
    {
        public string Endpoint { get; private set; }

        public IrcConnectedEventArgs(string endpoint)
            : base()
        {
            Endpoint = endpoint;
        }
    }
}