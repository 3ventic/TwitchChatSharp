using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchChatSharp
{
    internal class IrcConnection
    {
        private static string[] _messageSeparators = new string[] { "\r\n" };

        private TcpClient _client;
        private SslStream _sslstream;
        private NetworkStream _networkstream;
        private bool _secure;
        private ConcurrentQueue<string> _sendQ = new ConcurrentQueue<string>();
        private ConcurrentQueue<string> _prioritySendQ = new ConcurrentQueue<string>();
        private int _rateLimit;

        // Events
        internal delegate void Irc_Connected(object sender, IrcConnectedEventArgs e);
        internal delegate void Irc_MessageReceived(object sender, IrcMessageEventArgs e);
        internal delegate void Irc_Disconnected(object sender = null, EventArgs e = null);

        internal event Irc_Connected Connected;
        internal event Irc_MessageReceived MessageReceived;
        internal event Irc_Disconnected Disconnected;

        // Methods

        /// <summary>
        /// Is the client currently connected?
        /// </summary>
        /// <returns></returns>
        internal bool IsConnected()
        {
            return _client.Connected;
        }

        /// <summary>
        /// Create a new IRC connection
        /// </summary>
        /// <param name="rateLimit"></param>
        internal IrcConnection(int rateLimit = 1500)
        {
            _rateLimit = rateLimit;
        }

        internal async Task ConnectAsync(string address, int port, bool secure)
        {
            System.Timers.Timer pinger = new System.Timers.Timer();
            pinger.Interval = 30000;
            pinger.Elapsed += (object o, System.Timers.ElapsedEventArgs e) =>
            {
                EnqueueMessage("PING " + (DateTime.Now.Ticks / TimeSpan.TicksPerSecond), true);
            };
            pinger.Start();

            _secure = secure;
            _client = new TcpClient();
            try
            {
                await _client.ConnectAsync(address, port);
            }
            catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException)
            {
                Disconnect();
                return;
            }
            try
            {
                if (_secure)
                {
                    var sslstream = new SslStream(_client.GetStream());
                    sslstream.AuthenticateAsClient(address);
                    _sslstream = sslstream;
                }
                _networkstream = _client.GetStream();
            }
            catch (ObjectDisposedException)
            {
                Disconnect();
                return;
            }

            string endpoint = "";
            try
            {
                endpoint = _client.Client.RemoteEndPoint.ToString();
            }
            catch (NullReferenceException) { }

            Connected(this, new IrcConnectedEventArgs(endpoint));

            Thread tlisten = new Thread(() => ListenerAsync());
            Thread tsend = new Thread(() => Sender());
            tlisten.Start();
            tsend.Start();
        }

        private void Disconnect()
        {
            _client.Close();
            Disconnected();
        }

        private async void ListenerAsync()
        {
            byte[] buffer = new byte[1024];
            StringBuilder msg = new StringBuilder();

            int badMessagesReceived = 0;

            while (_client.Connected)
            {
                if (_networkstream.DataAvailable)
                {
                    Array.Clear(buffer, 0, buffer.Length);

                    try
                    {
                        if (_secure)
                        {
                            await _sslstream.ReadAsync(buffer, 0, buffer.Length);
                        }
                        else
                        {
                            await _networkstream.ReadAsync(buffer, 0, buffer.Length);
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        Disconnect();
                        break;
                    }

                    string text = Encoding.UTF8.GetString(buffer).TrimEnd('\0');

                    msg.Append(text);

                    string msgstr = msg.ToString();
                    if (!msgstr.EndsWith("\r\n"))
                    {
                        bool all0 = true;
                        foreach (var b in buffer)
                        {
                            if (b != 0)
                            {
                                all0 = false;
                                break;
                            }
                        }

                        if (all0)
                        {
                            if (++badMessagesReceived <= 2)
                                continue;
                            else
                            {
                                Disconnect();
                                break;
                            }
                        }
                        else
                        {
                            var idx = msgstr.LastIndexOf("\r\n");
                            if (idx != -1)
                            {
                                idx += 2;
                                msg.Remove(0, idx);
                                msgstr = msgstr.Substring(0, idx);
                            }
                            else
                                continue;
                        }
                    }
                    else
                        msg.Clear();

                    string[] messages = msgstr.Split(_messageSeparators, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string message in messages)
                    {
                        var ircMsg = IrcMessage.FromRawMessage(message);

                        MessageReceived(this, new IrcMessageEventArgs(ircMsg));

                        switch (ircMsg.Command)
                        {
                            case IrcCommand.Ping:
                                EnqueueMessage(message.Replace("PING", "PONG"), true);
                                break;
                            case IrcCommand.Reconnect:
                                Disconnect();
                                break;
                        }
                    }
                }
                else
                {
                    Thread.Sleep(5);
                }
            }
        }

        internal void EnqueueMessage(string message, bool hasPriority = false)
        {
            (hasPriority ? _prioritySendQ : _sendQ).Enqueue(message);
        }

        private void Sender()
        {
            while (_client.Connected)
            {
                string message;
                if (!_prioritySendQ.IsEmpty && _prioritySendQ.TryDequeue(out message))
                {
                    TransmitMessage(message);
                }
                else if (!_sendQ.IsEmpty && _sendQ.TryDequeue(out message))
                {
                    TransmitMessage(message);

                    Thread.Sleep(_rateLimit);
                }

                Thread.Sleep(1);
            }
        }

        private void TransmitMessage(string message)
        {
            message = message.Replace("\r\n", " ");

            byte[] buffer = Encoding.UTF8.GetBytes(message + "\r\n");
            try
            {
                if (_secure)
                {
                    _sslstream.Write(buffer, 0, buffer.Length);
                }
                else
                {
                    _networkstream.Write(buffer, 0, buffer.Length);
                }
            }
            catch (ObjectDisposedException) { Disconnect(); }
            catch (IOException) { Disconnect(); }
        }
    }
}
