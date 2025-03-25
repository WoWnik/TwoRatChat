// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Threading;
using dotIRC;
using Newtonsoft.Json;
using TwoRatChat.Main.Clients;
using TwoRatChat.Main.Helpers;
using TwoRatChat.Main.Properties;
using TwoRatChat.Model;

namespace TwoRatChat.Main.Sources;

public class Twitch_ChatSource : ChatSource
{
    #region smiles

    //public const string UpdateSmilesUri = "https://api.twitch.tv/kraken/chat/emoticons";

    //internal class EmoticonImage {
    //    [JsonProperty( PropertyName = "emoticon_set" )]
    //    public int? Set { get; set; }
    //    [JsonProperty( PropertyName = "url" )]
    //    public string Uri { get; set; }
    //    [JsonProperty( PropertyName = "height" )]
    //    public int? Height { get; set; }
    //    [JsonProperty( PropertyName = "width" )]
    //    public int? Width { get; set; }
    //}

    //internal class Emoticon {
    //    [JsonProperty( PropertyName = "images" )]
    //    public EmoticonImage[] Images { get; set; }
    //    [JsonProperty( PropertyName = "regex" )]
    //    public string Regex { get; set; }

    //    public bool AllowToAdd() {
    //        for (int j = 0; j < Images.Length; ++j)
    //            if (!Images[j].Width.HasValue
    //                &&
    //                !Images[j].Height.HasValue)
    //                return false;

    //        for (int j = 0; j < Images.Length; ++j) {
    //            if (!Images[j].Set.HasValue)
    //                return true;
    //            //if (Images[j].Set.HasValue && Images[j].Set.Value == 1504)
    //            //    return true;
    //        }

    //        return false;
    //    }

    //    public string GetUri() {
    //        for (int j = 0; j < Images.Length; ++j)
    //            if (!Images[j].Set.HasValue)
    //                return Images[j].Uri;
    //        return null;
    //    }
    //}

    //internal class Emoticons {
    //    [JsonProperty( PropertyName = "emoticons" )]
    //    public Emoticon[] EmoticonsArray { get; set; }
    //}

    //internal class premiumEmoticonImage {
    //    [JsonProperty( PropertyName = "state" )]
    //    public string State { get; set; }
    //    [JsonProperty( PropertyName = "regex" )]
    //    public string regex { get; set; }
    //    [JsonProperty( PropertyName = "url" )]
    //    public string Uri { get; set; }
    //    [JsonProperty( PropertyName = "height" )]
    //    public int? Height { get; set; }
    //    [JsonProperty( PropertyName = "width" )]
    //    public int? Width { get; set; }
    //}

    //internal class premiumEmoticons {
    //    [JsonProperty( PropertyName = "emoticons" )]
    //    public premiumEmoticonImage[] EmoticonsArray { get; set; }
    //}

    internal class images
    {
        [JsonProperty(PropertyName = "image")]
        public string image { get; set; }
    }

    internal class cbadges
    {
        [JsonProperty(PropertyName = "global_mod")]
        public images globalModerator { get; set; }

        [JsonProperty(PropertyName = "admin")]
        public images admin { get; set; }

        [JsonProperty(PropertyName = "broadcaster")]
        public images broadcaster { get; set; }

        [JsonProperty(PropertyName = "mod")]
        public images moderator { get; set; }

        [JsonProperty(PropertyName = "staff")]
        public images staff { get; set; }

        [JsonProperty(PropertyName = "turbo")]
        public images turbo { get; set; }

        [JsonProperty(PropertyName = "subscriber")]
        public images subscriber { get; set; }
    }

    internal class user
    {
        [JsonProperty(PropertyName = "_id")]
        public int Id;

        [JsonProperty(PropertyName = "name")]
        public string Name;

        [JsonProperty(PropertyName = "display_name")]
        public string DisplayName;
    }

    internal class follower
    {
        [JsonProperty("created_at")]
        public DateTime CreatedAt;

        [JsonProperty("user")]
        public user User;
    }

    internal class followers
    {
        [JsonProperty("follows")]
        public follower[] follow;

        [JsonProperty("_total")]
        public int Total;
    }

    private HashSet<string> _preventRefollow = new();
    private cbadges badges;

    private void updateBadges(string userName)
    {
        if (badges == null)
        {
            try
            {
                var wc = new WebClient();
                var s = wc.DownloadString("https://api.twitch.tv/kraken/chat/" + userName + "/badges");
                badges = JsonConvert.DeserializeObject<cbadges>(s);
            }
            catch
            {
                badges = null;
            }
        }
    }

    #endregion

    private readonly Timer _timer;

    private readonly Timer _pingTimer;

    //  TwitchFollowersController _controller;
    private string _id;
    private readonly string _apiKey = "d45e19x896zb5hviy43ify8p6gmp4ei";
    private string _streamerNick;
    private IrcClient _ircClient;
    private IrcRegistrationInfo _regInfo;

    private readonly Regex _parsingRegex =
        new(@"^(:(?<prefix>\S+) )?(?<command>\S+)( (?!:)(?<params>.+?))?( :(?<trail>.+))?$",
            RegexOptions.Compiled | RegexOptions.ExplicitCapture);

    private DateTime? _pingSended = null;

    private readonly Regex _linkRegex =
        new(@"(http|ftp|https)://([\w+?\.\w+])+([a-zA-Z0-9\~\!\@\#\$\%\^\&\*\(\)_\-\=\+\\\/\?\.\:\;\'\,]*)?",
            RegexOptions.IgnoreCase);

    private const string _smilePlaceHolder = "☻♥%SMILE_{0}%♥☻";

    protected override void disposeManaged()
    {
        _timer?.Dispose();
        _ircClient?.Dispose();
    }

    public Twitch_ChatSource(Dispatcher dispatcher)
        : base(dispatcher)
    {
        _timer = new Timer(500);
        _timer.Elapsed += _timer_Elapsed;

        _pingTimer = new Timer(5000);
        _pingTimer.Elapsed += _pingTimer_Elapsed;
    }


    public override void ReloadChatCommand()
    {
        //MessageError("Chat reloading...");
        StartReconnect();
    }

    private bool ParseIrcMessageWithRegex(string message, out string prefix, out string command,
        out string[] parameters)
    {
        //:justinfan84912118.tmi.twitch.tv 366 justinfan84912118 #pokerstaples :End of /NAMES list
        //:tmi.twitch.tv CAP *ACK :twitch.tv/commands
        //@color=#8A2BE2;display-name=Kamiiiiiiiii;emotes=;subscriber=0;turbo=0;user-type= :kamiiiiiiiii!kamiiiiiiiii@kamiiiiiiiii.tmi.twitch.tv PRIVMSG #pokerstaples :^ lol
        string trailing = null;
        prefix = command = string.Empty;
        parameters = new string[] { };

        var messageMatch = _parsingRegex.Match(message);

        if (messageMatch.Success)
        {
            prefix = messageMatch.Groups["prefix"].Value;
            command = messageMatch.Groups["command"].Value;
            parameters = messageMatch.Groups["params"].Value.Split(' ');
            trailing = messageMatch.Groups["trail"].Value;

            if (!string.IsNullOrEmpty(trailing))
                parameters = parameters.Concat(new string[] { trailing }).ToArray();
            return true;
        }

        return false;
    }

    private string getUserName(string prefix)
    {
        var nm = prefix.Split('!');
        if (nm.Length == 0 || nm[0].Length < 2)
            return "";
        return
            nm[0].Substring(0, 1).ToUpper() +
            nm[0].Substring(1);
    }


    public override string Id => "twitchtv";

    private void _timer_Elapsed(object sender, ElapsedEventArgs e)
    {
        _timer.Stop();
        Reconnect();
    }

    private void StartReconnect()
    {
        try
        {
            Status = false;
            _timer.Stop();
            Unsub();
            _timer.Start();
        }
        catch
        {
        }
    }

    private void Unsub()
    {
        if (_ircClient != null)
        {
            _ircClient.Connected -= IrcClient_Connected;
            _ircClient.ProtocolError -= IrcClient_ProtocolError;
            _ircClient.Error -= IrcClient_Error;
            _ircClient.Disconnected -= IrcClient_Disconnected;
            _ircClient.RawMessageReceived -= IrcClient_RawMessageReceived;
            _ircClient.ConnectFailed -= IrcClient_ConnectFailed;
            _ircClient.MotdReceived -= IrcClient_MotdReceived;
            _ircClient.PongReceived -= IrcClient_PongReceived;
            _ircClient.PingReceived -= IrcClient_PingReceived;
        }
    }

    private void MessageError(string Error, string userName = "*SYSTEM*")
    {
        //List<ChatMessage> msgs = new List<ChatMessage>(); 
        //msgs.Add(new ChatMessage() {
        //    Date = DateTime.Now,
        //    Name = userName,
        //    Text = Error,
        //    Source = this,
        //    //Form = 0,
        //    ToMe = true,
        //    Id = _id
        //});

        //newMessagesArrived(msgs);
    }


    private void Reconnect()
    {
        Tooltip = string.Format("twitch: {0}?", _streamerNick);
        Header = "Checking...";

        if (_ircClient != null)
        {
            _ircClient.Disconnect();
            _ircClient = null;
        }

        _ircClient = new IrcClient();
        //IrcClient. = "TWITCHCLIENT 3";
        _ircClient.PongReceived += IrcClient_PongReceived;
        _ircClient.PingReceived += IrcClient_PingReceived;
        _ircClient.Connected += IrcClient_Connected;
        _ircClient.ProtocolError += IrcClient_ProtocolError;
        _ircClient.Error += IrcClient_Error;
        _ircClient.Disconnected += IrcClient_Disconnected;
        _ircClient.RawMessageReceived += IrcClient_RawMessageReceived;
        _ircClient.ConnectFailed += IrcClient_ConnectFailed;
        _ircClient.MotdReceived += IrcClient_MotdReceived;

        //IrcClient.

        var rnd = new Random();
        var s = "justinfan" + rnd.Next(100000000);
        _regInfo = new IrcUserRegistrationInfo()
        {
            NickName = s,
            UserName = s,
            Password = "blah",
            RealName = s,
        };

        var dat = Settings.Default.url_Twitch.Split(':');
        try
        {
            var port = int.Parse(dat[1]);
            _ircClient.Connect(dat[0], port, false, _regInfo);
        }
        catch (Exception e)
        {
            StartReconnect();
            MessageError("Connection error: " + e.Message);
        }
    }

    private int _pongCounter = 0;

    private void _pingTimer_Elapsed(object sender, ElapsedEventArgs e)
    {
        if (_pingSended.HasValue)
        {
            if ((DateTime.Now - _pingSended.Value).TotalSeconds > 30)
            {
                StartReconnect();
                _pingTimer.Stop();
            }
        }
        else
        {
            _pingSended = DateTime.Now;
            _ircClient?.Ping();

            if (_pongCounter++ > 10)
            {
                _pongCounter = 0;
                _ircClient.SendRawMessage("PONG tmi.twitch.tv\r\n");
            }
        }
    }

    private void IrcClient_PingReceived(object sender, IrcPingOrPongReceivedEventArgs e)
    {
        //   _pingSended = null;
    }

    private void IrcClient_PongReceived(object sender, IrcPingOrPongReceivedEventArgs e)
    {
        _pingSended = null;
    }

    private void IrcClient_MotdReceived(object sender, EventArgs e)
    {
        _ircClient.SendRawMessage("CAP REQ :twitch.tv/commands");
        _ircClient.SendRawMessage("CAP REQ :twitch.tv/tags");


        // :tmi.twitch.tv USERSTATE #channel
        Tooltip = string.Format("twitch: {0}", _streamerNick);
    }

    private void IrcClient_Error(object sender, IrcErrorEventArgs e)
    {
        Tooltip = string.Format("twitch: error");
        Header = "Error";
        MessageError("Network Irc error: " + e.Error.Message);
        StartReconnect();
    }

    private void IrcClient_ProtocolError(object sender, IrcProtocolErrorEventArgs e)
    {
        Tooltip = string.Format("twitch: error");
        Header = "Proto Error";
        MessageError("IRC error: " + e.Message);
        StartReconnect();
    }

    private void IrcClient_ConnectFailed(object sender, IrcErrorEventArgs e)
    {
        Tooltip = string.Format("twitch: invalid login");
        Header = "Invalid name";
        MessageError("Invalid login: " + e.Error.Message);
        StartReconnect();
    }

    public static string ToDividedString<T>(IEnumerable<T> This, string Divider = ",",
        bool NeedStarterDivider = false)
    {
        if (This == null)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var item in This)
        {
            if (NeedStarterDivider)
            {
                sb.AppendFormat("{0}{1}", Divider, item);
            }
            else
            {
                sb.Append(item);
            }

            NeedStarterDivider = true;
        }

        return sb.ToString();
    }

    /*
:jtv!jtv@jtv.tmi.twitch.tv PRIVMSG #absnerdity :SPECIALUSER berndlauert8 subscriber
:jtv!jtv@jtv.tmi.twitch.tv PRIVMSG #absnerdity :SPECIALUSER berndlauert8 subscriber
:jtv!jtv@jtv.tmi.twitch.tv PRIVMSG #absnerdity :USERCOLOR berndlauert8 #0000FF
:jtv!jtv@jtv.tmi.twitch.tv PRIVMSG #absnerdity :USERCOLOR berndlauert8 #0000FF
:jtv!jtv@jtv.tmi.twitch.tv PRIVMSG #absnerdity :EMOTESET berndlauert8 [8742]
:jtv!jtv@jtv.tmi.twitch.tv PRIVMSG #absnerdity :EMOTESET berndlauert8 [8742]
*/
    private Dictionary<string, HashSet<int>> _emotesets = new();
    private Dictionary<string, List<Uri>> _bages = new();

    private List<Uri> getBages(string nickname)
    {
        if (_bages.TryGetValue(nickname, out var u))
            return u;
        return new List<Uri>();
    }

    private void addBage(string nickname, string id)
    {
        if (badges != null)
        {
            List<Uri> u;
            if (!_bages.TryGetValue(nickname, out u))
                _bages[nickname] = u = new List<Uri>();

            Uri x;
            switch (id)
            {
                case "subscriber":
                    x = new Uri(badges.subscriber.image);
                    if (!u.Contains(x))
                        u.Add(x);
                    break;

                case "turbo":
                    x = new Uri(badges.turbo.image);
                    if (!u.Contains(x))
                        u.Add(x);
                    break;

                case "moderator":
                    x = new Uri(badges.moderator.image);
                    if (!u.Contains(x))
                        u.Add(x);
                    break;

                case "broadcaster":
                    x = new Uri(badges.broadcaster.image);
                    if (!u.Contains(x))
                        u.Add(x);
                    break;
            }
        }
    }

    private void addUserEmoteset(string nick, string set)
    {
        if (!_emotesets.TryGetValue(nick, out var x))
            _emotesets[nick] = x = new HashSet<int>();

        var xx = set.Substring(1, set.Length - 2).Split(',');
        for (var j = 0; j < xx.Length; ++j)
            x.Add(int.Parse(xx[j]));
    }

    private class Emo
    {
        public int start;
        public int end;
        public string id;
    }

    private string doAttachEmotes(string text, string sets)
    {
        //34:9-17,28-36,47-55/22639:0-7,19-26,38-45
        //" + uu.ToString() + "[/sml]
        const string emoteUrl = "[sml]http://static-cdn.jtvnw.net/emoticons/v1/{0}/1.0[/sml]";

        //      Console.WriteLine( "1=>{0}", text );

        var emos = new List<Emo>();

        foreach (var emote in sets.Split('/'))
        {
            var d = emote.Split(':');

            foreach (var r in d[1].Split(','))
            {
                var se = r.Split('-');

                var s = int.Parse(se[0]);
                var e = int.Parse(se[1]);

                emos.Add(new Emo() { id = d[0], start = s, end = e });

                //  Console.WriteLine( "{0} => emote: {1}", text.Substring( s, e - s + 1 ), d[0] );
            }
        }

        foreach (var e in from b in emos
                 orderby b.start descending
                 select b)
        {
            text =
                text.Substring(0, e.start) +
                string.Format(emoteUrl, e.id) +
                text.Substring(e.end + 1);
        }


        //Console.WriteLine( "2=>{0}", text );
        return text;
    }

    //   string dots = ".";
    private void IrcClient_RawMessageReceived(object sender, IrcRawMessageEventArgs e)
    {
        string prefix, command;
        var ptags = new Dictionary<string, string>();
        string[] parameters;

        //Console.WriteLine( e.RawContent );


        try
        {
            if (e.RawContent.StartsWith("@"))
            {
                // TAGged command
                var n = e.RawContent.IndexOf(' ');
                if (n > 0)
                {
                    var tags = e.RawContent.Substring(1, n - 1).Split(';');
                    foreach (var tag in tags)
                    {
                        var data = tag.Split('=');
                        ptags[data[0]] = data[1];
                    }

                    var txt = e.RawContent.Substring(n + 1);
                    ParseIrcMessageWithRegex(txt, out prefix, out command, out parameters);

                    //@color=#00B4CC;display-name=ilittle17;emotes=501:9-10;subscriber=1;turbo=1;user-type=mod
                    //@color=#0000FF;display-name=WinBotCity;emotes=;subscriber=1;turbo=0;user-type=
                    //@color=#C2CC00;display-name=Event0011;emotes=;subscriber=1;turbo=1;user-type= 
                }
                else
                {
                    return;
                }
            }
            else
            {
                // typical IRCv2 command
                ParseIrcMessageWithRegex(e.RawContent, out prefix, out command, out parameters);
            }


            var msgs = new List<ChatMessage>();
            //List<ChatCommand> cmds = new List<ChatCommand>();
            var username = getUserName(prefix);
            switch (command)
            {
                /* case "001":
                     this.Status = true;
                     this.Tooltip = string.Format( "twitch channel {0} not found!", StreamerNick );
                     Destroy();
                     break;*/

                case "PART":
                case "JOIN":
                    //Header = "http://twitch.tv, " + StreamerNick + dots;
                    Status = true;
                    Tooltip = string.Format("twitch: {0}", _streamerNick);
                    Header = "Joined";
                    break;

                case "MODE":
                    //if (parameters[1] == "+o") {
                    //    addBage( parameters[2].ToLower(), "moderator" );
                    //}
                    break;

                case "CLEARCHAT":
                    fireOnRemoveUserMessages(parameters[1]);
                    break;

                case "PRIVMSG":
                    //Header = "http://twitch.tv, " + StreamerNick;
                    if (username != "Jtv")
                    {
                        var text = parameters[1];

                        var emotes = ptags.SafeGet("emotes", "");
                        if (!string.IsNullOrEmpty(emotes))
                        {
                            // FUCK emotes
                            text = doAttachEmotes(text, emotes);
                        }
                        else
                        {
                            text = Regex.Replace(text,
                                @"((http|ftp|https):\/\/[\w\-_]+(\.[\w\-_]+)+([\w\-\.,@?^=%&amp;:/~\+#]*[\w\-\@?^=%&amp;/~\+#])?)",
                                "[url]$1[/url]");
                        }

                        // actualize badges!
                        //addBage
                        //@color=#00B4CC;display-name=ilittle17;emotes=501:9-10;subscriber=1;turbo=1;user-type=mod
                        if (Settings.Default.twitch_ShowSubsIcon)
                            if (ptags.SafeGet("subscriber", "0") == "1")
                                addBage(username.ToLower(), "subscriber");

                        if (ptags.SafeGet("turbo", "0") == "1")
                            addBage(username.ToLower(), "turbo");

                        if (ptags.SafeGet("user-type", "0") == "mod")
                            addBage(username.ToLower(), "moderator");

                        //parameters[1] = "Скажите позязя название сией композицииKappa";
                        if (string.Compare(username, _streamerNick, true) == 0)
                            addBage(username.ToLower(), "broadcaster");

                        var nick = ptags.SafeGet("display-name", username);

                        if (string.IsNullOrEmpty(nick))
                            nick = username;

                        var msg = new ChatMessage()
                        {
                            Date = DateTime.Now,
                            Name = nick,
                            Text = Clean(text), //ReplaceSmiles( username.ToLower(), parameters[1] ),
                            Source = this,
                            //Form = 0,
                            ToMe = ContainKeywords(parameters[1]),
                            Id = _id,
                        };

                        if (Settings.Default.twitch_AllowUseColors)
                            msg.Color = ptags.SafeGet("color", "");
                        else
                            msg.Color = "";

                        msg.AddBadges(getBages(username.ToLower()).ToArray());

                        msgs.Add(msg);
                    }
                    else
                    {
                    }

                    break;

                default:
                    break;
            }

            if (msgs.Count > 0)
                newMessagesArrived(msgs);
            //if ( cmds.Count > 0 )
            //    newCommandsArrived( cmds );
        }
        catch (Exception ee)
        {
            App.Log('!', "Twitch error: {0}", ee);
            Header = "XError";
            MessageError("Oxlamon error: " + ee.Message);
            StartReconnect();
        }
    }

    private string Clean(string text)
    {
        var t = JsonConvert.ToString(text);
        return t.Substring(1, t.Length - 2);
    }

    private void IrcClient_Disconnected(object sender, EventArgs e)
    {
        Tooltip = string.Format("twitch: ?");
        Header = "Lost";

        MessageError("Disconnected...");
        StartReconnect();
    }

    private void IrcClient_Connected(object sender, EventArgs e)
    {
        _pingTimer.Start();
        _ircClient.Channels.Join("#" + _streamerNick.ToLowerInvariant());
        Header = "Connected";
    }

    public override void Create(string streamerUri, string id)
    {
        Uri = _streamerNick = SetKeywords(streamerUri);
        Label = _id = id;

        //   UpdateSmiles();
        updateBadges(_streamerNick);
        Reconnect();
    }

    public override void Destroy()
    {
        // this._controller.Stop();
        Unsub();
        _timer.Stop();
        if (_ircClient != null)
            _ircClient.Disconnect();
        _ircClient = null;
    }

    private string _streamerID = null;
    private bool _allowUpdateCount = true;

    public override void UpdateViewerCount()
    {
        Task.Run(UpdateViewerCountAsync);
    }

    private async Task UpdateViewerCountAsync()
    {
        if (_allowUpdateCount)
        {
            _allowUpdateCount = false;
            App.Log(' ', "Twitch Find ID: {0}", _streamerNick);
            try
            {
                var data = await WoWnikTwitchClient.GetInfoAsync(_streamerNick).ConfigureAwait(false);

                _streamerID = $"{data.users.data[0].id}";
                try
                {
                    ViewersCount = data.streams.data[0].viewer_count;
                }
                catch
                {
                    ViewersCount = 0;
                }

                Header = $"{_streamerNick}: {ViewersCount}";
            }
            catch (Exception e)
            {
                Header = $"{_streamerNick}:err";
                App.Log('!', "Twitch Find ID error: {0}", e);
            }
            finally
            {
                _allowUpdateCount = true;
            }
        }
    }
}