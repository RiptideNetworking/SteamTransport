
// This file is provided under The MIT License as part of RiptideSteamTransport.
// Copyright (c) 2021 Tom Weiland
// For additional information please see the included LICENSE.md file or view it on GitHub: https://github.com/tom-weiland/RiptideSteamTransport/blob/main/LICENSE.md

using RiptideNetworking.Utils;
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
            byte[] data = new byte[message.WrittenLength]; // TODO: is this allocation even necessary?
            Array.Copy(message.Bytes, data, data.Length);

            GCHandle pinnedArray = GCHandle.Alloc(data, GCHandleType.Pinned);
            IntPtr pData = pinnedArray.AddrOfPinnedObject();
            int sendFlag = message.SendMode == MessageSendMode.reliable ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_Unreliable;
            EResult res = SteamNetworkingSockets.SendMessageToConnection(connection, pData, (uint)data.Length, sendFlag, out long _);
            if (res != EResult.k_EResultOK)
                RiptideLogger.Log(LogType.warning, LogName, $"Failed to send message: {res}");

            pinnedArray.Free();
            return res;
        }

        protected Message SteamProcessMessage(IntPtr ptrs, out HeaderType messageHeader)
        {
            SteamNetworkingMessage_t data = Marshal.PtrToStructure<SteamNetworkingMessage_t>(ptrs);
            
            Message message = MessageExtensionsTransports.CreateRaw();
            if (data.m_cbSize > message.Bytes.Length)
            {
                RiptideLogger.Log(LogType.warning, LogName, $"Can't fully handle {data.m_cbSize} bytes because it exceeds the maximum of {message.Bytes.Length}, message will contain incomplete data!");
                Marshal.Copy(data.m_pData, message.Bytes, 0, message.Bytes.Length);
                messageHeader = (HeaderType)message.Bytes[0];
                message.PrepareForUse(messageHeader, (ushort)message.Bytes.Length);
            }
            else
            {
                Marshal.Copy(data.m_pData, message.Bytes, 0, data.m_cbSize);
                messageHeader = (HeaderType)message.Bytes[0];
                message.PrepareForUse(messageHeader, (ushort)message.Bytes.Length);
            }

            SteamNetworkingMessage_t.Release(ptrs);
            return message;
        }
    }
}