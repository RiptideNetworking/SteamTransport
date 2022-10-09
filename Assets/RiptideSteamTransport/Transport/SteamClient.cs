// This file is provided under The MIT License as part of RiptideSteamTransport.
// Copyright (c) Tom Weiland
// For additional information please see the included LICENSE.md file or view it on GitHub:
// https://github.com/tom-weiland/RiptideSteamTransport/blob/main/LICENSE.md

using Steamworks;
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Riptide.Transports.Steam
{
    public class SteamClient : SteamPeer, IClient
    {
        public event EventHandler Connected;
        public event EventHandler ConnectionFailed;
        public event EventHandler<DisconnectedEventArgs> Disconnected;

        private const string LocalHostName = "localhost";
        private const string LocalHostIP = "127.0.0.1";

        private SteamConnection steamConnection;
        private SteamServer localServer;
        private Callback<SteamNetConnectionStatusChangedCallback_t> connectionStatusChanged;

        public SteamClient(SteamServer localServer = null)
        {
            this.localServer = localServer;
        }

        public void ChangeLocalServer(SteamServer newLocalServer)
        {
            localServer = newLocalServer;
        }

        public bool Connect(string hostAddress, out Connection connection, out string connectError)
        {
            connection = null;

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
                connectError = $"Couldn't connect: {ex}";
                return false;
            }

            connectError = $"Invalid host address '{hostAddress}'! Expected '{LocalHostIP}' or '{LocalHostName}' for local connections, or a valid Steam ID.";
            if (hostAddress == LocalHostIP || hostAddress == LocalHostName)
            {
                if (localServer == null)
                {
                    connectError = $"No locally running server was specified to connect to! Either pass a {nameof(SteamServer)} instance to your {nameof(SteamClient)}'s constructor or call its {nameof(SteamClient.ChangeLocalServer)} method before attempting to connect locally.";
                    connection = null;
                    return false;
                }

                connection = steamConnection = ConnectLocal();
                return true;
            }
            else if (ulong.TryParse(hostAddress, out ulong hostId))
            {
                connection = steamConnection = TryConnect(new CSteamID(hostId));
                return connection != null;
            }

            return false;
        }

        private SteamConnection ConnectLocal()
        {
            Debug.Log($"{LogName}: Connecting to locally running server...");

            connectionStatusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);
            CSteamID playerSteamId = SteamUser.GetSteamID();

            SteamNetworkingIdentity clientIdentity = new SteamNetworkingIdentity();
            clientIdentity.SetSteamID(playerSteamId);
            SteamNetworkingIdentity serverIdentity = new SteamNetworkingIdentity();
            serverIdentity.SetSteamID(playerSteamId);

            SteamNetworkingSockets.CreateSocketPair(out HSteamNetConnection connectionToClient, out HSteamNetConnection connectionToServer, false, ref clientIdentity, ref serverIdentity);

            localServer.Add(new SteamConnection(playerSteamId, connectionToClient, this));
            OnConnected();
            return new SteamConnection(playerSteamId, connectionToServer, this);
        }

        private SteamConnection TryConnect(CSteamID hostId)
        {
            try
            {
                connectionStatusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);

                SteamNetworkingIdentity serverIdentity = new SteamNetworkingIdentity();
                serverIdentity.SetSteamID(hostId);

                SteamNetworkingConfigValue_t[] options = new SteamNetworkingConfigValue_t[] { };
                HSteamNetConnection connectionToServer = SteamNetworkingSockets.ConnectP2P(ref serverIdentity, 0, options.Length, options);

                ConnectTimeout();
                return new SteamConnection(hostId, connectionToServer, this);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                OnConnectionFailed();
                return null;
            }
        }

        private async void ConnectTimeout() // TODO: confirm if this is needed, Riptide *should* take care of timing out the connection
        {
            Task timeOutTask = Task.Delay(6000); // TODO: use Riptide Client's TimeoutTime
            await Task.WhenAny(timeOutTask);

            if (!steamConnection.IsConnected)
                OnConnectionFailed();
        }

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t callback)
        {
            if (!callback.m_hConn.Equals(steamConnection.SteamNetConnection))
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
                    OnConnected();
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                    SteamNetworkingSockets.CloseConnection(callback.m_hConn, 0, "Closed by peer", false);
                    OnDisconnected(DisconnectReason.Disconnected);
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    SteamNetworkingSockets.CloseConnection(callback.m_hConn, 0, "Problem detected", false);
                    OnDisconnected(DisconnectReason.TransportError);
                    break;

                default:
                    Debug.Log($"{LogName}: Connection state changed - {callback.m_info.m_eState} | {callback.m_info.m_szEndDebug}");
                    break;
            }
        }

        public void Poll()
        {
            if (steamConnection != null)
                Receive(steamConnection);
        }

        // TODO: disable nagle so this isn't needed
        //public void Flush()
        //{
        //    foreach (SteamConnection connection in connections.Values)
        //        SteamNetworkingSockets.FlushMessagesOnConnection(connection.SteamNetConnection);
        //}

        public void Disconnect()
        {
            if (connectionStatusChanged != null)
            {
                connectionStatusChanged.Dispose();
                connectionStatusChanged = null;
            }

            SteamNetworkingSockets.CloseConnection(steamConnection.SteamNetConnection, 0, "Disconnected", false);
            steamConnection = null;
        }

        protected virtual void OnConnected()
        {
            Connected?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnConnectionFailed()
        {
            ConnectionFailed?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnDisconnected(DisconnectReason reason)
        {
            Disconnected?.Invoke(this, new DisconnectedEventArgs(steamConnection, reason));
        }
    }
}
