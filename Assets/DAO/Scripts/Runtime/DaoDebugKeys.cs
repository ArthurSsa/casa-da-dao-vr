using UnityEngine;
#if ENABLE_INPUT_SYSTEM && DAO_HAS_INPUTSYSTEM
using UnityEngine.InputSystem;
#endif

namespace DAO
{
    /// <summary>
    /// Atalhos de teclado para testar no Editor/Meta XR Simulator sem headset.
    /// 1/2/3 = ir para a sala · Z/X = votar A/B na proposta da sala atual · R = reabrir votação · T = +1 token · 0 = escala normal
    /// </summary>
    public class DaoDebugKeys : MonoBehaviour
    {
        public bool onlyInEditor = true;

        void Update()
        {
            if (onlyInEditor && !Application.isEditor) return;
            var mgr = DaoRoomManager.Instance;
            if (mgr == null) return;

            if (Pressed(KeyCode.Alpha1)) mgr.GoToRoom(0);
            if (Pressed(KeyCode.Alpha2)) mgr.GoToRoom(1);
            if (Pressed(KeyCode.Alpha3)) mgr.GoToRoom(2);

            var p = mgr.CurrentRoom?.proposal;
            if (p != null)
            {
                if (Pressed(KeyCode.Z)) p.Vote(0);
                if (Pressed(KeyCode.X)) p.Vote(1);
                if (Pressed(KeyCode.R)) p.ResetProposal();
            }
            if (Pressed(KeyCode.T) && DaoLedger.Instance != null) DaoLedger.Instance.AddToken();
            if (Pressed(KeyCode.Alpha0))
            {
                var s = FindFirstObjectByType<DaoPlayerScale>();
                if (s != null) s.ResetScale();
            }
        }

        static bool Pressed(KeyCode k)
        {
#if ENABLE_INPUT_SYSTEM && DAO_HAS_INPUTSYSTEM
            var kb = Keyboard.current;
            if (kb == null) return false;
            Key key;
            switch (k)
            {
                case KeyCode.Alpha0: key = Key.Digit0; break;
                case KeyCode.Alpha1: key = Key.Digit1; break;
                case KeyCode.Alpha2: key = Key.Digit2; break;
                case KeyCode.Alpha3: key = Key.Digit3; break;
                case KeyCode.Z: key = Key.Z; break;
                case KeyCode.X: key = Key.X; break;
                case KeyCode.R: key = Key.R; break;
                case KeyCode.T: key = Key.T; break;
                default: return false;
            }
            return kb[key].wasPressedThisFrame;
#else
            return Input.GetKeyDown(k);
#endif
        }
    }
}
