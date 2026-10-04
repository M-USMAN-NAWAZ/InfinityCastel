using UnityEngine;

namespace StarterAssets
{
    public class UICanvasControllerInput : MonoBehaviour
    {

        [Header("Output")]
        public StarterAssetsInputs starterAssetsInputs;

        public void VirtualMoveInput(Vector2 virtualMoveDirection)
        {
            if (starterAssetsInputs != null) starterAssetsInputs.MoveInput(virtualMoveDirection);
        }

        public void VirtualLookInput(Vector2 virtualLookDirection)
        {
            if (starterAssetsInputs != null) starterAssetsInputs.LookInput(virtualLookDirection);
        }

        public void VirtualJumpInput(bool virtualJumpState)
        {
            if (starterAssetsInputs != null) starterAssetsInputs.JumpInput(virtualJumpState);
        }

        public void VirtualSprintInput(bool virtualSprintState)
        {
            if (starterAssetsInputs != null) starterAssetsInputs.SprintInput(virtualSprintState);
        }
        
    }

}
