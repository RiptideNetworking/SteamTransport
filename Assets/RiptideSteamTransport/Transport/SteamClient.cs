
// This file is provided under The MIT License as part of RiptideSteamTransport.
// Copyright (c) 2021 Tom Weiland
// For additional information please see the included LICENSE.md file or view it on GitHub: https://github.com/tom-weiland/RiptideSteamTransport/blob/main/LICENSE.md

using RiptideNetworking.Utils;
using Steamworks;
using System;
using System.Threading.Tasks;
using UnityEngine;
using LogType = RiptideNetworking.Utils.LogType;

namespace RiptideNetworking.Transports.SteamTransport
{
    public class SteamClient : SteamCommon, IClient
    {
        /// <inheritdoc/>
        public event EventHandler Connected;
        /// <inheritdoc/>
        public event EventHandler ConnectionFailed;
        /// <inheritdoc/>
        public event EventHandler<ClientMessageReceivedEventArgs> MessageReceived;
        /// <inheritdoc/>
        public event EventHandler Disconnected;
        /// <inheritdoc/>
        public event EventHandler<ClientConnectedEventArgs> ClientConnected;
        /// <inheritdoc/>
        public event EventHandler<ClientDisconnectedEventArgs> ClientDisconnected;

        /// <inheritdoc/>
        public ushort Id { get; private set; }
        /// <inheritdoc/>
        public short RTT => throw new NotImplementedException();
        /// <inheritdoc/>
        public short SmoothRTT => throw new NotImplementedException();
        /// <inheritdoc/>
        public bool IsNotConnected => connectionState == ConnectionState.notConnected;
        /// <inheritdoc/>
        public bool IsConnecting => connectionState == ConnectionState.connecting;
        /// <inheritdoc/>
        public bool IsConnected => connectionState == ConnectionState.connected;
        /// <summary>The time (in milliseconds) after which to give up on a connection attempt.</summary>
        public ushort TimeoutTime { get; set; } = 5000;

        private Callback<SteamNetConnectionStatusChangedCallback_t> connectionStatusChanged = null;
        private CSteamID hostSteamId = CSteamID.Nil;
        private HSteamNetConnection hostConnection;
        private ConnectionState connectionState;
        private SteamServer localServer;
        private ActionQueue actionQueue;

        public SteamClient(SteamServer localServer = null, ushort timeoutTime = 5000, string logName = "Steam Client") : base(logName)
        {
            this.localServer = localServer;
            TimeoutTime = timeoutTime;
            actionQueue = new ActionQueue();
        }

        public void ChangeLocalServer(SteamServer newLocalServer)
        {
            localServer = newLocalServer;
        }

        /// <inheritdoc/>
        /// <remarks>Expects the host address to consist of a Steam ID (<see cref="ulong"/>).</remarks>
        public void Connect(string hostAddress, Message message)
        {
            if (message != null)
                RiptideLogger.Log(LogType.error, $"The Steam transport does not support including custom data in connection attempts! Use the {nameof(Client.Connected)} event to send a message instead."); // TODO: maybe revisit this?

            try
            {
#if UNITY_SERVER
                SteamGameServerNetworkingUtils.InitRelayNetworkAccess();
#else
                SteamNetworkingUtils.InitRelayNetworkAccess();
#endif
            }
            catch (Exception ex)
            {
                OnConnectionFailed(ex.ToString());
                return;
            }
            
            if (hostAddress == "localhost" || hostAddress == "127.0.0.1")
                ConnectLocal();
            else if (ulong.TryParse(hostAddress, out ulong hostId))
                TryConnect(hostId);
            else
                OnConnectionFailed($"Invalid host address, '{hostAddress}' is not a Steam ID!");
        }

        private async void TryConnect(ulong hostId)
        {
            try
            {
                RiptideLogger.Log(LogType.info, LogName, $"Connecting to {hostId}...");

                connectionStatusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);
                hostSteamId = new CSteamID(hostId);

                SteamNetworkingIdentity hostSteamNetIdentity = new SteamNetworkingIdentity();
                hostSteamNetIdentity.SetSteamID(hostSteamId);

                SteamNetworkingConfigValue_t[] options = new SteamNetworkingConfigValue_t[] { };
                hostConnection = SteamNetworkingSockets.ConnectP2P(ref hostSteamNetIdentity, 0, options.Length, options);

                Task timeOutTask = Task.Delay(TimeoutTime);
                await Task.WhenAny(timeOutTask);

                if (!IsConnected)
                    OnConnectionFailed("Connection request timed out!");
            }
            catch (Exception ex)
            {
                OnConnectionFailed(ex.ToString());
            }
        }
        
        private void ConnectLocal()
        {
            if (localServer == null)
            {
                OnConnectionFailed($"No locally running server was specified to connect to! Either pass a {nameof(SteamServer)} instance to your {nameof(SteamClient)}'s constructor or call its {nameof(SteamClient.ChangeLocalServer)} method before attempting to connect locally.");
                return;
            }

            RiptideLogger.Log(LogType.info, LogName, $"Connecting to local server...");

            connectionStatusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);
            hostSteamId = SteamUser.GetSteamID();

            SteamNetworkingIdentity clientIdentity = new SteamNetworkingIdentity();
            SteamNetworkingIdentity serverIdentity = new SteamNetworkingIdentity();
            clientIdentity.SetSteamID(hostSteamId);
            serverIdentity.SetSteamID(hostSteamId);

            SteamNetworkingSockets.CreateSocketPair(out HSteamNetConnection connectionToClient, out HSteamNetConnection connectionToServer, false, ref clientIdentity, ref serverIdentity);
            hostConnection = connectionToServer;

            localServer.NewClientConnected(hostSteamId, connectionToClient);
            ConnectionStatusChangedToConnected();
        }

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t callback)
        {
            if (!callback.m_hConn.Equals(hostConnection))
            {
                // When connecting via local loopback connection to a locally running SteamServer (aka
                // this player is also the host), other external clients that attempt to connect seem
                // to trigger ConnectionStatusChanged callbacks for the locally connected client. Not
                // 100% sure why this is the case, but returning out of the callback here when the
                // connection doesn't match that between local client & server avoids the problem.
                return;
            }

            switch (callback.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    ConnectionStatusChangedToConnected();
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    RiptideLogger.Log(LogType.info, LogName, $"Connection was closed by peer: {callback.m_info.m_szEndDebug}");
                    OnDisconnected();
                    break;

                default:
                    RiptideLogger.Log(LogType.info, LogName, $"Connection state changed: {callback.m_info.m_eState} - {callback.m_info.m_szEndDebug}");
                    break;
            }
        }

        private void ConnectionStatusChangedToConnected()
        {
            connectionState = ConnectionState.connected;

            actionQueue.ExecuteAll();
        }

        /// <inheritdoc/>
        public void Tick()
        {
            HandleMessages();
        }

        private void HandleMessages()
        {
            IntPtr[] ptrs = new IntPtr[MaxMessages]; // TODO: remove allocation?

            int messageCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(hostConnection, ptrs, MaxMessages);
            if (messageCount > 0)
            {
                for (int i = 0; i < messageCount; i++)
                {
                    Message message = SteamProcessMessage(ptrs[i], out HeaderType messageHeader);
                    if (IsConnected)
                        Handle(message, messageHeader);
                    else
                    {
                        Debug.Log("Ok this actually happens");
                        actionQueue.Add(() => Handle(message, messageHeader));
                    }
                }
            }
        }

        private void Handle(Message message, HeaderType messageHeader)
        {
            switch (messageHeader)
            {
                case HeaderType.unreliable:
                case HeaderType.reliable:
                    OnMessageReceived(new ClientMessageReceivedEventArgs(message.GetUShort(), message));
                    break;

                case HeaderType.welcome:
                    HandleWelcome(message);
                    break;
                case HeaderType.clientConnected:
                    HandleClientConnected(message);
                    break;
                case HeaderType.clientDisconnected:
                    HandleClientDisconnected(message);
                    break;
                default:
                    break;
            }

            message.Release();
        }

        public void FlushMessages()
        {
            SteamNetworkingSockets.FlushMessagesOnConnection(hostConnection);
        }

        /// <inheritdoc/>
        public void Send(Message message, bool shouldRelease = true)
        {
            SteamSend(message, hostConnection);

            if (shouldRelease)
                message.Release();
        }

        /// <inheritdoc/>
        public void Disconnect()
        {
            if (IsNotConnected)
                return;

            LocalDisconnect();
            RiptideLogger.Log(LogType.info, LogName, "Disconnected.");
        }

        private void LocalDisconnect()
        {
            connectionState = ConnectionState.notConnected;

            if (connectionStatusChanged != null)
            {
                connectionStatusChanged.Dispose();
                connectionStatusChanged = null;
            }

            if (hostConnection.m_HSteamNetConnection != 0)
            {
                SteamNetworkingSockets.CloseConnection(hostConnection, 0, "Disconnected", false);
                hostConnection.m_HSteamNetConnection = 0;
            }
        }

        #region Messages
        /// <summary>Handles a welcome message.</summary>
        /// <param name="message">The welcome message to handle.</param>
        private void HandleWelcome(Message message)
        {
            Id = message.GetUShort();

            SendWelcomeReceived();
            OnConnected();
        }

        /// <summary>Sends a welcome (received) message.</summary>
        private void SendWelcomeReceived()
        {
            Message message = MessageExtensionsTransports.Create(HeaderType.welcome);
            message.Add(Id);

            Send(message);
        }

        /// <summary>Handles a client connected message.</summary>
        /// <param name="message">The client connected message to handle.</param>
        private void HandleClientConnected(Message message)
        {
            OnClientConnected(new ClientConnectedEventArgs(message.GetUShort()));
        }

        /// <summary>Handles a client disconnected message.</summary>
        /// <param name="message">The client disconnected message to handle.</param>
        private void HandleClientDisconnected(Message message)
        {
            OnClientDisconnected(new ClientDisconnectedEventArgs(message.GetUShort()));
        }
        #endregion

        #region Events
        /// <summary>Invokes the <see cref="Connected"/> event.</summary>
        private void OnConnected()
        {
            RiptideLogger.Log(LogType.info, LogName, "Connected successfully!");
            Connected?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Invokes the <see cref="ConnectionFailed"/> event.</summary>
        private void OnConnectionFailed(string reason)
        {
            RiptideLogger.Log(LogType.info, LogName, $"Connection to server failed: {reason}");

            LocalDisconnect();
            ConnectionFailed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Invokes the <see cref="MessageReceived"/> event.</summary>
        /// <param name="e">The event args to invoke the event with.</param>
        private void OnMessageReceived(ClientMessageReceivedEventArgs e)
        {
            MessageReceived?.Invoke(this, e);
        }

        /// <summary>Invokes the <see cref="Disconnected"/> event.</summary>
        private void OnDisconnected()
        {
            RiptideLogger.Log(LogType.info, LogName, "Disconnected from server (initiated remotely).");

            LocalDisconnect();
            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Invokes the <see cref="ClientConnected"/> event.</summary>
        /// <param name="e">The event args to invoke the event with.</param>
        private void OnClientConnected(ClientConnectedEventArgs e)
        {
            RiptideLogger.Log(LogType.info, LogName, $"Client {e.Id} connected.");
            ClientConnected?.Invoke(this, e);
        }

        /// <summary>Invokes the <see cref="ClientDisconnected"/> event.</summary>
        /// <param name="e">The event args to invoke the event with.</param>
        private void OnClientDisconnected(ClientDisconnectedEventArgs e)
        {
            RiptideLogger.Log(LogType.info, LogName, $"Client {e.Id} disconnected.");
            ClientDisconnected?.Invoke(this, e);
        }
        #endregion
    }
}