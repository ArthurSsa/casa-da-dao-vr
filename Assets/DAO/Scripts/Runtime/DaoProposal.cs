using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

namespace DAO
{
    [Serializable]
    public class DaoOption
    {
        public string label = "Opção";
        public Color color = Color.white;
        [Tooltip("O que o 'contrato' executa se esta opção vencer. Ex.: DaoPlayerScale.Shrink().")]
        public UnityEvent onWin = new UnityEvent();
    }

    /// <summary>Como a DAO converte tokens de governança em votos.</summary>
    public enum DaoVotingCurve
    {
        [Tooltip("votos = tokens. Cada token pesa igual.")]
        Linear,
        [Tooltip("votos = 2^tokens - 1. Quem acumula tokens ganha poder desproporcional (a 'baleia' manda).")]
        Exponencial,
        [Tooltip("votos = log2(tokens + 1). Cada token a mais vale menos: protege quem tem poucos tokens.")]
        ExponencialInvertida
    }

    [Serializable]
    public class DaoMember
    {
        public string name = "Membro";
        [Min(0)] public int tokens = 1;
        [Tooltip("Índice da opção em que este membro vota.")]
        public int option;
    }

    /// <summary>
    /// Uma proposta da DAO: o visitante vota (Poke ou clique) com o poder de voto dos tokens da carteira,
    /// os membros simulados votam em seguida, a contagem aparece no painel e a opção vencedora é executada
    /// automaticamente (como um smart contract). Empate: vence a opção do visitante.
    /// </summary>
    public class DaoProposal : MonoBehaviour
    {
        [Header("Proposta")]
        public string title = "Proposta #1";
        [TextArea(2, 4)] public string description = "O que a DAO deve fazer com esta sala?";
        public List<DaoOption> options = new List<DaoOption>
        {
            new DaoOption { label = "A", color = new Color(0.2f, 0.6f, 1f) },
            new DaoOption { label = "B", color = new Color(1f, 0.5f, 0.2f) },
        };

        [Header("Governança")]
        public DaoVotingCurve curve = DaoVotingCurve.Linear;
        public List<DaoMember> members = new List<DaoMember>();
        [Min(0f)] public float secondsPerMemberVote = 0.6f;
        [Tooltip("Se falso, cada carteira vota uma vez só (o voto é imutável). Use ResetProposal para reabrir.")]
        public bool allowRevote = false;

        [Header("Textos (opcional, objetos com DaoText)")]
        public DaoText titleText;
        public DaoText curveText;
        public DaoText statusText;
        public DaoText tallyText;
        public DaoText membersText;

        [Header("Eventos")]
        public UnityEvent<int> onVoteCast = new UnityEvent<int>();
        public UnityEvent<int> onExecuted = new UnityEvent<int>();
        public UnityEvent onReset = new UnityEvent();

        public bool HasVoted { get; private set; }
        public bool Executed { get; private set; }
        public int Winner { get; private set; } = -1;
        public float[] Tally => tally;

        float[] tally;
        int membersCounted;
        int visitorChoice = -1;
        int visitorTokens;
        Coroutine counting;

        // ---------- curvas ----------

        public static float VotePower(DaoVotingCurve c, int tokens)
        {
            tokens = Mathf.Max(0, tokens);
            switch (c)
            {
                case DaoVotingCurve.Exponencial: return Mathf.Pow(2f, tokens) - 1f;
                case DaoVotingCurve.ExponencialInvertida: return Mathf.Log(tokens + 1f, 2f);
                default: return tokens;
            }
        }

        public float VotePower(int tokens) => VotePower(curve, tokens);

        public static string CurveName(DaoVotingCurve c)
        {
            switch (c)
            {
                case DaoVotingCurve.Exponencial: return "DAO EXPONENCIAL";
                case DaoVotingCurve.ExponencialInvertida: return "DAO EXPONENCIAL INVERTIDA (logarítmica)";
                default: return "DAO LINEAR";
            }
        }

        public static string CurveShortName(DaoVotingCurve c)
        {
            switch (c)
            {
                case DaoVotingCurve.Exponencial: return "exponencial";
                case DaoVotingCurve.ExponencialInvertida: return "exponencial invertida";
                default: return "linear";
            }
        }

        public static string CurveFormula(DaoVotingCurve c)
        {
            switch (c)
            {
                case DaoVotingCurve.Exponencial: return "votos = 2^tokens - 1";
                case DaoVotingCurve.ExponencialInvertida: return "votos = log2(tokens + 1)";
                default: return "votos = tokens (1 token = 1 voto)";
            }
        }

        public static string CurveExamples(DaoVotingCurve c)
        {
            var sb = new StringBuilder();
            foreach (int t in new[] { 1, 3, 7, 15 })
            {
                if (sb.Length > 0) sb.Append("   ");
                sb.Append(t).Append(" tk → ").Append(DaoLedger.FormatVotes(VotePower(c, t)));
            }
            return sb.ToString();
        }

        public static string CurveLesson(DaoVotingCurve c)
        {
            switch (c)
            {
                case DaoVotingCurve.Exponencial: return "Quem acumula tokens manda: a baleia decide sozinha.";
                case DaoVotingCurve.ExponencialInvertida: return "Cada token a mais vale menos: muitos pequenos vencem a baleia.";
                default: return "Todo token vale igual: poder proporcional ao que se tem.";
            }
        }

        // ---------- ciclo ----------

        void Start()
        {
            ResetTally();
            RefreshTexts("Aperte um botão para votar.");
            if (DaoLedger.Instance != null) DaoLedger.Instance.onTokensChanged.AddListener(_ => { if (!HasVoted) RefreshTexts(null); });
        }

        public void VoteA() => Vote(0);
        public void VoteB() => Vote(1);
        public void VoteC() => Vote(2);

        public void Vote(int optionIndex)
        {
            if (optionIndex < 0 || optionIndex >= options.Count) return;
            if (counting != null) return;
            if (HasVoted && !allowRevote)
            {
                RefreshTexts("Esta carteira já votou. O voto é imutável.\nReinicie o jogo para votar de novo.");
                return;
            }
            if (Executed) ResetTally();
            counting = StartCoroutine(Count(optionIndex));
        }

        public void ResetProposal()
        {
            if (counting != null) StopCoroutine(counting);
            counting = null;
            HasVoted = false;
            Executed = false;
            Winner = -1;
            visitorChoice = -1;
            ResetTally();
            onReset.Invoke();
            RefreshTexts("Votação reaberta. Aperte um botão para votar.");
        }

        void ResetTally()
        {
            tally = new float[options.Count];
            membersCounted = 0;
        }

        IEnumerator Count(int choice)
        {
            HasVoted = true;
            visitorChoice = choice;
            visitorTokens = DaoLedger.Instance != null ? DaoLedger.Instance.Tokens : 1;
            float power = VotePower(visitorTokens);
            tally[choice] += power;
            string tx = DaoLedger.Instance != null ? DaoLedger.Instance.RecordVote(this, choice, visitorTokens, power) : "";
            onVoteCast.Invoke(choice);
            RefreshTexts($"Seu voto em \"{options[choice].label}\": {visitorTokens} token(s) → {DaoLedger.FormatVotes(power)} voto(s).\n{tx}");

            // Membros votando um a um, para o visitante ver a curva agir.
            for (int i = 0; i < members.Count; i++)
            {
                if (secondsPerMemberVote > 0f) yield return new WaitForSeconds(secondsPerMemberVote);
                var m = members[i];
                if (m.option >= 0 && m.option < tally.Length) tally[m.option] += VotePower(m.tokens);
                membersCounted = i + 1;
                RefreshTexts($"{m.name} votou em \"{options[Mathf.Clamp(m.option, 0, options.Count - 1)].label}\" com {m.tokens} token(s).");
            }
            if (secondsPerMemberVote > 0f) yield return new WaitForSeconds(0.6f);

            int best = choice; // empate: vence a opção do visitante
            for (int i = 0; i < tally.Length; i++)
                if (tally[i] > tally[best] + 1e-4f) best = i;

            Winner = best;
            Executed = true;
            counting = null;
            string verdict = best == choice
                ? $"Aprovado: \"{options[best].label}\". Seu voto ajudou a decidir."
                : $"Aprovado: \"{options[best].label}\". Você perdeu: {CurveLesson(curve)}";
            RefreshTexts(verdict + "\nO contrato executou sozinho, sem chefe.");

            DaoLedger.Instance?.RecordExecution(this, best);
            options[best].onWin.Invoke();
            onExecuted.Invoke(best);
        }

        // ---------- textos ----------

        void RefreshTexts(string status)
        {
            DaoText.Set(titleText, $"{title}\n{description}");
            DaoText.Set(curveText, $"{CurveName(curve)}\n{CurveFormula(curve)}\n{CurveExamples(curve)}");
            if (status != null) DaoText.Set(statusText, status);
            else if (!HasVoted)
            {
                int t = DaoLedger.Instance != null ? DaoLedger.Instance.Tokens : 1;
                DaoText.Set(statusText, $"Seu poder aqui: {t} token(s) → {DaoLedger.FormatVotes(VotePower(t))} voto(s).\nPegue moedas douradas para votar mais forte!");
            }

            if (tallyText != null && tally != null)
            {
                float max = 1f;
                foreach (var v in tally) max = Mathf.Max(max, v);
                var sb = new StringBuilder("PLACAR\n");
                for (int i = 0; i < options.Count; i++)
                {
                    int bars = Mathf.RoundToInt(12f * tally[i] / max);
                    sb.Append(options[i].label).Append("  ").Append(new string('■', bars)).Append(bars == 0 ? "·" : "")
                      .Append("  ").Append(DaoLedger.FormatVotes(tally[i])).Append(" votos");
                    if (i < options.Count - 1) sb.Append('\n');
                }
                tallyText.Set(sb.ToString());
            }

            if (membersText != null)
            {
                var sb = new StringBuilder("MEMBROS DA DAO\n");
                int t = HasVoted ? visitorTokens : (DaoLedger.Instance != null ? DaoLedger.Instance.Tokens : 1);
                sb.Append(Line("Você", t, visitorChoice, HasVoted)).Append('\n');
                for (int i = 0; i < members.Count; i++)
                {
                    var m = members[i];
                    bool voted = HasVoted && i < membersCounted;
                    sb.Append(Line(m.name, m.tokens, voted ? m.option : -1, voted));
                    if (!voted && !HasVoted) sb.Append($"  (prefere {options[Mathf.Clamp(m.option, 0, options.Count - 1)].label})");
                    sb.Append('\n');
                }
                membersText.Set(sb.ToString().TrimEnd('\n'));
            }
        }

        string Line(string who, int tokens, int option, bool voted)
        {
            string dots = tokens <= 10 ? new string('●', tokens) : new string('●', 10) + "+";
            string votes = DaoLedger.FormatVotes(VotePower(tokens));
            string vote = voted && option >= 0 ? $"  | votou {options[option].label}" : "";
            return $"{who}: {dots} {tokens} tk → {votes} voto(s){vote}";
        }
    }
}
