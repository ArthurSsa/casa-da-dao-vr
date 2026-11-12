using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

namespace DAO
{
    /// <summary>
    /// "Blockchain" simulada do projeto: guarda cada voto com um hash de transação (fictício),
    /// guarda os tokens da carteira do visitante e dispara a conclusão quando todas as propostas forem executadas.
    /// </summary>
    public class DaoLedger : MonoBehaviour
    {
        public static DaoLedger Instance { get; private set; }

        public struct Entry
        {
            public string proposal;
            public string option;
            public int weight;
            public string txHash;
            public bool execution;
        }

        [Header("Carteira do visitante")]
        [Tooltip("Tokens de governança. Somam peso ao voto se a proposta usar 'Use Wallet Tokens'.")]
        [SerializeField] int tokens;
        public int Tokens => tokens;

        [Header("Conclusão")]
        [Tooltip("Quantas propostas diferentes precisam ser executadas para concluir o desafio.")]
        public int proposalsToComplete = 3;
        public UnityEvent onChallengeComplete = new UnityEvent();

        [Header("Textos (opcional, pode ter um painel por sala)")]
        public DaoText[] ledgerTexts = new DaoText[0];
        public DaoText[] walletTexts = new DaoText[0];
        [Min(1)] public int linesShown = 6;

        public UnityEvent<int> onTokensChanged = new UnityEvent<int>();

        readonly List<Entry> entries = new List<Entry>();
        readonly HashSet<DaoProposal> executed = new HashSet<DaoProposal>();
        bool completed;
        int block = 1024;

        public IReadOnlyList<Entry> Entries => entries;
        public int ExecutedCount => executed.Count;

        void Awake()
        {
            Instance = this;
        }

        void Start() => Refresh();

        public void AddTokens(int amount)
        {
            tokens = Mathf.Max(0, tokens + amount);
            onTokensChanged.Invoke(tokens);
            Refresh();
        }

        public void AddToken() => AddTokens(1);

        public string RecordVote(DaoProposal p, int option, int weight)
        {
            string hash = FakeHash($"{p.title}|{option}|{weight}|{Time.realtimeSinceStartup}|{entries.Count}");
            entries.Add(new Entry { proposal = p.title, option = p.options[option].label, weight = weight, txHash = hash });
            Refresh();
            return $"tx {Short(hash)} no bloco #{++block}";
        }

        public void RecordExecution(DaoProposal p, int winner)
        {
            entries.Add(new Entry
            {
                proposal = p.title,
                option = p.options[winner].label,
                txHash = FakeHash($"exec|{p.title}|{winner}|{entries.Count}"),
                execution = true
            });
            executed.Add(p);
            Refresh();
            if (!completed && executed.Count >= proposalsToComplete)
            {
                completed = true;
                onChallengeComplete.Invoke();
            }
        }

        void Refresh()
        {
            foreach (var w in walletTexts) DaoText.Set(w, $"Carteira: {tokens} token(s)\nPeso do voto: {1 + tokens}");
            if (ledgerTexts.Length == 0) return;
            var sb = new StringBuilder("LIVRO-RAZÃO DA DAO\n");
            int from = Mathf.Max(0, entries.Count - linesShown);
            for (int i = from; i < entries.Count; i++)
            {
                var e = entries[i];
                sb.Append(Short(e.txHash)).Append("  ");
                sb.Append(e.execution ? $"EXECUTADO {e.proposal}: {e.option}" : $"voto {e.option} (peso {e.weight})");
                sb.Append('\n');
            }
            sb.Append($"Propostas executadas: {executed.Count}/{proposalsToComplete}");
            string text = sb.ToString();
            foreach (var l in ledgerTexts) DaoText.Set(l, text);
        }

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
