using System.Collections.Generic;
using UnityEngine;

namespace Riptide.Demos.Steam.PlayerHosted
{
    [RequireComponent(typeof(PlayerMovement))]
    public class ServerPlayer : MonoBehaviour
    {
        public static Dictionary<ushort, ServerPlayer> List { get; private set; } = new Dictionary<ushort, ServerPlayer>();

        public ushort Id { get; private set; }
        public string Username { get; private set; }

        [SerializeField] private PlayerMovement movement;

        private void OnValidate()
        {
            if (movement == null)
                movement = GetComponent<PlayerMovement>();
        }

        public void SetForwardDirection(Vector3 forward)
        {
            forward.y = 0; // Keep the player upright
            transform.forward = forward;
        }

        private void OnDestroy()
        {
            List.Remove(Id);
        }

        public static void Spawn(ushort id, string username)
        {
            ServerPlayer player = Instantiate(NetworkManager.Singleton.ServerPlayerPrefab, new Vector3(0f, 1f, 0f), Quaternion.identity).GetComponent<ServerPlayer>();
            player.name = $"Server Player {id} ({(username == "" ? "Guest" : username)})";
            player.Id = id;
            player.Username = username;

            player.SendSpawn();
            List.Add(player.Id, player);
        }

        #region Messages
        /// <summary>Sends a player's info to the given client.</summary>
        /// <param name="toClient">The client to send the message to.</param>
        public void SendSpawn(ushort toClient)
        {
            NetworkManager.Singleton.Server.Send(GetSpawnData(Message.Create(MessageSendMode.Reliable, ServerToClientId.SpawnPlayer)), toClient);
        }
        /// <summary>Sends a player's info to all clients.</summary>
        private void SendSpawn()
        {
            NetworkManager.Singleton.Server.SendToAll(GetSpawnData(Message.Create(MessageSendMode.Reliable, ServerToClientId.SpawnPlayer)));
        }

        private Message GetSpawnData(Message message)
        {
            message.AddUShort(Id);
            message.AddString(Username);
            message.AddVector3(transform.position);
            return message;
        }

        [MessageHandler((ushort)ClientToServerId.PlayerName, NetworkManager.PlayerHostedDemoMessageHandlerGroupId)]
        private static void PlayerName(ushort fromClientId, Message message)
        {
            Spawn(fromClientId, message.GetString());
        }

        [MessageHandler((ushort)ClientToServerId.PlayerInput, NetworkManager.PlayerHostedDemoMessageHandlerGroupId)]
        private static void PlayerInput(ushort fromClientId, Message message)
        {
            ServerPlayer player = List[fromClientId];
            message.GetBools(5, player.movement.Inputs);
            player.SetForwardDirection(message.GetVector3());
        }
        #endregion
    }
}
