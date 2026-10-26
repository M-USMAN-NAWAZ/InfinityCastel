using UnityEngine;

namespace StarterAssets
{
    // Animation events are delivered to the child Animator, not its player root.
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed class ThirdPersonAnimationEvents : MonoBehaviour
    {
        [SerializeField] private ThirdPersonController controller;
        public void Bind(ThirdPersonController player) => controller = player;
        private void Awake()
        {
            if (controller == null) controller = GetComponentInParent<ThirdPersonController>();
        }
        public void OnFootstep(AnimationEvent evt)
        {
            if (controller != null) controller.OnFootstep(evt);
        }
        public void OnLand(AnimationEvent evt)
        {
            if (controller != null) controller.OnLand(evt);
        }
    }
}
