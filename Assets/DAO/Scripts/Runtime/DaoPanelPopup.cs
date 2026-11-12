using UnityEngine;
using UnityEngine.Events;

namespace DAO
{
    /// <summary>
    /// Pop-up em VR: abre na frente do jogador, na altura dos olhos, e se reposiciona suavemente se ele
    /// virar muito a cabeça (não fica grudado no rosto, que causa enjoo). Feche com Hide (botão FECHAR).
    /// </summary>
    public class DaoPanelPopup : MonoBehaviour
    {
        [Tooltip("Objeto com o conteúdo do pop-up (ativado/desativado).")]
        public GameObject root;
        public float distance = 1.2f;
        public float heightOffset = -0.05f;
        [Tooltip("Se a cabeça virar mais que isso, o pop-up volta para a frente.")]
        public float followAngle = 40f;
        public bool showOnEnable;
        public UnityEvent onShow = new UnityEvent();
        public UnityEvent onHide = new UnityEvent();

        public bool IsOpen => root != null && root.activeSelf;
        Vector3 targetPos;
        Quaternion targetRot;

        void OnEnable() { if (showOnEnable) Show(); }

        Transform Head()
        {
            var mgr = DaoRoomManager.Instance;
            if (mgr != null && mgr.head != null) return mgr.head;
            return Camera.main != null ? Camera.main.transform : null;
        }

        public void Show()
        {
            if (root == null) return;
            root.SetActive(true);
            Snap(true);
            onShow.Invoke();
        }

        public void Hide()
        {
            if (root == null || !root.activeSelf) return;
            root.SetActive(false);
            onHide.Invoke();
        }

        public void Toggle() { if (IsOpen) Hide(); else Show(); }

        void Snap(bool instant)
        {
            var head = Head();
            if (head == null) return;
            Vector3 fwd = head.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = head.parent != null ? head.parent.forward : Vector3.forward;
            fwd.Normalize();
            targetPos = head.position + fwd * distance + Vector3.up * heightOffset;
            targetRot = Quaternion.LookRotation(fwd);
            if (instant) root.transform.SetPositionAndRotation(targetPos, targetRot);
        }

        void LateUpdate()
        {
            if (!IsOpen) return;
            var head = Head();
            if (head == null) return;
            Vector3 toPanel = root.transform.position - head.position;
            float dy = Mathf.Abs(targetPos.y - (head.position.y + heightOffset));
            Vector3 fwd = head.forward;
            toPanel.y = 0f; fwd.y = 0f;
            bool far = toPanel.magnitude > distance * 1.6f || toPanel.magnitude < distance * 0.5f || dy > 0.4f;
            if (far || Vector3.Angle(toPanel, fwd) > followAngle) Snap(far);
            root.transform.position = Vector3.Lerp(root.transform.position, targetPos, Time.deltaTime * 4f);
            root.transform.rotation = Quaternion.Slerp(root.transform.rotation, targetRot, Time.deltaTime * 4f);
        }
    }
}
