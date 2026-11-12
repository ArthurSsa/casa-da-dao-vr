using UnityEngine;

namespace DAO
{
    /// <summary>
    /// HUD pequeno no canto inferior direito da visão: tokens, poder de voto na sala atual, placar e status.
    /// Fica preso à cabeça (objeto filho da câmera). O placar e o status vêm da proposta da sala atual.
    /// </summary>
    public class DaoHud : MonoBehaviour
    {
        [Tooltip("Raiz do HUD: fica escondida enquanto algum pop-up estiver aberto.")]
        public GameObject hudRoot;
        [Tooltip("Posição em relação aos olhos (x = direita, y = cima, z = frente). No Quest o campo de visão de cada olho é estreito: x ~0.15 já fica à direita.")]
        public Vector3 hudPosition = new Vector3(0.15f, 0.12f, 0.7f);
        [Tooltip("Tamanho do HUD (1 = original).")]
        public float hudScale = 1.4f;
        public DaoText walletText;
        [Tooltip("Mesmo DaoText usado como tallyText de todas as propostas.")]
        public DaoText tallyText;
        [Tooltip("Mesmo DaoText usado como statusText das propostas e do efeito de escala.")]
        public DaoText statusText;

        float next;

        void Start()
        {
            var mgr = DaoRoomManager.Instance;
            if (mgr != null) mgr.onRoomChanged.AddListener(_ => RefreshRoom());
            if (DaoLedger.Instance != null)
            {
                DaoLedger.Instance.onTokensChanged.AddListener(_ => RefreshWallet());
                DaoLedger.Instance.onReset.AddListener(RefreshRoom);
            }
            RefreshRoom();
        }

        public void RefreshRoom()
        {
            var p = DaoRoomManager.Instance?.CurrentRoom?.proposal;
            if (p != null) p.Refresh();
            RefreshWallet();
        }

        void Update()
        {
            if (hudRoot != null)
            {
                hudRoot.transform.localPosition = hudPosition;
                hudRoot.transform.localScale = Vector3.one * hudScale;
                bool popupOpen = false;
                foreach (var p in FindObjectsByType<DaoPanelPopup>(FindObjectsSortMode.None)) popupOpen |= p.IsOpen;
                var done = FindFirstObjectByType<DaoCompletionPopup>();
                if (done != null && done.popupRoot != null && done.popupRoot.activeSelf) popupOpen = true;
                if (hudRoot.activeSelf == popupOpen) hudRoot.SetActive(!popupOpen);
            }
            if (Time.time < next) return;
            next = Time.time + 0.5f;
            RefreshWallet();
        }

        public void RefreshWallet()
        {
            var mgr = DaoRoomManager.Instance;
            var p = mgr?.CurrentRoom?.proposal;
            int t = DaoLedger.Instance != null ? DaoLedger.Instance.Tokens : 1;
            string room = mgr?.CurrentRoom?.name ?? "-";
            string power = p != null ? $"{DaoLedger.FormatVotes(p.VotePower(t))} voto(s) · {DaoProposal.CurveShortName(p.curve)}" : "-";
            DaoText.Set(walletText, $"{room}   ●  {t} token(s)  →  {power}");
        }
    }
}
