/*
* Vha.Net
* Copyright (C) 2005-2010 Remco van Oosterhout
* See Credits.txt for all aknowledgements.
*
* This program is free software; you can redistribute it and/or modify
* it under the terms of the GNU General Public License as published by
* the Free Software Foundation; version 2 of the License only.
*
* This program is distributed in the hope that it will be useful,
* but WITHOUT ANY WARRANTY; without even the implied warranty of
* MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
* GNU General Public License for more details.
*
* You should have received a copy of the GNU General Public License
* along with this program; if not, write to the Free Software
* Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307
* USA
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
using System.Timers;
using Vha.Common;
using Vha.Net.Packets;
using Vha.Net.Events;

namespace Vha.Net
{
    public class Chat
	{
		#region Public settings
		public bool AutoReconnect = true;
        public int ReconnectDelay = 5000;
        public int PingInterval = 60000;
        public int PingTimeout = 120000;
        public double FastPacketDelay = 10;
        public double SlowPacketDelay = 2200;
        public bool IgnoreCharacterLoggedIn = true;
        public bool UseThreadPool = true;
        /// <summary>
        /// Default timeout of for GetCharacterID()
        /// </summary>
        public int LookupTimeout = 2500;
		public object Tag = null;
		#endregion
		#region Events
		public event AmdMuxInfoEventHandler AmdMuxInfoEvent;
        public event AnonVicinityEventHandler AnonVicinityEvent;
        /// <summary>
        /// Notices about friends being online or offline
        /// </summary>
		public event FriendStatusEventHandler FriendStatusEvent;
        public event FriendRemovedEventHandler FriendRemovedEvent;
        public event ClientUnknownEvent ClientUnknownEvent;
		/// <summary>
		/// Triggered when receiving an invite to a private channel
		/// </summary>
        public event PrivateChannelRequestEventHandler PrivateChannelRequestEvent;
        public event NameLookupEventHandler NameLookupEvent;
        public event ForwardEventHandler ForwardEvent;
        public event ChannelStatusEventHandler ChannelStatusEvent;
        public event ChannelMessageEventHandler ChannelMessageEvent;
        public event SystemMessageEventHandler SystemMessageEvent;
        public event SimpleMessageEventHandler SimpleMessageEvent;
        public event LoginOKEventHandler LoginOKEvent;
        public event LoginErrorEventHandler LoginErrorEvent;
        public event UnknownPacketEventHandler UnknownPacketEvent;
        public event PrivateChannelStatusEventHandler PrivateChannelStatusEvent;
        public event PrivateChannelMessageEventHandler PrivateChannelMessageEvent;
        public event PrivateMessageEventHandler PrivateMessageEvent;
        public event VicinityMessageEventHandler VicinityMessageEvent;
        public event LoginSeedEventHandler LoginSeedEvent;
        public event LoginCharlistEventHandler LoginCharlistEvent;
        public event StateChangeEventHandler StateChangeEvent;
        public event DebugEventHandler DebugEvent;
#if !DEBUG
        public event ExceptionEventHandler ExceptionEvent;
#endif
		#endregion

		#region Protected members
		protected string _account = string.Empty;
        protected string _password = string.Empty;
        protected string _character = string.Empty;
        protected ChatState _state = ChatState.Disconnected;
        protected UInt32 _id = 0;
        protected BigInteger _organizationid = 0;
        protected string _organization = string.Empty;
        protected List<Thread> _threads;
        protected Thread _receiveThread;
        protected Thread _sendThread;
		/// <summary>
		/// This event is signaled whenever something is added to the send queue.
		/// </summary>
		protected ManualResetEvent _sendThread_ResetEvent;
        protected Socket _socket;
        protected Dictionary<UInt32, String> _characters;
        protected Dictionary<String, UInt32> _charactersByName;
        protected Dictionary<BigInteger, Channel> _channels;
        protected string _serverAddress;
        protected int _port;
        protected PacketQueue _fastQueue;
        protected PacketQueue _slowQueue;
        protected bool _closing = false;
        protected System.Timers.Timer _reconnectTimer;
        protected ManualResetEvent _lookupReset;
        protected System.Timers.Timer _pingTimer;
        protected DateTime _lastPong = DateTime.Now;

        /// <summary>
        /// Proxy server. new Uri("http://proxyserver:port/") for a HTTP proxy supporting Connect().
        /// new Uri("socks4://userid@proxyserver:port/") for a Socks v4 connection.
        /// </summary>
        protected Uri _proxy = null;
		#endregion

		#region Public attributes
		/// <summary>
		/// My character ID
		/// </summary>
        public UInt32 ID { get { return this._id; } }
        /// <summary>
        /// My account name
        /// </summary>
		public string Account { get { return this._account; } }
		/// <summary>
		/// My character name
		/// </summary>
        public string Character { get { return this._character; } }
		/// <summary>
		/// Address of server I am connected to
		/// </summary>
        public string Server { get { return this._serverAddress; } }
		/// <summary>
		/// Port on server I am connected to
		/// </summary>
        public int Port { get { return this._port; } }
        /// <summary>
        /// The proxy this connection is tunneled through.
        /// Returns null when no proxy is used.
        /// </summary>
        public Uri Proxy { get { return this._proxy; } }
		/// <summary>
		/// Name of organization I am a member of
		/// </summary>
        public string Organization { get { return this._organization; } }
		/// <summary>
		/// ID of organization I am a member of
		/// </summary>
        public BigInteger OrganizationID { get { return this._organizationid; } }
        /// <summary>
        /// The current connection state
        /// </summary>
        public ChatState State { get { return this._state; } }
        /// <summary>
        /// Number of entries in the slow outgoing queue
        /// </summary>
		public int SlowQueueCount { get { return this._slowQueue.Count; } }
		/// <summary>
		/// Number of entries in the fast outgoing queue
		/// </summary>
        public int FastQueueCount { get { return this._fastQueue.Count; } }
		#endregion

		#region Constructors
		/// <summary>
		/// </summary>
		/// <param name="ConnectionString">ao://user:pass@serverhost:port/Charactername</param>
        public Chat(Uri connectionString)
        {
            UriBuilder ub = new UriBuilder(connectionString);
            this._serverAddress = ub.Host;
            this._port = ub.Port;
            this._account = ub.UserName;
            this._password = ub.Password;
            if (!string.IsNullOrEmpty(ub.Path))
                this._character = Format.UppercaseFirst(ub.Path.Substring(1)); //Start at 1 to remove the initial /
            this._proxy = null;
        }

		/// <summary>
		/// </summary>
		/// <param name="ConnectionString">ao://user:pass@serverhost:port/Charactername</param>
		/// <param name="proxy">The proxy server this connection should be tunnelled through</param>
        public Chat(Uri connectionString, Uri proxy)
            : this(connectionString)
        {
            this._proxy = proxy;
        }

		public Chat(string server, int port, string account, string password)
        {
            this._serverAddress = server;
            this._port = port;
            this._account = account;
            this._password = password;
            this._proxy = null;
        }

        /// <summary>
        /// Initializes a new Chat instance
        /// </summary>
        /// <param name="server"></param>
        /// <param name="port"></param>
        /// <param name="account"></param>
        /// <param name="password"></param>
        /// <param name="proxy">The proxy server this connection should be tunnelled through</param>
        public Chat(string server, int port, string account, string password, Uri proxy)
        {
            if (proxy == null)
                throw new ArgumentNullException();
            this._serverAddress = server;
            this._port = port;
            this._account = account;
            this._password = password;
            this._proxy = proxy;
        }

        public Chat(string server, int port, string account, string password, string character)
        {
            this._serverAddress = server;
            this._port = port;
            this._account = account;
            this._password = password;
            this._character = Format.UppercaseFirst(character);
            this._proxy = null;
        }

        /// <summary>
        /// Initializes a new Chat instance
        /// </summary>
        /// <param name="server"></param>
        /// <param name="port"></param>
        /// <param name="account"></param>
        /// <param name="password"></param>
        /// <param name="character"></param>
        /// <param name="proxy">The proxy server this connection should be tunnelled through</param>
        public Chat(string server, int port, string account, string password, string character, Uri proxy)
        {
            if (proxy == null)
                throw new ArgumentNullException();
            this._serverAddress = server;
            this._port = port;
            this._account = account;
            this._password = password;
            this._character = Format.UppercaseFirst(character);
            this._proxy = proxy;
		}
		#endregion

		// Get this thing ready for running
        protected void PrepareChat()
        {
            lock (this)
            {
                if (this._receiveThread != null)
                {
                    if (this._receiveThread.ThreadState == System.Threading.ThreadState.Running)
                    {
                        this._receiveThread.Abort();
                        this._receiveThread.Join(500);
                    }
                }
                if (this._sendThread != null)
                {
                    if (this._sendThread.ThreadState == System.Threading.ThreadState.Running)
                    {
                        this._sendThread.Abort();
                        this._sendThread.Join(500);
                    }
                }
                if (this._socket != null && this._socket.Connected)
                {
                    this._socket.Close();
                }
                this._threads = new List<Thread>();
                this._lookupReset = new ManualResetEvent(false);
                this._receiveThread = new Thread(new ThreadStart(this.RunReceiver));
                this._receiveThread.IsBackground = true;
				this._sendThread_ResetEvent = new ManualResetEvent(true); //Resetevent for sendthread.
                this._sendThread = new Thread(new ThreadStart(this.RunSender));
                this._sendThread.IsBackground = true;
                this._characters = new Dictionary<UInt32, String>();
                this._charactersByName = new Dictionary<string, uint>();
                this._channels = new Dictionary<BigInteger, Channel>();
                this._fastQueue = new PacketQueue();
                this._fastQueue.delay = this.FastPacketDelay;
                this._slowQueue = new PacketQueue();
                this._slowQueue.delay = this.SlowPacketDelay;
                this._reconnectTimer = new System.Timers.Timer();
                this._reconnectTimer.AutoReset = false;
                this._reconnectTimer.Interval = this.ReconnectDelay;
                this._reconnectTimer.Elapsed += new ElapsedEventHandler(OnReconnectEvent);
                this._pingTimer = new System.Timers.Timer();
                this._pingTimer.AutoReset = true;
                this._pingTimer.Interval = this.PingInterval;
                this._pingTimer.Elapsed += new ElapsedEventHandler(OnPingTimerEvent);
                this._lastPong = DateTime.Now;
            }
        }

        #region Connect
        /// <summary>
        /// Connect to chatserver using previously provided parameters
        /// </summary>
        /// <param name="async">Whether to use a blocking call to connect</param>
        /// <returns></returns>
        public bool Connect(bool async)
        {
            if (async)
            {
                Thread thread = new Thread(new ThreadStart(_connect));
                thread.Start();
                return false;
            }
            else return Connect();
        }
        internal void _connect() { Connect(); }

		/// <summary>
		/// Connect to chatserver using previously provided parameters
		/// </summary>
		/// <returns></returns>
        public bool Connect()
        {
            lock (this)
            {
                if (this._socket != null && this._socket.Connected)
                {
                    this.Debug("Already Connected", "[Error]");
                    return false;
                }
                this.OnStateChangeEvent(new StateChangeEventArgs(ChatState.Connecting));
                this._closing = false;
                this.PrepareChat();

                this.Debug("Connecting to dimension: " + this._serverAddress + ":" + this._port, "[Auth]");

                bool connected = false; // Set this to true when a connection is successfull.
                if (this._proxy != null)
                {
                    // Try connecting through a proxy
                    Proxy np = null;
                    try
                    {
                        np = new Proxy(this._proxy, this._serverAddress, this._port);
                        if (np.Socket != null)
                        {
                            if (np.Socket.Connected)
                            {
                                this._socket = np.Socket;
                                connected = true;
                                this.Debug("Connected to " + this._serverAddress.ToString() + ":" + this._port + " through " + np.ToString(), "[Socket]");
                            }
                        }
                        else
                        {
                            throw new Exception();
                        }
                    }
                    catch
                    {
                        if (np == null)
                            this.Debug("Failed construct proxy connection", "[Socket]");
                        else if (np.Socket == null)
                            this.Debug("Failed connecting to proxy server " + np.ToString(), "[Socket]");
                        else
                            this.Debug("Failed connecting to " + this._serverAddress + ":" + this._port.ToString() + " through " + np.ToString(), "[Socket]");
                    }
                }
                else
                {
                    // Try connecting without a proxy
                    try
                    {
                        IPHostEntry host = Dns.GetHostEntry(this._serverAddress);
                        foreach (IPAddress addy in host.AddressList)
                        {
                            IPEndPoint ipe = new IPEndPoint(addy, this._port);
                            Socket tempSocket = new Socket(ipe.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                            tempSocket.Connect(ipe);

                            if (tempSocket.Connected)
                            {
                                connected = true;
                                this._socket = tempSocket;
                                this.Debug("Connected to " + ipe.ToString(), "[Socket]");
                            }
                            else
                                this.Debug("Failed connecting to " + ipe.ToString(), "[Socket]");
                        }
                    }
                    catch
                    {
                        this.Debug("Unknown error during connecting", "[Error]");
                    }
                }
                if (connected)
                {
                    this._receiveThread.Start();
                    this._sendThread.Start();
                    return true;
                }
            }
            this.OnStateChangeEvent(new StateChangeEventArgs(ChatState.Disconnected));
            return false;
        }
        #endregion

        /// <summary>
		/// Remove all subscriptions from all of my events
		/// </summary>
        public void ClearEvents()
        {
            this.AmdMuxInfoEvent = null;
            this.AnonVicinityEvent = null;
            this.FriendStatusEvent = null;
            this.FriendRemovedEvent = null;
            this.ClientUnknownEvent = null;
            this.PrivateChannelRequestEvent = null;
            this.NameLookupEvent = null;
            this.ForwardEvent = null;
            this.ChannelStatusEvent = null;
            this.ChannelMessageEvent = null;
            this.SystemMessageEvent = null;
            this.SimpleMessageEvent = null;
            this.LoginOKEvent = null;
            this.LoginErrorEvent = null;
            this.UnknownPacketEvent = null;
            this.PrivateChannelStatusEvent = null;
            this.PrivateChannelMessageEvent = null;
            this.PrivateMessageEvent = null;
            this.VicinityMessageEvent = null;
            this.LoginSeedEvent = null;
            this.LoginCharlistEvent = null;
            this.StateChangeEvent = null;
            this.DebugEvent = null;
#if !DEBUG
            this.ExceptionEvent = null;
#endif
		}

		#region Disconnect
		/// <summary>
		/// Disconnect from chat server synchronously.
		/// </summary>
        public void Disconnect()
        {
            if (this._socket != null && this._socket.Connected)
            {
                this._socket.Close();
            }
            if (this._receiveThread != null)
            {
                // This lock ensures the receive thread is not in a state unsafe for aborting
                lock (this._receiveThread)
                {
                    this._receiveThread.Abort();
                    if (this._receiveThread.IsAlive)
                        this._receiveThread.Join();
                    this._receiveThread = null;
                }
            }
            if (this._sendThread != null)
            {
                this._sendThread.Abort();
                if (this._sendThread.IsAlive)
                    this._sendThread.Join();
                this._sendThread = null;
            }
            this._socket = null;
            this._lookupReset = null;
            if (this._characters != null) this._characters.Clear();
            this._characters = null;
            this._charactersByName = null;
            if (this._channels != null) this._channels.Clear();
            this._channels = null;
            this._fastQueue = null;
            this._slowQueue = null;
            this._reconnectTimer = null;
            this._pingTimer = null;

            this.OnStateChangeEvent(new StateChangeEventArgs(ChatState.Disconnected));
            this._state = ChatState.Disconnected;

            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

		/// <summary>
		/// Disconnect from chatserver.
		/// </summary>
		/// <param name="async">weither or not to disconnect in async mode</param>
        public void Disconnect(bool async)
        {
            // Prepare the disconnect
            this._closing = true;
            if (this._reconnectTimer != null) { this._reconnectTimer.Stop(); }
            if (this._pingTimer != null) { this._pingTimer.Stop(); }
            // Close it up
            if (async)
            {
                Thread thread = new Thread(new ThreadStart(Disconnect));
                thread.Start();
            }
            else Disconnect();
        }
		#endregion

		// Receive Thread
        internal void RunReceiver()
        {
            this.Debug("Started", "[ReceiveThread]");
            try
            {
                while (true)
                {
                    if (this._closing) break;
                    if (!_socket.Connected)
                    {
                        break;
                    }
                    byte[] buffer = new byte[4];
                    int receivedBytes = 0;
                    try
                    {
                        receivedBytes = this._socket.Receive(buffer, buffer.Length, 0);
                    }
                    catch (ObjectDisposedException) { throw new SocketException(); }
                    if (receivedBytes == 0 || !this._socket.Connected)
                    {
                        break;
                    }
                    Packet.Type type = (Packet.Type)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, 0));
                    short length = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, 2));
                    if (length == 0)
                    {
                        ParsePacketData packetData = new ParsePacketData(type, length, null);
                        if (this.UseThreadPool)
                            ThreadPool.QueueUserWorkItem(new WaitCallback(this.ParsePacket), packetData);
                        else
                            this.ParsePacket(packetData, true);
                    }
                    else
                    {
                        buffer = new byte[length];
                        int bytesLeft = length;
                        while (bytesLeft > 0)
                        {
                            try
                            {
                                receivedBytes = this._socket.Receive(buffer, length - bytesLeft, bytesLeft, 0);
                            }
                            catch (ObjectDisposedException) { throw new SocketException(); }
                            bytesLeft -= receivedBytes;
                            Thread.Sleep(10);
                        }
                        ParsePacketData packetData = new ParsePacketData(type, length, buffer);
                        if (this.UseThreadPool == false || packetData.type == Packet.Type.NAME_LOOKUP)
                            this.ParsePacket(packetData, true);
                        else
                            ThreadPool.QueueUserWorkItem(new WaitCallback(this.ParsePacket), packetData);
                    }
                    Thread.Sleep(0);
                }
            }
            catch (ThreadAbortException)
            {
                this.Debug("Thread aborted", "[ReceiveThread]");
            }
            catch (SocketException ex)
            {
                this.Debug("Network error: " + ex.ToString(), "[ReceiveThread]");
            }
            finally
            {
                // Wait for our child threads to finish
                while (true)
                {
                    Thread t = null;
                    lock (this._threads)
                    {
                        if (this._threads.Count == 0) break;
                        t = this._threads[0];
                    }
                    t.Abort();
                    if (t.IsAlive) t.Join(new TimeSpan(0, 0, 1));
                    lock (this._threads)
                    {
                        this._threads.Remove(t);
                    }
                }
                // And we're done!
                this.Debug("Stopped!", "[ReceiveThread]");
                this.OnStateChangeEvent(new StateChangeEventArgs(ChatState.Disconnected));
            }
        }
        // Send Thread
        internal void RunSender()
        {
            this.Debug("Started", "[SendThread]");
            try
            {
                while (this._sendThread_ResetEvent.WaitOne())
                {
                    if (this._closing) break;
                    if (this._socket == null || this._socket.Connected == false)
                    {
                        break;
                    }
                    if (this._fastQueue.Available || this._slowQueue.Available)
                    {
                        Packet packet;
                        if (this._slowQueue.Available)
                        {
                            packet = this._slowQueue.Dequeue();
                        }
                        else
                        {
                            packet = this._fastQueue.Dequeue();
                        }
                        byte[] data = packet.GetBytes();
                        short len = (short)data.Length;
                        byte[] buffer = new byte[len + 4];
                        BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)packet.PacketType)).CopyTo(buffer, 0);
                        BitConverter.GetBytes(IPAddress.HostToNetworkOrder(len)).CopyTo(buffer, 2);
                        data.CopyTo(buffer, 4);
                        try
                        {
                            _socket.Send(buffer, buffer.Length, 0);
                        }
                        catch (ObjectDisposedException) { throw new SocketException(); }
                        if (packet.PacketType == Packet.Type.PRIVATE_MESSAGE)
                        {
                            PrivateMessagePacket msg = (PrivateMessagePacket)packet;
                            this.OnPrivateMessageEvent(new PrivateMessageEventArgs(msg.CharacterID, this.GetCharacterName(msg.CharacterID), msg.Message, true));
                        }
						if (this._fastQueue.Count > 0 || this._slowQueue.Count > 0) //If there is still something in queue, sleep for predefined delay..
							Thread.Sleep((int)this.FastPacketDelay);
						else //If there's nothing more in the queues, reset our resetevent.
							this._sendThread_ResetEvent.Reset();
                    }
                    else
                    {
                        Thread.Sleep(100);
                    }
                }
            }
            catch (ThreadAbortException)
            {
                this.Debug("Thread aborted", "[SendThread]");
            }
            catch (SocketException ex)
            {
                this.Debug("Network error: " + ex.ToString(), "[SendThread]");
            }
            finally
            {
                this.Debug("Stopped!", "[SendThread]");
                this.OnStateChangeEvent(new StateChangeEventArgs(ChatState.Disconnected));
            }
        }

        internal void ParsePacket(Object o) { ParsePacket((ParsePacketData)o, false); }
        internal void ParsePacket(ParsePacketData packetData, bool local)
        {
            // Register this thread
            if (local == false)
            {
                // Keep track of this thread to ensure it's not aborted while processing a packet
                lock (this._threads)
                    this._threads.Add(Thread.CurrentThread);
            }
            else
            {
                // Lock the 'thread' to ensure it's not aborted while processing a packet
                Monitor.Enter(this._receiveThread);
            }
            // Handle packet
            try
            {
                Packet packet = null;
                // figure out the packet type and raise an event.
                switch (packetData.type)
                {
                    case Packet.Type.PING:
                        OnPongEvent();
                        break;
                    case Packet.Type.LOGIN_SEED:
                        packet = new LoginSeedPacket(packetData.type, packetData.data);
                        OnLoginSeedEvent(
                            new LoginSeedEventArgs(
                            ((LoginSeedPacket)packet).Seed
                            ));
                        break;
                    case Packet.Type.SYSTEM_MESSAGE:
                        packet = new SimpleStringPacket(packetData.type, packetData.data);
                        OnSimpleMessageEvent(
                            new SimpleMessageEventArgs(
                            ((SimpleStringPacket)packet).Message
                            ));
                        break;
                    case Packet.Type.LOGIN_ERROR:
                        packet = new SimpleStringPacket(packetData.type, packetData.data);
                        OnLoginErrorEvent(
                            new LoginErrorEventArgs(
                            ((SimpleStringPacket)packet).Message
                            ));
                        break;
                    case Packet.Type.LOGIN_CHARACTERLIST:
                        packet = new LoginCharacterListPacket(packetData.type, packetData.data);
                        OnLoginCharacterListEvent(
                            new LoginChararacterListEventArgs(
                            ((LoginCharacterListPacket)packet).Characters
                            ));
                        break;
                    case Packet.Type.FRIEND_REMOVED:
                        packet = new SimpleIdPacket(packetData.type, packetData.data);
                        OnFriendRemovedEvent(
                            new CharacterIDEventArgs(
                            ((SimpleIdPacket)packet).CharacterID,
                            this.GetCharacterName(((SimpleIdPacket)packet).CharacterID)
                            ));
                        break;
                    case Packet.Type.CLIENT_UNKNOWN:
                        packet = new SimpleIdPacket(packetData.type, packetData.data);
                        OnClientUnknownEvent(
                            new CharacterIDEventArgs(
                            ((SimpleIdPacket)packet).CharacterID,
                            this.GetCharacterName(((SimpleIdPacket)packet).CharacterID)
                            ));
                        break;
                    case Packet.Type.PRIVATE_CHANNEL_INVITE:
                        packet = new PrivateChannelStatusPacket(packetData.type, packetData.data);
                        OnPrivateChannelRequestEvent(
                            new PrivateChannelRequestEventArgs(
                            ((PrivateChannelStatusPacket)packet).ChannelID,
                            this.GetCharacterName(((PrivateChannelStatusPacket)packet).ChannelID),
                            false
                            ));
                        break;
                    case Packet.Type.PRIVATE_CHANNEL_KICK:
                    case Packet.Type.PRIVATE_CHANNEL_PART:
                        packet = new PrivateChannelStatusPacket(packetData.type, packetData.data);
                        OnPrivateChannelStatusEvent(
                            new PrivateChannelStatusEventArgs(
                            ((PrivateChannelStatusPacket)packet).ChannelID,
                            this.GetCharacterName(((PrivateChannelStatusPacket)packet).ChannelID),
                            this.ID, this.Character, false, false
                            ));
                        break;
                    case Packet.Type.LOGIN_OK:
                        packet = new EmptyPacket(packetData.type);
                        OnLoginOKEvent();
                        break;
                    case Packet.Type.CLIENT_NAME:
                        packet = new ClientNamePacket(packetData.type, packetData.data);
                        OnNameLookupEvent(
                            new NameLookupEventArgs(
                            ((ClientNamePacket)packet).CharacterID,
                            ((ClientNamePacket)packet).CharacterName
                            ));
                        break;
                    case Packet.Type.NAME_LOOKUP:
                        packet = new NameLookupPacket(packetData.type, packetData.data);
                        OnNameLookupEvent(
                            new NameLookupEventArgs(
                            ((NameLookupPacket)packet).CharacterID,
                            ((NameLookupPacket)packet).CharacterName
                            ));
                        break;
                    case Packet.Type.PRIVATE_MESSAGE:
                        packet = new PrivateMessagePacket(packetData.type, packetData.data);
                        OnPrivateMessageEvent(
                            new PrivateMessageEventArgs(
                            ((PrivateMessagePacket)packet).CharacterID,
                            this.GetCharacterName(((PrivateMessagePacket)packet).CharacterID),
                            ((PrivateMessagePacket)packet).Message,
                            false
                            ));
                        break;
                    case Packet.Type.VICINITY_MESSAGE:
                        packet = new PrivateMessagePacket(packetData.type, packetData.data);
                        OnVicinityMessageEvent(
                            new VicinityMessageEventArgs(
                            ((PrivateMessagePacket)packet).CharacterID,
                            this.GetCharacterName(((PrivateMessagePacket)packet).CharacterID),
                            ((PrivateMessagePacket)packet).Message
                            ));
                        break;
                    case Packet.Type.ANON_MESSAGE:
                        packet = new AnonVicinityPacket(packetData.type, packetData.data);
                        OnAnonVicinityEvent(
                            new AnonVicinityEventArgs(
                            ((AnonVicinityPacket)packet).UnknownString,
                            ((AnonVicinityPacket)packet).Message
                            ));
                        break;
                    case Packet.Type.FRIEND_STATUS:
                        packet = new FriendStatusPacket(packetData.type, packetData.data);
                        OnFriendStatusEvent(
                            new FriendStatusEventArgs(
                            ((FriendStatusPacket)packet).CharacterID,
                            this.GetCharacterName(((FriendStatusPacket)packet).CharacterID),
                            ((FriendStatusPacket)packet).Online,
                            ((FriendStatusPacket)packet).Temporary
                            ));
                        break;
                    case Packet.Type.CHANNEL_STATUS:
                        packet = new ChannelStatusPacket(packetData.type, packetData.data);
                        OnChannelStatusEvent(
                            new ChannelStatusEventArgs(
                            ((ChannelStatusPacket)packet).ID,
                            ((ChannelStatusPacket)packet).Name,
                            ((ChannelStatusPacket)packet).Flags,
                            ((ChannelStatusPacket)packet).Muted,
                            ((ChannelStatusPacket)packet).ChannelType
                            ));
                        break;
                    case Packet.Type.PRIVATE_CHANNEL_CLIENTJOIN:
                    case Packet.Type.PRIVATE_CHANNEL_CLIENTPART:
                        packet = new PrivateChannelStatusPacket(packetData.type, packetData.data);
                        OnPrivateChannelStatusEvent(
                            new PrivateChannelStatusEventArgs(
                            ((PrivateChannelStatusPacket)packet).ChannelID,
                            this.GetCharacterName(((PrivateChannelStatusPacket)packet).ChannelID),
                            ((PrivateChannelStatusPacket)packet).CharacterID,
                            this.GetCharacterName(((PrivateChannelStatusPacket)packet).CharacterID),
                            ((PrivateChannelStatusPacket)packet).Joined,
                            ((PrivateChannelStatusPacket)packet).ChannelID == this._id
                            ));
                        break;
                    case Packet.Type.PRIVGRP_MESSAGE:
                        packet = new PrivateChannelMessagePacket(packetData.type, packetData.data);
                        OnPrivateChannelMessageEvent(
                            new PrivateChannelMessageEventArgs(
                            ((PrivateChannelMessagePacket)packet).ChannelID,
                            this.GetCharacterName(((PrivateChannelMessagePacket)packet).ChannelID),
                            ((PrivateChannelMessagePacket)packet).CharacterID,
                            this.GetCharacterName(((PrivateChannelMessagePacket)packet).CharacterID),
                            ((PrivateChannelMessagePacket)packet).Message,
                            ((PrivateChannelMessagePacket)packet).ChannelID == this._id
                            ));
                        break;
                    case Packet.Type.CHANNEL_MESSAGE:
                        packet = new ChannelMessagePacket(packetData.type, packetData.data);
                        OnChannelMessageEvent(
                            new ChannelMessageEventArgs(
                            ((ChannelMessagePacket)packet).ChannelID,
                            this.GetChannelName(((ChannelMessagePacket)packet).ChannelID),
                            ((ChannelMessagePacket)packet).CharacterID,
                            this.GetCharacterName(((ChannelMessagePacket)packet).CharacterID),
                            ((ChannelMessagePacket)packet).Message,
                            this.GetChannelType(((ChannelMessagePacket)packet).ChannelID)
                            ));
                        break;
                    case Packet.Type.FORWARD:
                        packet = new ForwardPacket(packetData.type, packetData.data);
                        OnForwardEvent(
                            new ForwardEventArgs(
                            ((ForwardPacket)packet).ID1,
                            ((ForwardPacket)packet).ID2
                            ));
                        break;
                    case Packet.Type.AMD_MUX_INFO:
                        packet = new AmdMuxInfoPacket(packetData.type, packetData.data);
                        OnAmdMuxInfoEvent(
                            new AmdMuxInfoEventArgs(
                            ((AmdMuxInfoPacket)packet).Message
                            ));
                        break;
                    case Packet.Type.MESSAGE_SYSTEM:
                        packet = new SystemMessagePacket(packetData.type, packetData.data);
                        OnSystemMessageEvent(
                            new SystemMessageEventArgs(
                            ((SystemMessagePacket)packet).ClientID,
                            ((SystemMessagePacket)packet).WindowID,
                            ((SystemMessagePacket)packet).MessageID,
                            ((SystemMessagePacket)packet).Arguments,
                            ((SystemMessagePacket)packet).Notice
                            ));
                        break;
                    default:
                        if (packetData.type == Packet.Type.NULL && packetData.data != null)
                        {
                            if (BitConverter.ToInt32(packetData.data, 0) == 0 && packetData.data.Length == 4)
                            {
                                Trace.WriteLine("Disconnect packet received.", "[Debug]");
                                if (local == false)
                                    lock (this._threads)
                                        this._threads.Remove(Thread.CurrentThread);
                                this.Disconnect(false);
                                return;
                            }
                        }
                        packet = new UnknownPacket(packetData.type, packetData.data);
                        OnUnknownPacketEvent(
                            new UnknownPacketEventArgs(
                            ((UnknownPacket)packet).PacketType,
                            ((UnknownPacket)packet).UnknownData
                            ));
                        break;
                }
            }
#if !DEBUG
            catch (Exception ex)
            {
                this.Debug("Unexpected exception: " + ex.ToString(), "Error");
                if (this.ExceptionEvent != null)
                    this.ExceptionEvent(this, ex);
            }
#endif
            finally
            {
                if (local == false)
                {
                    lock (this._threads)
                        this._threads.Remove(Thread.CurrentThread);
                }
                else
                {
                    Monitor.Exit(this._receiveThread);
                }
            }
        }
        #region Events
        protected virtual void OnUnknownPacketEvent(UnknownPacketEventArgs e)
        {
            if (this.UnknownPacketEvent != null)
                this.UnknownPacketEvent(this, e);
        }

        protected virtual void OnSystemMessageEvent(SystemMessageEventArgs e)
        {
            this.Debug("Client:" + e.ClientID +
                " Window:" + e.WindowID +
                " ID:" + e.MessageID +
                //" Args:" + e.Arguments + // TODO: figure out a decent way to display these arguments
                " Notice:" + e.Notice, "[System]");
            if (this.SystemMessageEvent != null)
                this.SystemMessageEvent(this, e);
        }

        protected virtual void OnAmdMuxInfoEvent(AmdMuxInfoEventArgs e)
        {
            if (this.AmdMuxInfoEvent != null)
                this.AmdMuxInfoEvent(this, e);
        }

        protected virtual void OnForwardEvent(ForwardEventArgs e)
        {
            if (this.ForwardEvent != null)
                this.ForwardEvent(this, e);
        }

        protected virtual void OnChannelMessageEvent(ChannelMessageEventArgs e)
        {
            this.Debug(e.Character + ": " + e.Message, "[" + e.Channel + "]");

            if (this.ChannelMessageEvent != null)
                this.ChannelMessageEvent(this, e);
        }

        protected virtual void OnPrivateChannelMessageEvent(PrivateChannelMessageEventArgs e)
        {
            this.Debug(e.Character + ": " + e.Message, "[" + e.Channel + "]");

            if (this.PrivateChannelMessageEvent != null)
                this.PrivateChannelMessageEvent(this, e);
        }

        protected virtual void OnPrivateMessageEvent(PrivateMessageEventArgs e)
        {
            if (e.Outgoing)
                this.Debug(e.Message, "To [" + e.Character + "]:");
            else
                this.Debug(e.Message, "[" + e.Character + "]:");

            if (this.PrivateMessageEvent != null)
                this.PrivateMessageEvent(this, e);
        }

        protected virtual void OnVicinityMessageEvent(VicinityMessageEventArgs e)
        {
            this.Debug(e.Character + ": " + e.Message, "[Vicinity]");

            if (this.VicinityMessageEvent != null)
                this.VicinityMessageEvent(this, e);
        }

        protected virtual void OnPrivateChannelStatusEvent(PrivateChannelStatusEventArgs e)
        {
            if (e.Join)
                this.Debug(e.Character + " has joined the private channel", "[" + e.Channel + "]");
            else
                this.Debug(e.Character + " has left the private channel", "[" + e.Channel + "]");
            if (this.PrivateChannelStatusEvent != null)
                this.PrivateChannelStatusEvent(this, e);
        }

        protected virtual void OnChannelStatusEvent(ChannelStatusEventArgs e)
        {
            if (this.State != ChatState.Connected)
                this.OnStateChangeEvent(new StateChangeEventArgs(ChatState.Connected));

            lock (_channels)
            {
                this._channels[e.ID] = e.GetChannel();
            }
            if (e.Type == ChannelType.Unknown)
                this.Debug("Unknown channel type: " + e.TypeID, "[Error]");
            this.Debug("Joined channel: " + e.Name + " (ID:" + e.ID + " Type:" + e.Type.ToString() + " Muted:" + e.Muted.ToString() + " Flags:" + e.Flags.ToString() + ")", "[Bot]");
            if (e.Type == ChannelType.Organization)
            {
                this._organization = e.Name;
                this._organizationid = e.ID;
                this.Debug("Registered organization: " + e.Name + " (ID:" + e.ID + ")", "[Bot]");
            }

            if (this.ChannelStatusEvent != null)
                this.ChannelStatusEvent(this, e);
        }

        protected virtual void OnFriendStatusEvent(FriendStatusEventArgs e)
        {
            this.Debug("Friend status received: " + e.Character + " (ID:" + e.CharacterID + " Online:" + e.Online.ToString() + " Temporary:" + e.Temporary.ToString() + ")", "[Database]");

            if (this.FriendStatusEvent != null)
                this.FriendStatusEvent(this, e);
        }

        protected virtual void OnFriendRemovedEvent(CharacterIDEventArgs e)
        {
            this.Debug("Friend removed: " + e.Character, "[Database]");
            if (this.FriendRemovedEvent != null)
                this.FriendRemovedEvent(this, e);
        }

        protected virtual void OnAnonVicinityEvent(AnonVicinityEventArgs e)
        {
            if (this.AnonVicinityEvent != null)
                this.AnonVicinityEvent(this, e);
        }

        protected virtual void OnNameLookupEvent(NameLookupEventArgs e)
        {
            lock (this._characters)
            {
                // Handle name changes
                if (this._characters.ContainsKey(e.CharacterID))
                {
                    string oldName = this._characters[e.CharacterID];
                    if (this._charactersByName.ContainsKey(oldName))
                    {
                        uint oldID = this._charactersByName[oldName];
                        if (oldID != e.CharacterID)
                        {
                            // Old name entry was assigned to a different UID.
                            if (this._characters.ContainsKey(oldID))
                                this._characters.Remove(oldID);
                        }
                        this._charactersByName.Remove(oldName);
                    }
                }
                // Store id
                if (e.CharacterID > 0) this._characters[e.CharacterID] = e.Name;
                this._charactersByName[e.Name] = e.CharacterID;
            }
            if (e.CharacterID > 0)
            {
                this.Debug("Name lookup received: " + e.Name + " (ID:" + e.CharacterID + ")", "[Database]");
            }
            else
            {
                this.Debug("Character doesn't exist: " + e.Name, "[Database]");
            }
            // Notify other threads
            this._lookupReset.Set();
            this._lookupReset.Reset();
            // Fire the event
            if (this.NameLookupEvent != null)
                this.NameLookupEvent(this, e);
        }

        protected virtual void OnLoginOKEvent()
        {
            this.Debug("Logged in succesfully", "[Auth]");
            if (this.LoginOKEvent != null)
                this.LoginOKEvent(this, new EventArgs());
        }

        protected virtual void OnPrivateChannelRequestEvent(PrivateChannelRequestEventArgs e)
        {
            if (this.PrivateChannelRequestEvent != null)
                this.PrivateChannelRequestEvent(this, e);
            this.SendPacket(new PrivateChannelStatusPacket(e.CharacterID, e.Accept));
        }

        protected virtual void OnClientUnknownEvent(CharacterIDEventArgs e)
        {
            if (this.ClientUnknownEvent != null)
                this.ClientUnknownEvent(this, e);
        }

        protected virtual void OnLoginCharacterListEvent(LoginChararacterListEventArgs e)
        {
            if (this.LoginCharlistEvent != null)
                this.LoginCharlistEvent(this, e);

            List<string> characters = new List<string>();
            foreach (LoginCharacter c in e.CharacterList)
                characters.Add(c.Name);
            string characterslist = string.Join(", ", characters.ToArray());
            
            if (this._state != ChatState.CharacterSelect && !string.IsNullOrEmpty(this._character))
            {
                LoginCharacter character = null;
                foreach (LoginCharacter c in e.CharacterList)
                    if (c.Name.ToLower() == this._character.ToLower())
                        character = c;
                if (character == null)
                {
                    this.Debug(String.Format("The character name, {0}, was not found in {1}.", this._character, characterslist), "[Auth]");
                    this.OnStateChangeEvent(new StateChangeEventArgs(ChatState.Error));
                    return;
                }
                this.SendLoginCharacter(character);
                return;
            }
            this.Debug(String.Format("Character list received: {1}. Awaiting character selection", this._character, characterslist), "[Auth]");
        }

        protected virtual void OnLoginErrorEvent(LoginErrorEventArgs e)
        {
            this.Debug(e.Error, "[Auth]");

            if (this.LoginErrorEvent != null)
                this.LoginErrorEvent(this, e);
        }

        protected virtual void OnSimpleMessageEvent(SimpleMessageEventArgs e)
        {
            this.Debug(e.Message, "[System]");

            if (this.SimpleMessageEvent != null)
                this.SimpleMessageEvent(this, e);
        }

        protected virtual void OnLoginSeedEvent(LoginSeedEventArgs e)
        {
            this.Debug("Logging in with account: " + this._account, "[Auth]");
            this.SendPacket(new LoginSeedPacket(this._account, this._password, e.Seed));
            this.OnStateChangeEvent(new StateChangeEventArgs(ChatState.Login));
            if (this.LoginSeedEvent != null)
                this.LoginSeedEvent(this, e);
        }

        protected virtual void OnStateChangeEvent(StateChangeEventArgs e)
        {
            if (this._state == ChatState.Reconnecting && e.State == ChatState.Disconnected)
            {
                return;
            }
            if (this._state != e.State)
            {
                if (e.State == ChatState.Connected && this._pingTimer != null)
                {
                    this._pingTimer.Start();
                }
                if (e.State == ChatState.Disconnected && this._pingTimer != null)
                {
                    this._pingTimer.Stop();
                }
                if (e.State == ChatState.Disconnected && this._closing == false && this.AutoReconnect == true)
                {
                    this._state = ChatState.Reconnecting;
                    e = new StateChangeEventArgs(this._state);
                    if (this._socket != null)
                    {
                        if (this._socket.Connected) { this._socket.Close(); }
                    }
                    this._reconnectTimer.Interval = this.ReconnectDelay;
                    this._reconnectTimer.Start();
                }
                this._state = e.State;
                this.Debug("State changed to: " + e.State.ToString(), "[Bot]");
                if (this.StateChangeEvent != null)
                    this.StateChangeEvent(this, e);
            }
        }

        protected virtual void OnReconnectEvent(object sender, ElapsedEventArgs e)
        {
            this._reconnectTimer.Stop();
            this.Connect();
        }

        protected virtual void OnPongEvent()
        {
            this.Debug("Pong!", "[Bot]");
            this._lastPong = DateTime.Now;
        }

        protected virtual void OnPingTimerEvent(object sender, ElapsedEventArgs e)
        {
            if (this._socket == null || this._socket.Connected == false)
            {
                this.OnStateChangeEvent(new StateChangeEventArgs(ChatState.Disconnected));
            }
            TimeSpan ts = DateTime.Now.Subtract(this._lastPong);
            if (ts.TotalMilliseconds > (this.PingTimeout))
            {
                this.Debug("Connection timed out", "[Bot]");
                this.OnStateChangeEvent(new StateChangeEventArgs(ChatState.Disconnected));
                return;
            }
            this.Debug("Ping?", "[Bot]");
            this.SendPing();
        }
        #endregion

        #region Get Commands
        /// <summary>
        /// Retrieve character ID associated with a character name.
        /// </summary>
        /// <param name="character"></param>
        /// <returns>Character ID</returns>
        public UInt32 GetCharacterID(string character)
        {
            UInt32 id;
            this.GetCharacterID(character, this.LookupTimeout, out id);
            return id;
        }

		/// <summary>
        /// Retrieve character ID associated with a character name.
		/// </summary>
        /// <param name="character"></param>
        /// <param name="timeout">Timeout in ms. If set to 0, we'll only use the internal lookup table, not fetch info from server.</param>
		/// <returns>Character ID</returns>
        public UInt32 GetCharacterID(string character, int timeout)
        {
            UInt32 id;
            this.GetCharacterID(character, timeout, out id);
            return id;
        }

        public bool GetCharacterID(string character, int timeout, out UInt32 id)
        {
            // Default return value to 0
            id = 0;
            // Keep track of time so we can see when we hit the timeout
            DateTime startTime = DateTime.Now;
            // Some basic error handling and formatting first
            character = Format.UppercaseFirst(character);
            if (this._charactersByName == null) 
                return false;
            // Check if we already have this character cached
            lock (this._characters)
            {
                if (this._charactersByName.ContainsKey(character))
                {
                    if (this._charactersByName[character] > 0)
                    {
                        // We found it!
                        id = this._charactersByName[character];
                        return true;
                    }
                    else
                    {
                        // If character id is 0, remove the entry
                        this._charactersByName.Remove(character);
                        return false;
                    }
                }
            }
            // We haven't found a cached entry, should we check with the server?
            if (timeout <= 0)
                return false;

            // Request the CharacterID from the server
            int currentTimeout = timeout;
            this.SendNameLookup(character);
            // Wait for a reply
            while (currentTimeout > 0)
            {
                // Wait for a name lookup to come in
                this._lookupReset.WaitOne(currentTimeout);
                // Check if we found it
                if (this.GetCharacterID(character, 0, out id))
                {
                    return true;
                }
                // Let's go for another round!
                currentTimeout = timeout - (int)(DateTime.Now - startTime).TotalMilliseconds;
            }
            // All failed, we never found the character
            return false;
        }

		/// <summary>
        /// Retrieve character name associated with an character ID
		/// </summary>
        /// <param name="characterID"></param>
		/// <returns></returns>
        public string GetCharacterName(UInt32 characterID)
        {
            if (characterID == 0 || characterID == UInt32.MaxValue)
                return "";
            if (this._characters == null)
                return "";
            lock (this._characters)
            {
                if (this._characters.ContainsKey(characterID))
                {
                    return this._characters[characterID];
                }
                else
                {
                    return "";
                }
            }
        }

		/// <summary>
		/// Retrieve channel ID associated with a channel name
		/// </summary>
		/// <param name="channelName"></param>
		/// <returns></returns>
        public BigInteger GetChannelID(String channelName)
        {
            lock (this._channels)
            {
                foreach (KeyValuePair<BigInteger, Channel> kvp in this._channels)
                {
                    if (kvp.Value.Name == channelName)
                        return kvp.Key;
                }
            }
            return new BigInteger(0);
        }

		/// <summary>
		/// Retrieve channel name associated with a channel ID.
		/// </summary>
		/// <param name="channelID"></param>
		/// <returns></returns>
        public string GetChannelName(Int32 channelID) { return this.GetChannelName(new BigInteger(channelID)); }
        /// <summary>
        /// Retrieve channel name associated with a channel ID.
        /// </summary>
        /// <param name="channelID"></param>
        /// <returns></returns>
		public string GetChannelName(BigInteger channelID)
        {
            if (this._channels == null)
                return "";

            lock (this._channels)
            {
                if (this._channels.ContainsKey(channelID))
                {
                    return this._channels[channelID].Name;
                }
                else
                {
                    return "";
                }
            }
        }
		/// <summary>
		/// Retrieve channel type of the provided channel.
		/// </summary>
		/// <param name="channelID"></param>
		/// <returns></returns>
        public ChannelType GetChannelType(BigInteger channelID)
        {
            lock (this._channels)
            {
                if (this._channels.ContainsKey(channelID))
                {
                    return this._channels[channelID].Type;
                }
                else
                {
                    return ChannelType.Unknown;
                }
            }
        }
        #endregion

        #region Send Commands
        public void SendPacket(Packet packet)
        {
            if (this._socket == null || !this._socket.Connected)
            {
                this.Debug("Not Connected", "[Error]");
                return;
            }
            switch (packet.PacketType)
            {
                case Packet.Type.PRIVATE_MESSAGE:
                case Packet.Type.CHANNEL_MESSAGE:
                    _slowQueue.Enqueue(packet.Priority, packet);
                    break;
                default:
                    _fastQueue.Enqueue(packet.Priority, packet);
                    break;
            }
			this._sendThread_ResetEvent.Set(); //Signal the send thread that we have added something.
        }

		/// <summary>
		/// Mute or unmute a channel
		/// </summary>
		/// <param name="channel">Name of channel to (un)mute</param>
		/// <param name="mute">true to mute, false to unmute</param>
        public void SendChannelMute(string channel, bool mute) { this.SendChannelMute(this.GetChannelID(channel), mute); }
		/// <summary>
		/// Mute or unmute a channel
		/// </summary>
		/// <param name="channelID">ID of channel to (un)mute</param>
		/// <param name="mute">true to mute, false to unmute</param>
        public void SendChannelMute(BigInteger channelID, bool mute)
        {
            this.Debug("Updating channel " + this.GetChannelName(channelID) + " with mute=" + mute.ToString(), "[Bot]");

            ChannelUpdatePacket p = new ChannelUpdatePacket(channelID, mute);
            p.Priority = PacketQueue.Priority.Standard;
            this.SendPacket(p);
        }

		/// <summary>
		/// Send a channel message (standard priority)
		/// </summary>
		/// <param name="channel"></param>
		/// <param name="text"></param>
        public void SendChannelMessage(string channel, string text) { this.SendChannelMessage(this.GetChannelID(channel), text, PacketQueue.Priority.Standard); }
		/// <summary>
		/// Send a channel message (standard priority)
		/// </summary>
		/// <param name="channelID"></param>
		/// <param name="text"></param>
        public void SendChannelMessage(BigInteger channelID, string text) { this.SendChannelMessage(channelID, text, PacketQueue.Priority.Standard); }
		/// <summary>
		/// Send a channel message. Custom priority
		/// </summary>
		/// <param name="channelID"></param>
		/// <param name="text"></param>
		/// <param name="priority"></param>
        public void SendChannelMessage(BigInteger channelID, string text, PacketQueue.Priority priority)
        {
            ChannelMessagePacket p = new ChannelMessagePacket(channelID, text);
            p.Priority = priority;
            this.SendPacket(p);
        }

		/// <summary>
		/// Add a friend. (standard priority)
		/// </summary>
        /// <param name="character"></param>
        public void SendFriendAdd(string character)
        {
            if (string.IsNullOrEmpty(character)) return;
            this.Debug("Adding character to friendslist: " + character, "[Bot]");

            ChatCommandPacket p = new ChatCommandPacket("addbuddy", character);
            p.Priority = PacketQueue.Priority.Standard;

            this.SendPacket(p);
        }

		/// <summary>
		/// Remove friend. (standard priority)
		/// </summary>
        /// <param name="character"></param>
        public void SendFriendRemove(string character)
        {
            if (string.IsNullOrEmpty(character)) return;
            this.Debug("Removing character from friendslist: " + character, "[Bot]");

            ChatCommandPacket p = new ChatCommandPacket("rembuddy", character);
            p.Priority = PacketQueue.Priority.Standard;

            this.SendPacket(p);
        }

        /// <summary>
        /// Send a /cc command. (standard priority)
        /// </summary>
        /// <param name="command"></param>
        public void SendChatCommand(string command) { SendChatCommand(command.Split(new char[]{' '}, StringSplitOptions.RemoveEmptyEntries)); }
        /// <summary>
        /// Send a /cc command. (standard priority)
        /// </summary>
        /// <param name="arguments"></param>
        public void SendChatCommand(params string[] arguments)
        {
            this.Debug("Sending command: /cc " + string.Join(" ", arguments), "[Bot]");

            ChatCommandPacket p = new ChatCommandPacket(arguments);
            p.Priority = PacketQueue.Priority.Standard;
            this.SendPacket(p);
        }

		/// <summary>
		/// Invite someone to your own private channel. (urgent priority)
		/// </summary>
        /// <param name="character"></param>
        public void SendPrivateChannelInvite(string character) { this.SendPrivateChannelInvite(this.GetCharacterID(character)); }
		/// <summary>
		/// Invite someone to your own private channel. (urgent priority)
		/// </summary>
        /// <param name="characterID"></param>
        public void SendPrivateChannelInvite(UInt32 characterID)
        {
            if (characterID == this._id)
                return;
            SimpleIdPacket p = new SimpleIdPacket(Packet.Type.PRIVATE_CHANNEL_INVITE, characterID);
            p.Priority = PacketQueue.Priority.Urgent;
            this.SendPacket(p);
        }

		/// <summary>
		/// Kick someone from your own private channel. (urgent priority)
		/// </summary>
        /// <param name="character"></param>
        public void SendPrivateChannelKick(string character) { this.SendPrivateChannelKick(this.GetCharacterID(character)); }
		/// <summary>
		/// Kick someone from your own private channel. (urgent priority)
		/// </summary>
        /// <param name="characterID"></param>
        public void SendPrivateChannelKick(UInt32 characterID)
        {
            if (characterID == this._id)
                return;
            SimpleIdPacket p = new SimpleIdPacket(Packet.Type.PRIVATE_CHANNEL_KICK, characterID);
            p.Priority = PacketQueue.Priority.Urgent;
            this.SendPacket(p);
        }

		/// <summary>
		/// Kick everyone from your own private channel. (urgent priority)
		/// </summary>
        public void SendPrivateChannelKickAll()
        {
            EmptyPacket p = new EmptyPacket(Packet.Type.PRIVATE_CHANNEL_KICKALL);
            p.Priority = PacketQueue.Priority.Urgent;
            this.SendPacket(p);
        }

		/// <summary>
		/// Leave someone elses private channel. (urgent priority)
		/// </summary>
		/// <param name="channel"></param>
        public void SendPrivateChannelLeave(string channel) { this.SendPrivateChannelLeave(this.GetCharacterID(channel)); }
        /// <summary>
        /// Leave someone elses private channel. (urgent priority)
        /// </summary>
        /// <param name="channelID"></param>
		public void SendPrivateChannelLeave(UInt32 channelID)
        {
            PrivateChannelStatusPacket p = new PrivateChannelStatusPacket(channelID, false);
            p.Priority = PacketQueue.Priority.Urgent;
            this.SendPacket(p);
        }

		/// <summary>
		/// Send a message to your own private channel (urgent priority).
		/// </summary>
		/// <param name="text"></param>
        public void SendPrivateChannelMessage(string text) { this.SendPrivateChannelMessage(this._id, text); }
		/// <summary>
		/// Send a message to someone elses private channel. (urgent priority)
		/// </summary>
		/// <param name="channel"></param>
		/// <param name="text"></param>
        public void SendPrivateChannelMessage(string channel, string text) { this.SendPrivateChannelMessage(this.GetCharacterID(channel), text); }
		/// <summary>
		/// Send a message to someone elses private channel. (urgent priority)
		/// </summary>
		/// <param name="channelID"></param>
		/// <param name="text"></param>
        public void SendPrivateChannelMessage(UInt32 channelID, string text)
        {
            PrivateChannelMessagePacket p = new PrivateChannelMessagePacket(channelID, text);
            p.Priority = PacketQueue.Priority.Urgent;
            this.SendPacket(p);
        }

		/// <summary>
		/// Send a private message (tell) to someone. (standard priority)
		/// </summary>
        /// <param name="character"></param>
		/// <param name="text"></param>
        public void SendPrivateMessage(string character, string text) { this.SendPrivateMessage(this.GetCharacterID(character), text, PacketQueue.Priority.Standard); }
		/// <summary>
		/// Send a private message (tell) to someone. (standard priority)
		/// </summary>
        /// <param name="characterID"></param>
		/// <param name="text"></param>
        public void SendPrivateMessage(UInt32 characterID, string text) { this.SendPrivateMessage(characterID, text, PacketQueue.Priority.Standard); }
		/// <summary>
		/// Send a private message (tell) to someone.
		/// </summary>
        /// <param name="characterID"></param>
		/// <param name="text"></param>
		/// <param name="priority"></param>
        public void SendPrivateMessage(UInt32 characterID, string text, PacketQueue.Priority priority)
        {
            if (characterID == this._id || characterID == 0)
                return;
            PrivateMessagePacket p = new PrivateMessagePacket(characterID, text);
            p.Priority = priority;
            this.SendPacket(p);
        }

		/// <summary>
        /// Query the server for a "name to character id" lookup. (urgent priority)
		/// </summary>
		/// <param name="name"></param>
        public void SendNameLookup(string name)
        {
            lock (this._characters)
                if (this._characters.ContainsValue(Format.UppercaseFirst(name)))
                    return;

            NameLookupPacket p = new NameLookupPacket(name);
            p.Priority = PacketQueue.Priority.Urgent;
            this.Debug("Requesting ID: " + name, "[Database]");
            this.SendPacket(p);
        }

		/// <summary>
		/// Ping the chatserver. (urgent priority)
		/// </summary>
        public void SendPing()
        {
            EmptyPacket p = new EmptyPacket(Packet.Type.PING);
            p.Priority = PacketQueue.Priority.Urgent;
            this.SendPacket(p);
        }

        public void SendLoginCharacter(LoginCharacter character)
        {
            if (this._state != ChatState.Login)
                throw new Exception("Not expecting character selection!");
            if (character == null)
                return;
            if (character.IsOnline && !this.IgnoreCharacterLoggedIn)
            {
                this.Debug("Character " + this._character + " is already online!", "[Auth]");
                this.OnStateChangeEvent(new StateChangeEventArgs(ChatState.Disconnected));
                return;
            }
            this._character = Format.UppercaseFirst(character.Name);
            this._id = character.ID;
            SimpleIdPacket p = new SimpleIdPacket(Packet.Type.LOGIN_SELCHAR, character.ID);
            p.Priority = PacketQueue.Priority.Urgent;
            this.SendPacket(p);
            this.Debug("Selecting character: " + this._character, "[Auth]");
            this.OnStateChangeEvent(new StateChangeEventArgs(ChatState.CharacterSelect));
        }
        #endregion

        public override string ToString()
        {
            string str = this.Account;
            if (!string.IsNullOrEmpty(this.Character))
                str += ":" + this.Character;
            str += "@" + this._serverAddress + ":" + this._port;
            return str;
        }

        protected void Debug(string msg, string cat)
        {
            if (this.DebugEvent != null)
                this.DebugEvent(this, new DebugEventArgs(this.ToString(), cat + " " + msg));
            Trace.WriteLine("[" + this.ToString() + "] " + cat + " " + msg);
        }
    } // end of Chat

    internal class ParsePacketData
    {
        public Packet.Type type;
        public short length = 0;
        public byte[] data;

        public ParsePacketData(Packet.Type t, short l, byte[] d)
        {
            this.type = t;
            this.length = l;
            if (d != null)
            {
                this.data = new byte[d.Length];
                d.CopyTo(this.data, 0);
            }
        }
    }
}