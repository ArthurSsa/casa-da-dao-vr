using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

namespace DAO
{
    /// <summary>
    /// "Blockchain" simulada do projeto: guarda cada voto com um hash de transação (fictício),
    /// guarda os tokens de governança da carteira do visitante e dispara a conclusão quando todas
    /// as propostas forem executadas. Tokens = poder de voto (cada sala converte tokens em votos com uma curva).
    /// </summary>
    public class DaoLedger : MonoBehaviour
    {
        public static DaoLedger Instance { get; private set; }

        public struct Entry
        {
            public string proposal;
            public string option;
            public int tokens;
            public float votes;
            public string curve;
            public string txHash;
            public bool execution;
        }

        [Header("Carteira do visitante")]
        [Tooltip("Tokens com que todo membro começa (1 token = você é membro da DAO).")]
        public int startTokens = 1;
        [SerializeField] int tokens = 1;
        public int Tokens => tokens;

        [Header("Conclusão")]
        [Tooltip("Quantas propostas diferentes precisam ser executadas para concluir o desafio.")]
        public int proposalsToComplete = 3;
        public UnityEvent onChallengeComplete = new UnityEvent();

        [Header("Textos (opcional, pode ter um painel por sala)")]
        public DaoText[] ledgerTexts = new DaoText[0];
        [Min(1)] public int linesShown = 6;

        public UnityEvent<int> onTokensChanged = new UnityEvent<int>();
        public UnityEvent onReset = new UnityEvent();

        readonly List<Entry> entries = new List<Entry>();
        readonly List<DaoProposal> executed = new List<DaoProposal>();
        bool completed;
        int block = 1024;

        public IReadOnlyList<Entry> Entries => entries;
        public int ExecutedCount => executed.Count;
        public bool Completed => completed;

        void Awake()
        {
            Instance = this;
            tokens = startTokens;
        }

        void Start() => Refresh();

        public void AddTokens(int amount)
        {
            tokens = Mathf.Max(0, tokens + amount);
            onTokensChanged.Invoke(tokens);
            Refresh();
        }

        public void AddToken() => AddTokens(1);

        public string RecordVote(DaoProposal p, int option, int voterTokens, float votes)
        {
            string hash = FakeHash($"{p.title}|{option}|{voterTokens}|{Time.realtimeSinceStartup}|{entries.Count}");
            entries.Add(new Entry
            {
                proposal = p.title, option = p.options[option].label, tokens = voterTokens, votes = votes,
                curve = DaoProposal.CurveShortName(p.curve), txHash = hash
            });
            Refresh();
            return $"tx {Short(hash)} gravada no bloco #{++block}";
        }

        public void RecordExecution(DaoProposal p, int winner)
        {
            entries.Add(new Entry
            {
                proposal = p.title,
                option = p.options[winner].label,
                curve = DaoProposal.CurveShortName(p.curve),
                txHash = FakeHash($"exec|{p.title}|{winner}|{entries.Count}"),
                execution = true
            });
            if (!executed.Contains(p)) executed.Add(p);
            Refresh();
            if (!completed && executed.Count >= proposalsToComplete)
            {
                completed = true;
                onChallengeComplete.Invoke();
            }
        }

        /// <summary>Resumo das decisões executadas, na ordem (para a tela final).</summary>
        public string DecisionsSummary()
        {
            var sb = new StringBuilder();
            foreach (var e in entries)
                if (e.execution) sb.Append("• ").Append(e.proposal).Append(": ").Append(e.option).Append("  (").Append(e.curve).Append(")\n");
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>Zera carteira, livro-razão e conclusão (reiniciar o jogo).</summary>
        public void ResetAll()
        {
            entries.Clear();
            executed.Clear();
            completed = false;
            tokens = startTokens;
            onTokensChanged.Invoke(tokens);
            onReset.Invoke();
            Refresh();
        }

        void Refresh()
        {
            if (ledgerTexts.Length == 0) return;
            var sb = new StringBuilder("LIVRO-RAZÃO DA DAO (blockchain simulada)\n");
            int from = Mathf.Max(0, entries.Count - linesShown);
            if (entries.Count == 0) sb.Append("Nenhuma transação ainda.\n");
            for (int i = from; i < entries.Count; i++)
            {
                var e = entries[i];
                sb.Append(Short(e.txHash)).Append("  ");
                sb.Append(e.execution
                    ? $"EXECUTADO {e.proposal}: {e.option}"
                    : $"voto \"{e.option}\" · {e.tokens} token(s) → {FormatVotes(e.votes)} voto(s)");
                sb.Append('\n');
            }
            sb.Append($"Propostas executadas: {executed.Count}/{proposalsToComplete}");
            string text = sb.ToString();
            foreach (var l in ledgerTexts) DaoText.Set(l, text);
        }

        public static string FormatVotes(float v) => Mathf.Approximately(v, Mathf.Round(v)) ? Mathf.RoundToInt(v).ToString() : v.ToString("0.0");

        static string Short(string h) => h.Substring(0, 8) + "…" + h.Substring(h.Length - 4);

        static string FakeHash(string s)
        {
            unchecked
            {
                ulong a = 1469598103934665603UL, b = 0x9E3779B97F4A7C15UL;
                foreach (char c in s)
                {
                    a = (a ^ c) * 1099511628211UL;
                    b = (b ^ (ulong)c) * 0xBF58476D1CE4E5B9UL + 0x94D049BB133111EBUL;
                }
                return "0x" + a.ToString("x16") + b.ToString("x16");
            }
        }
    }
}
