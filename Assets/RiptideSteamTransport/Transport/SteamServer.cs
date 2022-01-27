
// This file is provided under The MIT License as part of RiptideSteamTransport.
// Copyright (c) 2021 Tom Weiland
// For additional information please see the included LICENSE.md file or view it on GitHub: https://github.com/tom-weiland/RiptideSteamTransport/blob/main/LICENSE.md

using RiptideNetworking.Utils;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LogType = RiptideNetworking.Utils.LogType;

namespace RiptideNetworking.Transports.SteamTransport
{
    public class SteamServer : SteamCommon, IServer
    {
        /// <inheritdoc/>
        public event EventHandler<ServerClientConnectedEventArgs> ClientConnected;
        /// <inheritdoc/>
        public event EventHandler<ServerMessageReceivedEventArgs> MessageReceived;
        /// <inheritdoc/>
        public event EventHandler<ClientDisconnectedEventArgs> ClientDisconnected;

        /// <inheritdoc/>
        public ushort Port { get; private set; }
        /// <inheritdoc/>
        public ushort MaxClientCount { get; private set; }
        /// <inheritdoc/>
        public int ClientCount => clients.Count;
        /// <inheritdoc/>
        public IConnectionInfo[] Clients => clients.Values.ToArray();
        /// <inheritdoc/>
        public bool AllowAutoMessageRelay { get; set; } = false;

        /// <summary>Currently connected clients, accessible by their endpoints or numeric ID.</summary>
        private DoubleKeyDictionary<ushort, CSteamID, SteamConnection> clients;
        /// <summary>All currently unused client IDs.</summary>
        private List<ushort> availableClientIds;
        private HSteamListenSocket listenSocket;
        private Callback<SteamNetConnectionStatusChangedCallback_t> connectionStatusChanged;

        public SteamServer(string logName = "Steam Server") : base(logName)
        {

        }

        /// <inheritdoc path="/summary | //param[2]"/>
        /// <param name="port">Virtual local port number. If you are only using one <see cref="SteamServer"/>, use zero. If you need to run multiple <see cref="SteamServer"/>s to handle multiple sets of connections, then <paramref name="port"/> should be &lt;1000.</param>
        public void Start(ushort port, ushort maxClientCount)
        {
            Port = port;
            MaxClientCount = maxClientCount;
            clients = new DoubleKeyDictionary<ushort, CSteamID, SteamConnection>(MaxClientCount);

            InitializeClientIds();

            connectionStatusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);

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
                Debug.LogException(ex);
            }

            SteamNetworkingConfigValue_t[] options = new SteamNetworkingConfigValue_t[] { };
            listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(port, options.Length, options);

            RiptideLogger.Log(LogType.info, LogName, port == 0 ? "Started server." : $"Started on virtual port {port}");
        }

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t callback)
        {
            ulong clientSteamID = callback.m_info.m_identityRemote.GetSteamID64();
            switch (callback.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    if (clients.Count < MaxClientCount)
                    {
                        EResult result = SteamNetworkingSockets.AcceptConnection(callback.m_hConn);
                        if (result == EResult.k_EResultOK)
                            RiptideLogger.Log(LogType.info, LogName, $"Accepting connection from {clientSteamID}...");
                        else
                            RiptideLogger.Log(LogType.warning, LogName, $"Connection from {clientSteamID} could not be accepted: {result}");
                    }
                    else
                    {
                        RiptideLogger.Log(LogType.info, LogName, $"Server is full! Rejecting connection from {clientSteamID}.");
                        SteamNetworkingSockets.CloseConnection(callback.m_hConn, 0, "Server full", false);
                    }
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    NewClientConnected(callback.m_info.m_identityRemote.GetSteamID(), callback.m_hConn);
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    if (clients.TryGetValue(callback.m_info.m_identityRemote.GetSteamID(), out SteamConnection client))
                        LocalDisconnect(client, "Disconnected");
                    break;

                default:
                    RiptideLogger.Log(LogType.info, LogName, $"{clientSteamID}'s connection state changed: {callback.m_info.m_eState}");
                    break;
            }
        }

        internal void NewClientConnected(CSteamID steamId, HSteamNetConnection connection)
        {
            ushort id = GetAvailableClientId();
            SteamConnection steamConnection = new SteamConnection(this, steamId, id, connection);
            clients.Add(id, steamId, steamConnection);
            OnClientConnected(steamConnection.SteamId, new ServerClientConnectedEventArgs(steamConnection, null));
        }

        /// <inheritdoc/>
        public void Tick()
        {
            HandleMessages();
        }

        private void HandleMessages()
        {
            foreach (SteamConnection client in clients.Values)
            {
                IntPtr[] ptrs = new IntPtr[MaxMessages]; // TODO: remove allocation?

                // TODO: consider using poll groups -> https://partner.steamgames.com/doc/api/ISteamNetworkingSockets#functions_poll_groups
                int messageCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(client.SteamNetConnection, ptrs, MaxMessages);
                if (messageCount > 0)
                {
                    for (int i = 0; i < messageCount; i++)
                    {
                        Message message = SteamProcessMessage(ptrs[i], out HeaderType messageHeader);
                        switch (messageHeader)
                        {
                            case HeaderType.unreliable:
                            case HeaderType.reliable:
                                OnMessageReceived(new ServerMessageReceivedEventArgs(client.Id, message.GetUShort(), message));
                                break;
                            case HeaderType.unreliableAutoRelay:
                            case HeaderType.reliableAutoRelay:
                                if (AllowAutoMessageRelay)
                                    SendToAll(message, client.Id, false);
                                else
                                    OnMessageReceived(new ServerMessageReceivedEventArgs(client.Id, message.GetUShort(), message));
                                break;

                            case HeaderType.welcome:
                                client.HandleWelcomeReceived(message);
                                break;
                            default:
                                break;
                        }

                        message.Release();
                    }
                }
            }
        }

        public void FlushMessages()
        {
            foreach (SteamConnection client in clients.Values)
                SteamNetworkingSockets.FlushMessagesOnConnection(client.SteamNetConnection);
        }

        /// <inheritdoc/>
        public void Send(Message message, ushort toClientId, bool shouldRelease = true)
        {
            if (clients.TryGetValue(toClientId, out SteamConnection client))
                Send(message, client, false);

            if (shouldRelease)
                message.Release();
        }

        internal void Send(Message message, SteamConnection client, bool shouldRelease = true)
        {
            SteamSend(message, client.SteamNetConnection);

            if (shouldRelease)
                message.Release();
        }

        /// <inheritdoc/>
        public void SendToAll(Message message, bool shouldRelease = true)
        {
            foreach (SteamConnection client in clients.Values)
                Send(message, client, false);

            if (shouldRelease)
                message.Release();
        }

        /// <inheritdoc/>
        public void SendToAll(Message message, ushort exceptToClientId, bool shouldRelease = true)
        {
            foreach (SteamConnection client in clients.Values)
                if (client.Id != exceptToClientId)
                    Send(message, client, false);

            if (shouldRelease)
                message.Release();
        }

        /// <inheritdoc/>
        public void DisconnectClient(ushort clientId)
        {
            if (clients.TryGetValue(clientId, out SteamConnection client))
            {
                RiptideLogger.Log(LogType.info, LogName, $"Kicked {client.SteamId} (ID: {client.Id}).");

                LocalDisconnect(client, "Kicked by server");
                availableClientIds.Add(client.Id);
            }
            else
                RiptideLogger.Log(LogType.warning, LogName, $"Failed to kick {client.SteamId} because they weren't connected!");
        }

        private void LocalDisconnect(SteamConnection client, string reason)
        {
            SteamNetworkingSockets.CloseConnection(client.SteamNetConnection, 0, reason, false);
            clients.Remove(client.Id, client.SteamId);

            OnClientDisconnected(client.SteamId.m_SteamID, new ClientDisconnectedEventArgs(client.Id));
            availableClientIds.Add(client.Id);
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            if (connectionStatusChanged != null)
            {
                connectionStatusChanged.Dispose();
                connectionStatusChanged = null;
            }

            foreach (SteamConnection client in clients.Values)
                SteamNetworkingSockets.CloseConnection(client.SteamNetConnection, 0, "Server stopped.", false);
            clients.Clear();

            SteamNetworkingSockets.CloseListenSocket(listenSocket);
            RiptideLogger.Log(LogType.info, LogName, "Server stopped.");
        }

        /// <summary>Initializes available client IDs.</summary>
        private void InitializeClientIds()
        {
            availableClientIds = new List<ushort>(MaxClientCount);
            for (ushort i = 1; i <= MaxClientCount; i++)
                availableClientIds.Add(i);
        }

        /// <summary>Retrieves an available client ID.</summary>
        /// <returns>The client ID. 0 if none available.</returns>
        private ushort GetAvailableClientId()
        {
            if (availableClientIds.Count > 0)
            {
                ushort id = availableClientIds[0];
                availableClientIds.RemoveAt(0);
                return id;
            }
            else
            {
                RiptideLogger.Log(LogType.error, LogName, "No available client IDs, assigned 0!");
                return 0;
            }
        }

        #region Messages
        /// <summary>Sends a client connected message.</summary>
        /// <param name="endPoint">The endpoint of the newly connected client.</param>
        /// <param name="id">The ID of the newly connected client.</param>
        private void SendClientConnected(CSteamID clientSteamId, ushort id)
        {
            if (clients.Count <= 1)
                return; // We don't send this to the newly connected client anyways, so don't even bother creating a message if he is the only one connected

            Message message = MessageExtensionsTransports.Create(HeaderType.clientConnected);
            message.Add(id);

            foreach (SteamConnection client in clients.Values)
            {
                if (!client.SteamId.Equals(clientSteamId))
                    Send(message, client, false);
            }

            message.Release();
        }

        /// <summary>Sends a client disconnected message.</summary>
        /// <param name="id">The numeric ID of the client that disconnected.</param>
        private void SendClientDisconnected(ushort id)
        {
            Message message = MessageExtensionsTransports.Create(HeaderType.clientDisconnected);
            message.Add(id);

            foreach (SteamConnection client in clients.Values)
                Send(message, client, false);

            message.Release();
        }
        #endregion

        #region Events
        /// <summary>Invokes the <see cref="ClientConnected"/> event.</summary>
        /// <param name="clientEndPoint">The endpoint of the newly connected client.</param>
        /// <param name="e">The event args to invoke the event with.</param>
        internal void OnClientConnected(CSteamID clientSteamId, ServerClientConnectedEventArgs e)
        {
            RiptideLogger.Log(LogType.info, LogName, $"{clientSteamId} connected successfully! Client ID: {e.Client.Id}");

            ClientConnected?.Invoke(this, e);
            SendClientConnected(clientSteamId, e.Client.Id);
        }

        /// <summary>Invokes the <see cref="MessageReceived"/> event.</summary>
        /// <param name="e">The event args to invoke the event with.</param>
        private void OnMessageReceived(ServerMessageReceivedEventArgs e)
        {
            MessageReceived?.Invoke(this, e);
        }

        /// <summary>Invokes the <see cref="ClientDisconnected"/> event.</summary>
        /// <param name="e">The event args to invoke the event with.</param>
        private void OnClientDisconnected(ulong steamId, ClientDisconnectedEventArgs e)
        {
            RiptideLogger.Log(LogType.info, LogName, $"Client {e.Id} (Steam ID: {steamId}) has disconnected.");

            ClientDisconnected?.Invoke(this, e);
            SendClientDisconnected(e.Id);
        }
        #endregion
    }
}
