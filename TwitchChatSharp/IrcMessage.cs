using System.Collections.Generic;
using System.Text;

namespace TwitchChatSharp
{
    public enum ChatEdgeCluster {
        Main,
        Event,
        Group,
        Aws
    }

    public enum IrcCommand {
        Unknown,
        PrivMsg,
        Notice,
        Ping,
        Pong,
        Join,
        Part,
        HostTarget,
        ClearChat,
        UserState,
        GlobalUserState,
        Nick,
        Pass,
        Cap,
        RPL_001,
        RPL_002,
        RPL_003,
        RPL_004,
        RPL_353,
        RPL_366,
        RPL_372,
        RPL_375,
        RPL_376,
        Whisper,
        RoomState,
        Reconnect,
        ServerChange,
        UserNotice
	}

    /// <summary>
    /// IRC PRIVMSG information
    /// </summary>
    public struct IrcMessage
    {
        /// <summary>
        /// The channel the message was sent in
        /// </summary>
        public string Channel { get { return Params; } }

        public string Params
        {
            get
            {
                return Parameters != null && Parameters.Length > 0 ? Parameters[0] : "";
            }
        }

        /// <summary>
        /// Message itself
        /// </summary>
        public string Message { get { return Trailing; } }

        public string Trailing
        {
            get
            {
                return Parameters != null && Parameters.Length > 1 ? Parameters[Parameters.Length - 1] : "";
            }
        }

        /// <summary>
        /// Command parameters
        /// </summary>
        private readonly string[] Parameters;

        /// <summary>
        /// The user whose message it is
        /// </summary>
        public readonly string User;

        /// <summary>
        /// Hostmask of the user, e.g. ventic!ventic@3v.fi
        /// </summary>
        public readonly string Hostmask;

        /// <summary>
        /// Raw Command
        /// </summary>
        public readonly IrcCommand Command;

        /// <summary>
        /// IRCv3 tags
        /// </summary>
        public readonly Dictionary<string, string> Tags;



        /// <summary>
        /// Create an INCOMPLETE IrcMessage only carrying username
        /// </summary>
        /// <param name="user"></param>
        public IrcMessage(string user)
        {
            Parameters = null;
            User = user;
            Hostmask = null;
            Command = IrcCommand.Unknown;
            Tags = null;
        }


        /// <summary>
        /// Create an IrcMessage
        /// </summary>
        /// <param name="command">IRC Command</param>
        /// <param name="parameters">Command params</param>
        /// <param name="hostmask">User</param>
        /// <param name="tags">IRCv3 tags</param>
        public IrcMessage(IrcCommand command, string[] parameters, string hostmask, Dictionary<string, string> tags = null)
        {
            var idx = hostmask.IndexOf('!');
            User = idx != -1 ? hostmask.Substring(0, idx) : hostmask;
            Hostmask = hostmask;
            Parameters = parameters;
            Command = command;
            Tags = tags;
        }


        /// <summary>
        /// Swap the first character of the message
        /// </summary>
        /// <param name="prefix">new character</param>
        /// <returns>Updated IrcMessage</returns>
        public IrcMessage SwapCommandPrefix(string prefix)
        {
            if (Parameters.Length > 1 && Parameters[1].Length >= 1)
                return new IrcMessage(Command, new string[] { Parameters[0], prefix + Parameters[1].Substring(1) }, User, Tags);
            else
                return this;
        }


        public new string ToString()
        {
            StringBuilder raw = new StringBuilder(32);
            if (Tags != null)
            {
                string[] tags = new string[Tags.Count];
                int i = 0;
                foreach (var tag in Tags)
                {
                    tags[i] = tag.Key + "=" + tag.Value;
                    ++i;
                }
                if (tags.Length > 0)
                {
                    raw.Append("@").Append(string.Join(";", tags)).Append(" ");
                }
            }
            if (Hostmask != null && Hostmask.Length > 0)
            {
                raw.Append(":").Append(Hostmask).Append(" ");
            }
            raw.Append(Command.ToString().ToUpper().Replace("RPL_", ""));
            if (Parameters.Length > 0)
            {
                if (Parameters[0] != null && Parameters[0].Length > 0)
                {
                    raw.Append(" ").Append(Parameters[0]);
                }
                if (Parameters.Length > 1 && Parameters[1] != null && Parameters[1].Length > 0)
                {
                    raw.Append(" :").Append(Parameters[1]);
                }
            }
            return raw.ToString();
        }


        private enum parserState
        {
            STATE_NONE,
            STATE_V3,
            STATE_PREFIX,
            STATE_COMMAND,
            STATE_PARAM,
            STATE_TRAILING
        };


        /// <summary>
        /// Builds an IrcMessage from a raw string
        /// </summary>
        /// <param name="raw">Raw IRC message</param>
        /// <returns>IrcMessage object</returns>
        public static IrcMessage FromRawMessage(string raw)
        {
            var tagDict = new Dictionary<string, string>();

            parserState state = parserState.STATE_NONE;
            int[] starts = new int[] { 0, 0, 0, 0, 0, 0 };
            int[] lens = new int[] { 0, 0, 0, 0, 0, 0 };
            for (int i = 0; i < raw.Length; ++i)
            {
                lens[(int) state] = i - starts[(int) state] - 1;
                if (state == parserState.STATE_NONE && raw[i] == '@')
                {
                    state = parserState.STATE_V3;
                    starts[(int) state] = ++i;

                    int start = i;
                    string key = null;
                    for (; i < raw.Length; ++i)
                    {
                        if (raw[i] == '=')
                        {
                            key = raw.Substring(start, i - start);
                            start = i + 1;
                        }
                        else if (raw[i] == ';')
                        {
                            if (key == null)
                                tagDict[raw.Substring(start, i - start)] = "1";
                            else
                                tagDict[key] = raw.Substring(start, i - start);
                            start = i + 1;
                        }
                        else if (raw[i] == ' ')
                        {
                            if (key == null)
                                tagDict[raw.Substring(start, i - start)] = "1";
                            else
                                tagDict[key] = raw.Substring(start, i - start);
                            break;
                        }
                    }
                }
                else if (state < parserState.STATE_PREFIX && raw[i] == ':')
                {
                    state = parserState.STATE_PREFIX;
                    starts[(int) state] = ++i;
                }
                else if (state < parserState.STATE_COMMAND)
                {
                    state = parserState.STATE_COMMAND;
                    starts[(int) state] = i;
                }
                else if (state < parserState.STATE_TRAILING && raw[i] == ':')
                {
                    state = parserState.STATE_TRAILING;
                    starts[(int) state] = ++i;
                    break;
                }
                else if (state == parserState.STATE_COMMAND)
                {
                    state = parserState.STATE_PARAM;
                    starts[(int) state] = i;
                }
                while (i < raw.Length && raw[i] != ' ')
                    ++i;
            }
            lens[(int) state] = raw.Length - starts[(int) state];
            string cmd = raw.Substring(starts[(int) parserState.STATE_COMMAND], lens[(int) parserState.STATE_COMMAND]);

            IrcCommand command = IrcCommand.Unknown;
            switch (cmd)
            {
                case "PRIVMSG":
                    command = IrcCommand.PrivMsg;
                    break;
                case "NOTICE":
                    command = IrcCommand.Notice;
                    break;
                case "PING":
                    command = IrcCommand.Ping;
                    break;
                case "PONG":
                    command = IrcCommand.Pong;
                    break;
                case "HOSTTARGET":
                    command = IrcCommand.HostTarget;
                    break;
                case "CLEARCHAT":
                    command = IrcCommand.ClearChat;
                    break;
                case "USERSTATE":
                    command = IrcCommand.UserState;
                    break;
                case "GLOBALUSERSTATE":
                    command = IrcCommand.GlobalUserState;
                    break;
                case "NICK":
                    command = IrcCommand.Nick;
                    break;
                case "JOIN":
                    command = IrcCommand.Join;
                    break;
                case "PART":
                    command = IrcCommand.Part;
                    break;
                case "PASS":
                    command = IrcCommand.Pass;
                    break;
                case "CAP":
                    command = IrcCommand.Cap;
                    break;
                case "001":
                    command = IrcCommand.RPL_001;
                    break;
                case "002":
                    command = IrcCommand.RPL_002;
                    break;
                case "003":
                    command = IrcCommand.RPL_003;
                    break;
                case "004":
                    command = IrcCommand.RPL_004;
                    break;
                case "353":
                    command = IrcCommand.RPL_353;
                    break;
                case "366":
                    command = IrcCommand.RPL_366;
                    break;
                case "372":
                    command = IrcCommand.RPL_372;
                    break;
                case "375":
                    command = IrcCommand.RPL_375;
                    break;
                case "376":
                    command = IrcCommand.RPL_376;
                    break;
                case "WHISPER":
                    command = IrcCommand.Whisper;
                    break;
                case "SERVERCHANGE":
                    command = IrcCommand.ServerChange;
                    break;
                case "RECONNECT":
                    command = IrcCommand.Reconnect;
                    break;
                case "ROOMSTATE":
                    command = IrcCommand.RoomState;
                    break;
                case "USERNOTICE":
                    command = IrcCommand.UserNotice;
                    break;
            }

            string parameters = raw.Substring(starts[(int) parserState.STATE_PARAM], lens[(int) parserState.STATE_PARAM]);
            string message = raw.Substring(starts[(int) parserState.STATE_TRAILING], lens[(int) parserState.STATE_TRAILING]);
            string hostmask = raw.Substring(starts[(int) parserState.STATE_PREFIX], lens[(int) parserState.STATE_PREFIX]);
            return new IrcMessage(command, new string[] { parameters, message }, hostmask, tagDict);
        }
    }
}
