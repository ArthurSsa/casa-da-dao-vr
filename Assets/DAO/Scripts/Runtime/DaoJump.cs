using UnityEngine;
#if DAO_HAS_META
using Oculus.Interaction.Locomotion;
#endif

namespace DAO
{
    /// <summary>
    /// Botões do controle do Quest:
    ///   A (direito) = fecha o painel de informações aberto; sem painel aberto, pula (locomotor da Meta).
    ///   B (direito) ou Y (esquerdo) = abre/fecha o menu de mapas.
    /// No modo desktop: Espaço pula e M abre os mapas.
    /// </summary>
    public class DaoJump : MonoBehaviour
    {
        bool prevA, prevB, prevY;

        static bool Button(UnityEngine.XR.XRNode node, UnityEngine.XR.InputFeatureUsage<bool> usage)
        {
            var d = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
            return d.isValid && d.TryGetFeatureValue(usage, out bool v) && v;
        }

        void Update()
        {
            bool a = Button(UnityEngine.XR.XRNode.RightHand, UnityEngine.XR.CommonUsages.primaryButton);
            bool b = Button(UnityEngine.XR.XRNode.RightHand, UnityEngine.XR.CommonUsages.secondaryButton);
            bool y = Button(UnityEngine.XR.XRNode.LeftHand, UnityEngine.XR.CommonUsages.secondaryButton);

            if (a && !prevA)
            {
                var info = DaoRoomManager.Instance?.CurrentRoom?.infoPopup;
                if (info != null && info.IsOpen) info.Hide();
                else Jump();
            }
            if ((b && !prevB) || (y && !prevY)) FindFirstObjectByType<DaoMapMenu>()?.Toggle();
            prevA = a; prevB = b; prevY = y;
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
