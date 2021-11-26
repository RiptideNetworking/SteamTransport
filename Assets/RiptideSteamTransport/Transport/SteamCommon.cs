
// This file is provided under The MIT License as part of RiptideSteamTransport.
// Copyright (c) 2021 Tom Weiland
// For additional information please see the included LICENSE.md file or view it on GitHub: https://github.com/tom-weiland/RiptideSteamTransport/blob/main/LICENSE.md

using Steamworks;
using System;
using System.Runtime.InteropServices;

namespace RiptideNetworking.Transports.SteamTransport
{
    public class SteamCommon
    {
        /// <summary>Whether or not to output informational log messages. Error-related log messages ignore this setting.</summary>
        public bool ShouldOutputInfoLogs { get; set; } = true;
        /// <summary>The name to use when logging messages via <see cref="RiptideLogger"/>.</summary>
        public readonly string LogName;

        protected const int MaxMessages = 256;

        protected SteamCommon(string logName)
        {
            LogName = logName;
        }

        protected EResult SteamSend(Message message, HSteamNetConnection connection)
        {
            byte[] data = new byte[message.WrittenLength]; // TODO: do something about this allocation?
            Array.Copy(message.Bytes, data, data.Length);

            GCHandle pinnedArray = GCHandle.Alloc(data, GCHandleType.Pinned);
            IntPtr pData = pinnedArray.AddrOfPinnedObject();
            int sendFlag = message.SendMode == MessageSendMode.reliable ? Constants.k_nSteamNetworkingSend_Unreliable : Constants.k_nSteamNetworkingSend_Reliable;
            EResult res = SteamNetworkingSockets.SendMessageToConnection(connection, pData, (uint)data.Length, sendFlag, out long _);
            if (res != EResult.k_EResultOK)
                RiptideLogger.Log(LogName, $"Failed to send message: {res}");

            pinnedArray.Free();
            return res;
        }

        protected Message SteamProcessMessage(IntPtr ptrs, out HeaderType messageHeader)
        {
            SteamNetworkingMessage_t data = Marshal.PtrToStructure<SteamNetworkingMessage_t>(ptrs);
            
            Message message = Message.Create();
            if (data.m_cbSize > message.Bytes.Length)
            {
                RiptideLogger.Log(LogName, $"Can't fully handle {data.m_cbSize} bytes because it exceeds the maximum of {message.Bytes.Length}, message will contain incomplete data!");
                Marshal.Copy(data.m_pData, message.Bytes, 0, data.m_cbSize);
            }
            else
                Marshal.Copy(data.m_pData, message.Bytes, 0, data.m_cbSize);

            SteamNetworkingMessage_t.Release(ptrs);
            messageHeader = message.PrepareForUse((ushort)data.m_cbSize);
            return message;
        }
    }
}