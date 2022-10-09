using UnityEngine;
using UnityEngine.UI;

namespace Riptide.Demos.Steam.PlayerHosted
{
    public class UIManager : MonoBehaviour
    {
        private static UIManager _singleton;
        internal static UIManager Singleton
        {
            get => _singleton;
            private set
            {
                if (_singleton == null)
                    _singleton = value;
                else if (_singleton != value)
                {
                    Debug.Log($"{nameof(UIManager)} instance already exists, destroying object!");
                    Destroy(value);
                }
            }
        }

        [SerializeField] private GameObject mainMenu;
        [SerializeField] private GameObject lobbyMenu;
        [SerializeField] private InputField roomIdField;
        [SerializeField] private InputField roomIdDisplayField;

        private void Awake()
        {
            Singleton = this;
        }

        public void HostClicked()
        {
            mainMenu.SetActive(false);

            LobbyManager.Singleton.CreateLobby();
        }

        internal void LobbyCreationFailed()
        {
            mainMenu.SetActive(true);
        }

        internal void LobbyCreationSucceeded(ulong lobbyId)
        {
            roomIdDisplayField.text = lobbyId.ToString();
            roomIdDisplayField.gameObject.SetActive(true);
            lobbyMenu.SetActive(true);
        }

        public void JoinClicked()
        {
            if (string.IsNullOrEmpty(roomIdField.text))
            {
                Debug.Log("A room ID is required to join!");
                return;
            }

            LobbyManager.Singleton.JoinLobby(ulong.Parse(roomIdField.text));
            mainMenu.SetActive(false);
        }

        internal void LobbyEntered()
        {
            roomIdDisplayField.gameObject.SetActive(false);
            lobbyMenu.SetActive(true);
        }

        public void LeaveClicked()
        {
            LobbyManager.Singleton.LeaveLobby();
            BackToMain();
        }

        internal void BackToMain()
        {
            mainMenu.SetActive(true);
            lobbyMenu.SetActive(false);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        internal void UpdateUIVisibility()
        {
            if (Cursor.lockState == CursorLockMode.None)
                lobbyMenu.SetActive(true);
            else
                lobbyMenu.SetActive(false);
        }
    }
}
