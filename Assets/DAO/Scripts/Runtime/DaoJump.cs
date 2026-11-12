using UnityEngine;
#if DAO_HAS_META
using Oculus.Interaction.Locomotion;
#endif

namespace DAO
{
    /// <summary>
    /// Pulo: botão A do controle direito do Quest (pulo do locomotor da Meta) e Espaço no modo desktop.
    /// </summary>
    public class DaoJump : MonoBehaviour
    {
        bool previous;

        void Update()
        {
            var right = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            bool pressed = right.isValid && right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool a) && a;
            if (pressed && !previous) Jump();
            previous = pressed;
        }

        public void Jump()
        {
            var rig = DaoRoomManager.Instance != null ? DaoRoomManager.Instance.playerRig : null;
            if (rig == null) return;
            var desktop = FindFirstObjectByType<DaoDesktopMode>();
            if (desktop != null && desktop.Active) { desktop.Jump(); return; }
#if DAO_HAS_META
            var loco = rig.GetComponentInChildren<FirstPersonLocomotor>();
            if (loco != null && loco.isActiveAndEnabled) loco.Jump();
#endif
        }
    }
}
