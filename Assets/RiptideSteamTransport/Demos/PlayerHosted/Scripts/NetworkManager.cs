using Riptide.Transports.Steam;
using Riptide.Utils;
using System;
using UnityEngine;

namespace Riptide.Demos.Steam.PlayerHosted
{
    public enum ServerToClientId : ushort
    {
        SpawnPlayer = 1,
        PlayerMovement,
    }
    public enum ClientToServerId : ushort
    {
        PlayerName = 1,
        PlayerInput,
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
                Server.Update();

            Client.Update();
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

        private void NewPlayerConnected(object sender, ServerConnectedEventArgs e)
        {
            foreach (ServerPlayer player in ServerPlayer.List.Values)
            {
                if (player.Id != e.Client.Id)
                    player.SendSpawn(e.Client.Id);
            }
        }

        private void ServerPlayerLeft(object sender, ServerDisconnectedEventArgs e)
        {
            Destroy(ServerPlayer.List[e.Client.Id].gameObject);
        }

        private void DidConnect(object sender, EventArgs e)
        {
            Message message = Message.Create(MessageSendMode.Reliable, ClientToServerId.PlayerName);
            message.AddString(Steamworks.SteamFriends.GetPersonaName());
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
