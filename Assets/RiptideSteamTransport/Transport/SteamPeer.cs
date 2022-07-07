// This file is provided under The MIT License as part of RiptideSteamTransport.
// Copyright (c) Tom Weiland
// For additional information please see the included LICENSE.md file or view it on GitHub:
// https://github.com/tom-weiland/RiptideSteamTransport/blob/main/LICENSE.md

using Steamworks;
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Riptide.Transports.Steam
{
    public class SteamPeer
    {
        /// <summary>The name to use when logging messages via <see cref="RiptideLogger"/>.</summary>
        public const string LogName = "STEAM";

        public event EventHandler<DataReceivedEventArgs> DataReceived;

        protected const int MaxMessages = 256;

        private readonly byte[] receiveBuffer;

        protected SteamPeer()
        {
            receiveBuffer = new byte[Message.MaxSize + sizeof(ushort)];
        }

        protected void Receive(SteamConnection fromConnection)
        {
            IntPtr[] ptrs = new IntPtr[MaxMessages]; // TODO: remove allocation?

            // TODO: consider using poll groups -> https://partner.steamgames.com/doc/api/ISteamNetworkingSockets#functions_poll_groups
            int messageCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(fromConnection.SteamNetConnection, ptrs, MaxMessages);
            if (messageCount > 0)
            {
                for (int i = 0; i < messageCount; i++)
                {
                    SteamNetworkingMessage_t data = Marshal.PtrToStructure<SteamNetworkingMessage_t>(ptrs[i]);

                    if (data.m_cbSize > 0)
                    {
                        int byteCount = data.m_cbSize;
                        if (data.m_cbSize > receiveBuffer.Length)
                        {
                            Debug.LogWarning($"{LogName}: Can't fully handle {data.m_cbSize} bytes because it exceeds the maximum of {receiveBuffer.Length}. Data will be incomplete!");
                            byteCount = receiveBuffer.Length;
                        }

                        Marshal.Copy(data.m_pData, receiveBuffer, 0, data.m_cbSize);
                        OnDataReceived(receiveBuffer, byteCount, fromConnection);
                    }
                }
            }
        }

        internal void Send(byte[] dataBuffer, int numBytes, HSteamNetConnection toConnection)
        {
            GCHandle handle = GCHandle.Alloc(dataBuffer, GCHandleType.Pinned);
            IntPtr pDataBuffer = handle.AddrOfPinnedObject();

            EResult result = SteamNetworkingSockets.SendMessageToConnection(toConnection, pDataBuffer, (uint)numBytes, Constants.k_nSteamNetworkingSend_Unreliable, out long _);
            if (result != EResult.k_EResultOK)
                Debug.LogWarning($"{LogName}: Failed to send {numBytes} bytes - {result}");

            handle.Free();
        }

        protected virtual void OnDataReceived(byte[] dataBuffer, int amount, SteamConnection fromConnection)
        {
            DataReceived?.Invoke(this, new DataReceivedEventArgs(dataBuffer, amount, fromConnection));
        }
    }
}
