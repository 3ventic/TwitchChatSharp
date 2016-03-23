using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchChatSharp 
{
    public class TwitchConnection
    {
        private readonly string _address;
        private readonly int _port;
        private readonly string _pass;
        private readonly string _nick;
        private readonly string[] _caps;
        private readonly bool _secure;

        private IrcConnection _client;

        private bool _blockConnectMessages = false;
        private List<string> _channels = new List<string>();
        private ConcurrentQueue<string> _joinQ = new ConcurrentQueue<string>();

        // Events
        public delegate void Twitch_Connected(object sender, IrcConnectedEventArgs e);
        public delegate void Twitch_MessageReceived(object sender, IrcMessageEventArgs e);
        public delegate void Twitch_Disconnected(object sender, System.EventArgs e);

        public event Twitch_Connected Connected;
        public event Twitch_MessageReceived MessageReceived;
        public event Twitch_Disconnected Reconnected;

        // Methods

        /// <summary>
        /// Initialize a connection to Twitch chat
        /// </summary>
        public TwitchConnection(string nick = "justinfan123", string oauth = "", ChatEdgeCluster cluster = ChatEdgeCluster.Aws, int port = -1, string[] capRequests = null, int ratelimit = 1500, bool secure = true)
        {
            _address = GetServerAddress(cluster);
            _pass = "oauth:" + oauth;
            _nick = nick;
            _caps = capRequests ?? new string[2] { "twitch.tv/tags", "twitch.tv/commands" };
            _secure = cluster == ChatEdgeCluster.Aws ? secure : false;

            if (port == -1)
            {
                if (cluster == ChatEdgeCluster.Aws && secure)
                {
                    _port = 6697;
                }
                else
                {
                    _port = 6667;
                }
            }
            else
            {
                _port = port;
            }

            InitializeIrcConnection(ratelimit);
        }

        /// <summary>
        /// Initialize a connection to Twitch chat via a defined proxy/relay
        /// </summary>
        public TwitchConnection(string address, string nick = "justinfan123", string oauth = "", int port = 6667, string[] capRequests = null, int ratelimit = 1500, bool secure = false)
        {
            _address = address;
            _port = port;
            _pass = "oauth:" + oauth;
            _nick = nick;
            _caps = capRequests ?? new string[2] { "twitch.tv/tags", "twitch.tv/commands" };
            _secure = secure;
            InitializeIrcConnection(ratelimit);
        }

        private void InitializeIrcConnection(int ratelimit)
        {
            _client = new IrcConnection(ratelimit);

            Thread chanJoiner = new Thread(ChannelJoiner);
            chanJoiner.Start();
        }

        /// <summary>
        /// Connect to chat
        /// </summary>
        public void Connect()
        {
            ConnectAsync().Wait();
        }

        /// <summary>
        /// Connect to chat
        /// </summary>
        public async Task ConnectAsync()
        {
            _client.Connected += _client_Connected;
            _client.MessageReceived += _client_MessageReceived;
            _client.Disconnected += _client_Disconnected;
            await _client.ConnectAsync(_address, _port, _secure);
        }

        private async void _client_Disconnected(object sender = null, System.EventArgs e = null)
        {
            if (Reconnected != null)
            {
                Reconnected(this, e);
            }
            Thread.Sleep(50);
            _blockConnectMessages = true;
            await _client.ConnectAsync(_address, _port, _secure);
            foreach (var channel in _channels)
            {
                _joinQ.Enqueue("JOIN " + channel);
            }
        }

        private void _client_Connected(object sender, IrcConnectedEventArgs e)
        {
            _client.EnqueueMessage("CAP REQ :" + string.Join(" ", _caps), true);
            _client.EnqueueMessage("PASS " + _pass, true);
            _client.EnqueueMessage("NICK " + _nick, true);
            foreach (var chan in _channels)
            {
                _client.EnqueueMessage("JOIN " + chan, true);
            }

            if (Connected != null)
            {
                Connected(this, new IrcConnectedEventArgs(e.Endpoint));
            }
        }

        private void _client_MessageReceived(object o, IrcMessageEventArgs e)
        {
            var ircMsg = e.Message;
            switch (ircMsg.Command)
            {
                case IrcCommand.Join:
                    if (ircMsg.User == _nick)
                    {
                        if (_channels.Contains(ircMsg.Channel))
                            return;
                        _channels.Add(ircMsg.Channel);
                    }
                    break;
                case IrcCommand.Part:
                    if (ircMsg.User == _nick)
                    {
                        _channels.Remove(ircMsg.Channel);
                    }
                    break;
                case IrcCommand.RPL_376:
                    _blockConnectMessages = false;
                    break;
            }

            if (!_blockConnectMessages && MessageReceived != null)
            {
                MessageReceived(this, new IrcMessageEventArgs(ircMsg));
            }
        }

        /// <summary>
        /// Channel to join
        /// </summary>
        /// <param name="channel">#channel</param>
        public void JoinChannel(string channel)
        {
            _joinQ.Enqueue("JOIN " + channel);
        }

        /// <summary>
        /// Channel to part
        /// </summary>
        /// <param name="channel">#channel</param>
        public void PartChannel(string channel)
        {
            _joinQ.Enqueue("PART " + channel);
        }

        /// <summary>
        /// Send a message or chat command to a channel
        /// </summary>
        /// <param name="channel">#channel</param>
        /// <param name="message"></param>
        public void SendMessage(string channel, string message)
        {
            _client.EnqueueMessage("PRIVMSG " + channel + " :" + message);
        }

        /// <summary>
        /// Send a message to a channel
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="message"></param>
        public void SendCommandSafeMessage(string channel, string message)
        {
            if (message.StartsWith("/") && !message.StartsWith("/me ") || message.StartsWith(".")) 
            {
                message = "\u200b" + message;
            }
            SendMessage(channel, message);
        }

        /// <summary>
        /// Send a raw IRC message to chat
        /// </summary>
        /// <param name="rawMessage">Raw IRC string</param>
        public void SendRaw(string rawMessage)
        {
            _client.EnqueueMessage(rawMessage);
        }

        private void ChannelJoiner()
        {
            string message;
            while (true)
            {
                if (!_joinQ.IsEmpty && _joinQ.TryDequeue(out message))
                {
                    _client.EnqueueMessage(message);
                    Thread.Sleep(300);
                }
                Thread.Sleep(1);
            }
        }

        // Static methods

        private static string GetServerAddress(ChatEdgeCluster cluster)
        {
            switch (cluster)
            {
                case ChatEdgeCluster.Event:
                    return "event.tmi.twitch.tv";
                case ChatEdgeCluster.Group:
                    return "group.tmi.twitch.tv";
                case ChatEdgeCluster.Main:
                    return "main.tmi.twitch.tv";
                default:
                    return "irc.chat.twitch.tv";
            }
        }
    }
}
