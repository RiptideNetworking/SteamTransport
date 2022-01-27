
// This file is provided under The MIT License as part of RiptideSteamTransport.
// Copyright (c) 2021 Tom Weiland
// For additional information please see the included LICENSE.md file or view it on GitHub: https://github.com/tom-weiland/RiptideSteamTransport/blob/main/LICENSE.md

using RiptideNetworking.Utils;
using Steamworks;

namespace RiptideNetworking.Transports.SteamTransport
{
    public class SteamConnection : IConnectionInfo
    {
        /// <inheritdoc/>
        public ushort Id { get; private set; }
        public CSteamID SteamId { get; private set; }
        /// <inheritdoc/>
        public short RTT => throw new System.NotImplementedException();
        /// <inheritdoc/>
        public short SmoothRTT => throw new System.NotImplementedException();
        /// <inheritdoc/>
        public bool IsNotConnected => connectionState == ConnectionState.notConnected;
        /// <inheritdoc/>
        public bool IsConnecting => connectionState == ConnectionState.connecting;
        /// <inheritdoc/>
        public bool IsConnected => connectionState == ConnectionState.connected;
        
        internal HSteamNetConnection SteamNetConnection { get; private set; }

        private readonly SteamServer server;
        private ConnectionState connectionState;

        internal SteamConnection(SteamServer server, CSteamID steamId, ushort id, HSteamNetConnection steamNetConnection)
        {
            this.server = server;
            SteamId = steamId;
            Id = id;
            SteamNetConnection = steamNetConnection;

            connectionState = ConnectionState.connecting;
            SendWelcome();
        }

        internal void Disconnect()
        {
            connectionState = ConnectionState.notConnected;
        }

        #region Messages
        /// <summary>Sends a welcome message.</summary>
        internal void SendWelcome()
        {
            Message message = MessageExtensionsTransports.Create(HeaderType.welcome);
            message.Add(Id);

            server.Send(message, this);
        }

        /// <summary>Handles a welcome message.</summary>
        /// <param name="message">The welcome message to handle.</param>
        internal void HandleWelcomeReceived(Message message)
        {
            ushort id = message.GetUShort();
            if (Id != id)
                RiptideLogger.Log(LogType.error, server.LogName, $"Client has assumed ID {id} instead of {Id}!");
        }
        #endregion
    }
};