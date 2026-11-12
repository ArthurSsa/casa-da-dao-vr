using UnityEngine;
using UnityEngine.Events;

namespace DAO
{
    /// <summary>
    /// Pop-up de finalização: aparece na frente do jogador quando as 3 propostas foram executadas,
    /// mostra o resumo das decisões e tem os botões "Reiniciar" (zera tudo e volta para a 1ª sala,
    /// para testar as outras opções) e "Continuar explorando".
    /// </summary>
    public class DaoCompletionPopup : MonoBehaviour
    {
        [Tooltip("Objeto do pop-up (fica escondido até a conclusão).")]
        public GameObject popupRoot;
        public DaoText summaryText;
        public float distance = 1.3f;
        public UnityEvent onRestart = new UnityEvent();

        void Start()
        {
            if (popupRoot != null) popupRoot.SetActive(false);
            if (DaoLedger.Instance != null) DaoLedger.Instance.onChallengeComplete.AddListener(Show);
        }

        public void Show()
        {
            if (popupRoot == null) return;
            var mgr = DaoRoomManager.Instance;
            Transform head = mgr != null ? mgr.head : (Camera.main != null ? Camera.main.transform : null);
            if (head != null)
            {
                Vector3 fwd = head.forward; fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                fwd.Normalize();
                popupRoot.transform.position = head.position + fwd * distance + Vector3.down * 0.15f;
                popupRoot.transform.rotation = Quaternion.LookRotation(fwd);
            }
            int tokens = DaoLedger.Instance != null ? DaoLedger.Instance.Tokens : 0;
            DaoText.Set(summaryText,
                "DESAFIO CONCLUÍDO!\nVocê governou as 3 salas da Casa da DAO.\n\n" +
                (DaoLedger.Instance != null ? DaoLedger.Instance.DecisionsSummary() : "") +
                $"\n\nTokens na carteira: {tokens}\n" +
                "Reinicie para votar nas outras opções\ne comparar as três curvas de governança.");
            popupRoot.SetActive(true);
        }

        public void Hide()
        {
            if (popupRoot != null) popupRoot.SetActive(false);
        }

        /// <summary>Zera votos, carteira, moedas, efeitos e objetos, e volta para a primeira sala.</summary>
        public void Restart()
        {
            Hide();
            foreach (var s in FindObjectsByType<DaoPlayerScale>(FindObjectsInactive.Include, FindObjectsSortMode.None)) s.ResetScaleInstant();
            foreach (var g in FindObjectsByType<DaoGravity>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { g.Restore(); g.ResetProps(); }
            foreach (var t in FindObjectsByType<DaoRoomTint>(FindObjectsInactive.Include, FindObjectsSortMode.None)) t.ResetTintInstant();
            foreach (var p in FindObjectsByType<DaoProposal>(FindObjectsInactive.Include, FindObjectsSortMode.None)) p.ResetProposal();
            foreach (var k in FindObjectsByType<DaoToken>(FindObjectsInactive.Include, FindObjectsSortMode.None)) k.ResetToken();
            DaoLedger.Instance?.ResetAll();
            foreach (var w in FindObjectsByType<DaoWalletPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None)) w.Refresh();
            DaoRoomManager.Instance?.Respawn(0);
            onRestart.Invoke();
            Debug.Log("[DAO] Jogo reiniciado.");
        }
    }
}
