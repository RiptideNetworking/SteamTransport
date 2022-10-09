// This file is provided under The MIT License as part of RiptideSteamTransport.
// Copyright (c) Tom Weiland
// For additional information please see the included LICENSE.md file or view it on GitHub:
// https://github.com/tom-weiland/RiptideSteamTransport/blob/main/LICENSE.md

using Steamworks;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Riptide.Transports.Steam
{
    public class SteamServer : SteamPeer, IServer
    {
        public event EventHandler<ConnectedEventArgs> Connected;
        public event EventHandler<DisconnectedEventArgs> Disconnected;

        public ushort Port { get; private set; }

        private Dictionary<CSteamID, SteamConnection> connections;
        private HSteamListenSocket listenSocket;
        private Callback<SteamNetConnectionStatusChangedCallback_t> connectionStatusChanged;

        public void Start(ushort port)
        {
            Port = port;
            connections = new Dictionary<CSteamID, SteamConnection>();

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
        }

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t callback)
        {
            CSteamID clientSteamId = callback.m_info.m_identityRemote.GetSteamID();
            switch (callback.m_info.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    Accept(callback.m_hConn);
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    Add(new SteamConnection(clientSteamId, callback.m_hConn, this));
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                    SteamNetworkingSockets.CloseConnection(callback.m_hConn, 0, "Closed by peer", false);
                    OnDisconnected(clientSteamId, DisconnectReason.Disconnected);
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    SteamNetworkingSockets.CloseConnection(callback.m_hConn, 0, "Problem detected", false);
                    OnDisconnected(clientSteamId, DisconnectReason.TransportError);
                    break;

                default:
                    Debug.Log($"{LogName}: {clientSteamId}'s connection state changed - {callback.m_info.m_eState}");
                    break;
            }
        }

        internal void Add(SteamConnection connection)
        {
            if (!connections.ContainsKey(connection.SteamId))
            {
                connections.Add(connection.SteamId, connection);
                OnConnected(connection);
            }
            else
                Debug.Log($"{LogName}: Connection from {connection.SteamId} could not be accepted: Already connected");
        }

        private void Accept(HSteamNetConnection connection)
        {
            EResult result = SteamNetworkingSockets.AcceptConnection(connection);
            if (result != EResult.k_EResultOK)
                Debug.LogWarning($"{LogName}: Connection could not be accepted: {result}");
        }

        public void Close(Connection connection)
        {
            if (connection is SteamConnection steamConnection)
            {
                SteamNetworkingSockets.CloseConnection(steamConnection.SteamNetConnection, 0, "Disconnected by server", false);
                connections.Remove(steamConnection.SteamId);
            }
        }

        public void Poll()
        {
            foreach (SteamConnection connection in connections.Values)
                Receive(connection);
        }

        // TODO: disable nagle so this isn't needed
        //public void Flush()
        //{
        //    foreach (SteamConnection connection in connections.Values)
        //        SteamNetworkingSockets.FlushMessagesOnConnection(connection.SteamNetConnection);
        //}

        public void Shutdown()
        {
            if (connectionStatusChanged != null)
            {
                connectionStatusChanged.Dispose();
                connectionStatusChanged = null;
            }

            foreach (SteamConnection connection in connections.Values)
                SteamNetworkingSockets.CloseConnection(connection.SteamNetConnection, 0, "Server stopped", false);

            connections.Clear();
            SteamNetworkingSockets.CloseListenSocket(listenSocket);
        }

        protected internal virtual void OnConnected(Connection connection)
        {
            Connected?.Invoke(this, new ConnectedEventArgs(connection));
        }

        protected virtual void OnDisconnected(CSteamID steamId, DisconnectReason reason)
        {
            if (connections.TryGetValue(steamId, out SteamConnection connection))
            {
                Disconnected?.Invoke(this, new DisconnectedEventArgs(connection, reason));
                connections.Remove(steamId);
            }
        }
    }
}
