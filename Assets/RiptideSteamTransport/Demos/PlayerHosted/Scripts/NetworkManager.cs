
// This file is provided under The MIT License as part of RiptideSteamTransport.
// Copyright (c) 2021 Tom Weiland
// For additional information please see the included LICENSE.md file or view it on GitHub: https://github.com/tom-weiland/RiptideSteamTransport/blob/main/LICENSE.md

using RiptideNetworking.Transports.SteamTransport;
using RiptideNetworking.Utils;
using System;
using UnityEngine;

namespace RiptideNetworking.Demos.SteamTransport.PlayerHosted
{
    public enum ServerToClientId : ushort
    {
        spawnPlayer = 1,
        playerMovement,
    }
    public enum ClientToServerId : ushort
    {
        playerName = 1,
        playerInput,
    }

    public class NetworkManager : MonoBehaviour
    {
        public const byte PlayerHostedDemoMessageHandlerGroupId = 255;

        private static NetworkManager _singleton;
        internal static NetworkManager Singleton
        {
            get => _singleton;
            private set
            {
                if (_singleton == null)
                    _singleton = value;
                else if (_singleton != value)
                {
                    Debug.Log($"{nameof(NetworkManager)} instance already exists, destroying object!");
                    Destroy(value);
                }
            }
        }

        [SerializeField] private GameObject serverPlayerPrefab;
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private GameObject localPlayerPrefab;

        public GameObject ServerPlayerPrefab => serverPlayerPrefab;
        public GameObject PlayerPrefab => playerPrefab;
        public GameObject LocalPlayerPrefab => localPlayerPrefab;
        
        internal Server Server { get; private set; }
        internal Client Client { get; private set; }

        private void Awake()
        {
            Singleton = this;
        }

        private void Start()
        {
            if (!SteamManager.Initialized)
            {
                Debug.LogError("Steam is not initialized!");
                return;
            }

#if UNITY_EDITOR
            RiptideLogger.Initialize(Debug.Log, Debug.Log, Debug.LogWarning, Debug.LogError, false);
#else
            RiptideLogger.Initialize(Debug.Log, true);
#endif

            SteamServer steamServer = new SteamServer();
            Server = new Server(steamServer);
            Server.ClientConnected += NewPlayerConnected;
            Server.ClientDisconnected += ServerPlayerLeft;

            Client = new Client(new SteamClient(steamServer));
            Client.Connected += DidConnect;
            Client.ConnectionFailed += FailedToConnect;
            Client.ClientDisconnected += ClientPlayerLeft;
            Client.Disconnected += DidDisconnect;
        }

        private void FixedUpdate()
        {
            if (Server.IsRunning)
                Server.Tick();

            if (Client.IsConnected)
                Client.Tick();
        }

        private void OnApplicationQuit()
        {
            StopServer();
            Server.ClientConnected -= NewPlayerConnected;
            Server.ClientDisconnected -= ServerPlayerLeft;

            DisconnectClient();
            Client.Connected -= DidConnect;
            Client.ConnectionFailed -= FailedToConnect;
            Client.ClientDisconnected -= ClientPlayerLeft;
            Client.Disconnected -= DidDisconnect;
        }

        internal void StopServer()
        {
            Server.Stop();
            foreach (ServerPlayer player in ServerPlayer.List.Values)
                Destroy(player.gameObject);
        }

        internal void DisconnectClient()
        {
            Client.Disconnect();
            foreach (ClientPlayer player in ClientPlayer.list.Values)
                Destroy(player.gameObject);
        }

        private void NewPlayerConnected(object sender, ServerClientConnectedEventArgs e)
        {
            foreach (ServerPlayer player in ServerPlayer.List.Values)
            {
                if (player.Id != e.Client.Id)
                    player.SendSpawn(e.Client.Id);
            }
        }

        private void ServerPlayerLeft(object sender, ClientDisconnectedEventArgs e)
        {
            Destroy(ServerPlayer.List[e.Id].gameObject);
        }

        private void DidConnect(object sender, EventArgs e)
        {
            Message message = Message.Create(MessageSendMode.reliable, (ushort)ClientToServerId.playerName);
            message.Add(Steamworks.SteamFriends.GetPersonaName());
            Client.Send(message);
        }

        private void FailedToConnect(object sender, EventArgs e)
        {
            UIManager.Singleton.BackToMain();
        }

        private void ClientPlayerLeft(object sender, ClientDisconnectedEventArgs e)
        {
            Destroy(ClientPlayer.list[e.Id].gameObject);
        }

        private void DidDisconnect(object sender, EventArgs e)
        {
            foreach (ClientPlayer player in ClientPlayer.list.Values)
                Destroy(player.gameObject);

            ClientPlayer.list.Clear();

            UIManager.Singleton.BackToMain();
        }
    }
}