using Steamworks;
using UnityEngine;

namespace Riptide.Demos.Steam.PlayerHosted
{
    public class LobbyManager : MonoBehaviour
    {
        private static LobbyManager _singleton;
        internal static LobbyManager Singleton
        {
            get => _singleton;
            private set
            {
                if (_singleton == null)
                    _singleton = value;
                else if (_singleton != value)
                {
                    Debug.Log($"{nameof(LobbyManager)} instance already exists, destroying object!");
                    Destroy(value);
                }
            }
        }

        protected Callback<LobbyCreated_t> lobbyCreated;
        protected Callback<GameLobbyJoinRequested_t> gameLobbyJoinRequested;
        protected Callback<LobbyEnter_t> lobbyEnter;

        private const string HostAddressKey = "HostAddress";
        private CSteamID lobbyId;

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

            lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            gameLobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
            lobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
        }

        internal void CreateLobby()
        {
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 5);
        }

        private void OnLobbyCreated(LobbyCreated_t callback)
        {
            if (callback.m_eResult != EResult.k_EResultOK)
            {
                UIManager.Singleton.LobbyCreationFailed();
                return;
            }

            lobbyId = new CSteamID(callback.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(lobbyId, HostAddressKey, SteamUser.GetSteamID().ToString());
            UIManager.Singleton.LobbyCreationSucceeded(callback.m_ulSteamIDLobby);

            NetworkManager.Singleton.Server.Start(0, 5, NetworkManager.PlayerHostedDemoMessageHandlerGroupId);
            NetworkManager.Singleton.Client.Connect("127.0.0.1", messageHandlerGroupId: NetworkManager.PlayerHostedDemoMessageHandlerGroupId);
        }

        internal void JoinLobby(ulong lobbyId)
        {
            SteamMatchmaking.JoinLobby(new CSteamID(lobbyId));
        }

        private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t callback)
        {
            SteamMatchmaking.JoinLobby(callback.m_steamIDLobby);
        }

        private void OnLobbyEnter(LobbyEnter_t callback)
        {
            if (NetworkManager.Singleton.Server.IsRunning)
                return;

            lobbyId = new CSteamID(callback.m_ulSteamIDLobby);
            string hostAddress = SteamMatchmaking.GetLobbyData(lobbyId, HostAddressKey);

            NetworkManager.Singleton.Client.Connect(hostAddress, messageHandlerGroupId: NetworkManager.PlayerHostedDemoMessageHandlerGroupId);
            UIManager.Singleton.LobbyEntered();
        }

        internal void LeaveLobby()
        {
            NetworkManager.Singleton.StopServer();
            NetworkManager.Singleton.DisconnectClient();
            SteamMatchmaking.LeaveLobby(lobbyId);
        }
    }
}
