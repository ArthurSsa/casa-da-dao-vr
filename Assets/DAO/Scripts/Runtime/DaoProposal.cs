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

    public enum DaoSimulation
    {
        [Tooltip("Os membros simulados empatam: o voto do visitante decide. Ótimo para demo.")]
        Empate,
        [Tooltip("Cada membro simulado vota aleatoriamente. Pode vencer a opção que o visitante NÃO escolheu.")]
        Aleatorio,
        [Tooltip("Só o visitante vota.")]
        SemSimulacao
    }

    /// <summary>
    /// Uma proposta da DAO: o visitante vota (Poke), os membros simulados votam em seguida,
    /// a contagem aparece no painel e a opção vencedora é executada automaticamente (como um smart contract).
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

        [Header("Membros simulados da DAO")]
        public DaoSimulation simulation = DaoSimulation.Empate;
        [Min(0)] public int simulatedMembers = 8;
        [Tooltip("Peso base do voto do visitante (1 carteira = 1 voto).")]
        [Min(1)] public int visitorWeight = 1;
        [Tooltip("Soma os tokens da carteira (DaoLedger.Tokens) ao peso do voto: governança por token.")]
        public bool useWalletTokens = true;
        [Min(0f)] public float secondsPerSimulatedVote = 0.25f;
        [Tooltip("Se falso, cada carteira vota uma vez só (o voto é imutável). Use ResetProposal para reabrir.")]
        public bool allowRevote = false;

        [Header("Textos (opcional, objetos com DaoText)")]
        public DaoText titleText;
        public DaoText statusText;
        public DaoText tallyText;

        [Header("Eventos")]
        public UnityEvent<int> onVoteCast = new UnityEvent<int>();
        public UnityEvent<int> onExecuted = new UnityEvent<int>();
        public UnityEvent onReset = new UnityEvent();

        public bool HasVoted { get; private set; }
        public bool Executed { get; private set; }
        public int Winner { get; private set; } = -1;
        public int[] Tally => tally;

        int[] tally;
        Coroutine counting;

        void Start()
        {
            ResetTally();
            RefreshTexts("Aperte um botão para votar.");
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
                RefreshTexts("Esta carteira já votou. O voto é imutável.");
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
            ResetTally();
            onReset.Invoke();
            RefreshTexts("Votação reaberta. Aperte um botão para votar.");
        }

        void ResetTally()
        {
            tally = new int[options.Count];
        }

        IEnumerator Count(int choice)
        {
            HasVoted = true;
            int weight = visitorWeight;
            if (useWalletTokens && DaoLedger.Instance != null) weight += DaoLedger.Instance.Tokens;

            tally[choice] += weight;
            string tx = DaoLedger.Instance != null ? DaoLedger.Instance.RecordVote(this, choice, weight) : "";
            onVoteCast.Invoke(choice);
            RefreshTexts($"Seu voto em \"{options[choice].label}\" foi registrado (peso {weight}).\n{tx}");

            // Membros simulados chegando um a um, em ordem embaralhada.
            var queue = BuildSimulatedVotes();
            foreach (int v in queue)
            {
                if (secondsPerSimulatedVote > 0f) yield return new WaitForSeconds(secondsPerSimulatedVote);
                tally[v]++;
                RefreshTexts("Contando votos dos membros...");
            }
            if (secondsPerSimulatedVote > 0f) yield return new WaitForSeconds(0.5f);

            // Vencedor: maior contagem; empate é decidido a favor do visitante.
            int best = choice;
            for (int i = 0; i < tally.Length; i++)
                if (tally[i] > tally[best]) best = i;

            Winner = best;
            Executed = true;
            counting = null;
            RefreshTexts($"Aprovado: \"{options[best].label}\".\nO contrato executou sozinho, sem chefe.");

            DaoLedger.Instance?.RecordExecution(this, best);
            options[best].onWin.Invoke();
            onExecuted.Invoke(best);
        }

        List<int> BuildSimulatedVotes()
        {
            var votes = new List<int>();
            int n = options.Count;
            switch (simulation)
            {
                case DaoSimulation.Empate:
                    int each = simulatedMembers / n;
                    for (int o = 0; o < n; o++)
                        for (int k = 0; k < each; k++) votes.Add(o);
                    break;
                case DaoSimulation.Aleatorio:
                    for (int k = 0; k < simulatedMembers; k++) votes.Add(UnityEngine.Random.Range(0, n));
                    break;
            }
            for (int i = votes.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (votes[i], votes[j]) = (votes[j], votes[i]);
            }
            return votes;
        }

        void RefreshTexts(string status)
        {
            DaoText.Set(titleText, $"{title}\n{description}");
            DaoText.Set(statusText, status);
            if (tallyText != null && tally != null)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < options.Count; i++)
                {
                    if (i > 0) sb.Append("   x   ");
                    sb.Append(options[i].label).Append(": ").Append(tally[i]);
                }
                tallyText.Set(sb.ToString());
            }
        }
    }
}
