using System;
using TwitchChatSharp;

namespace MinimalisticExample 
{
    class Program
    {
        static void Main(string[] args)
        {
            var client = new TwitchConnection(
                cluster: ChatEdgeCluster.Aws,
                nick: "justinfan1",
                oauth: "sad9di9wad", // no oauth: prefix
                port: 6697,
                capRequests: new string[] { "twitch.tv/tags", "twitch.tv/commands" },
                ratelimit: 1500,
                secure: true
                );

            client.Connected += (object sender, IrcConnectedEventArgs e) =>
            {
                Console.WriteLine("Connected");
                client.JoinChannel("#b0aty");
            };

            client.MessageReceived += (object sender, IrcMessageEventArgs e) =>
            {
                Console.WriteLine("Received: " + e.Message.ToString());
            };

            client.Reconnected += (object sender, EventArgs e) =>
            {
                Console.WriteLine("Reconnected");
            };

            client.Connect();
        }
    }
}
