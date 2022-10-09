using UnityEngine;
using UnityEngine.UI;

namespace Riptide.Demos.Steam.PlayerHosted
{
    internal class PlayerUIManager : MonoBehaviour
    {
        [SerializeField] private Text usernameText;

        private void Update()
        {
            if (ClientPlayer.list.TryGetValue(NetworkManager.Singleton.Client.Id, out ClientPlayer player))
                transform.rotation = Quaternion.LookRotation(transform.position - player.transform.position);
        }

        internal void SetName(string _name)
        {
            usernameText.text = _name;
        }
    }
}
