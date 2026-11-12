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
