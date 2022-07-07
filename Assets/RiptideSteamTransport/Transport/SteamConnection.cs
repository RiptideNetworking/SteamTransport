// This file is provided under The MIT License as part of RiptideSteamTransport.
// Copyright (c) Tom Weiland
// For additional information please see the included LICENSE.md file or view it on GitHub:
// https://github.com/tom-weiland/RiptideSteamTransport/blob/main/LICENSE.md

using Steamworks;
using System;
using System.Collections.Generic;

namespace Riptide.Transports.Steam
{
    public class SteamConnection : Connection, IEquatable<SteamConnection>
    {
        public readonly CSteamID SteamId;
        public readonly HSteamNetConnection SteamNetConnection;

        private readonly SteamPeer peer;

        internal SteamConnection(CSteamID steamId, HSteamNetConnection steamNetConnection, SteamPeer peer)
        {
            SteamId = steamId;
            SteamNetConnection = steamNetConnection;
            this.peer = peer;
        }

        protected override void Send(byte[] dataBuffer, int amount)
        {
            peer.Send(dataBuffer, amount, SteamNetConnection);
        }

        /// <inheritdoc/>
        public override string ToString() => SteamNetConnection.ToString();

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as SteamConnection);
        /// <inheritdoc/>
        public bool Equals(SteamConnection other)
        {
            if (other is null)
                return false;

            if (ReferenceEquals(this, other))
                return true;

            return SteamNetConnection.Equals(other.SteamNetConnection);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return -721414014 + EqualityComparer<HSteamNetConnection>.Default.GetHashCode(SteamNetConnection);
        }

        public static bool operator ==(SteamConnection left, SteamConnection right)
        {
            if (left is null)
            {
                if (right is null)
                    return true;

                return false; // Only the left side is null
            }

            // Equals handles case of null on right side
            return left.Equals(right);
        }

        public static bool operator !=(SteamConnection left, SteamConnection right) => !(left == right);
    }
}
