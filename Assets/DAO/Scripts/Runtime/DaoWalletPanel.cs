using System.Text;
using UnityEngine;

namespace DAO
{
    /// <summary>
    /// Painel "SUA CARTEIRA" de uma sala: explica para que servem os tokens, mostra o saldo,
    /// o poder de voto que esse saldo dá NA CURVA DESTA SALA e quantas moedas ainda faltam pegar aqui.
    /// </summary>
    public class DaoWalletPanel : MonoBehaviour
    {
        public DaoProposal proposal;
        public DaoToken[] roomTokens = new DaoToken[0];
        public DaoText text;

        void Start()
        {
            if (DaoLedger.Instance != null)
            {
                DaoLedger.Instance.onTokensChanged.AddListener(_ => Refresh());
                DaoLedger.Instance.onReset.AddListener(Refresh);
            }
            Refresh();
        }

        void OnEnable() => Refresh();

        public void Refresh()
        {
            if (text == null || proposal == null) return;
            int t = DaoLedger.Instance != null ? DaoLedger.Instance.Tokens : 1;
            int left = 0;
            foreach (var tk in roomTokens) if (tk != null && !tk.Collected) left++;
            float now = proposal.VotePower(t);
            float next = proposal.VotePower(t + 1);

            var sb = new StringBuilder();
            sb.Append("SUA CARTEIRA\n");
            sb.Append(t <= 15 ? new string('●', t) : new string('●', 15) + "+").Append("  ").Append(t).Append(" token(s)\n");
            sb.Append("Poder de voto nesta sala: ").Append(DaoLedger.FormatVotes(now)).Append(" voto(s)\n");
            sb.Append("Com +1 token: ").Append(DaoLedger.FormatVotes(next)).Append(" voto(s)\n");
            sb.Append(left > 0 ? $"Moedas douradas nesta sala: faltam {left} de {roomTokens.Length}\n" : "Você pegou todas as moedas desta sala.\n");
            sb.Append("\nPOR QUE TOKENS?\n");
            sb.Append("Na DAO, token de governança = poder de voto.\n");
            sb.Append("Cada DAO decide como converter tokens em votos:\n");
            sb.Append(DaoProposal.CurveFormula(proposal.curve)).Append('\n');
            sb.Append(DaoProposal.CurveLesson(proposal.curve));
            text.Set(sb.ToString());
        }
    }
}
