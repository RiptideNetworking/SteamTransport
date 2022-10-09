using UnityEngine;

namespace Riptide.Demos.Steam.PlayerHosted
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovement : MonoBehaviour
    {
        [SerializeField] private ServerPlayer player;
        [SerializeField] private CharacterController controller;
        [SerializeField] private float gravity;
        [SerializeField] private float moveSpeed;
        [SerializeField] private float jumpSpeed;

        public bool[] Inputs { get; set; }
        private float yVelocity;

        private void OnValidate()
        {
            if (controller == null)
                controller = GetComponent<CharacterController>();

            if (player == null)
                player = GetComponent<ServerPlayer>();
        }

        private void Start()
        {
            gravity *= Time.fixedDeltaTime * Time.fixedDeltaTime;
            moveSpeed *= Time.fixedDeltaTime;
            jumpSpeed *= Time.fixedDeltaTime;

            Inputs = new bool[5];
        }

        private void FixedUpdate()
        {
            Vector2 inputDirection = Vector2.zero;
            if (Inputs[0])
                inputDirection.y += 1;

            if (Inputs[1])
                inputDirection.y -= 1;

            if (Inputs[2])
                inputDirection.x -= 1;

            if (Inputs[3])
                inputDirection.x += 1;

            Move(inputDirection);
        }

        private void Move(Vector2 inputDirection)
        {
            Vector3 moveDirection = transform.right * inputDirection.x + transform.forward * inputDirection.y;
            moveDirection *= moveSpeed;

            if (controller.isGrounded)
            {
                yVelocity = 0f;
                if (Inputs[4])
                    yVelocity = jumpSpeed;
            }
            yVelocity += gravity;

            moveDirection.y = yVelocity;
            controller.Move(moveDirection);

            SendMovement();
        }

        #region Messages
        private void SendMovement()
        {
            Message message = Message.Create(MessageSendMode.Unreliable, ServerToClientId.PlayerMovement);
            message.AddUShort(player.Id);
            message.AddVector3(transform.position);
            message.AddVector3(transform.forward);
            NetworkManager.Singleton.Server.SendToAll(message);
        }
        #endregion
    }
}
