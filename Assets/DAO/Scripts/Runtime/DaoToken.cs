using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace DAO
{
    /// <summary>
    /// Moeda de token de governança. Gira e flutua; é coletada quando o jogador chega perto
    /// (cabeça a menos de <see cref="pickupRadius"/> na horizontal) ou quando é tocada/clicada
    /// (DaoButton no mesmo objeto chama Collect). Cada moeda soma 1 token na carteira (DaoLedger).
    /// </summary>
    public class DaoToken : MonoBehaviour
    {
        public int amount = 1;
        public float pickupRadius = 0.7f;
        public float spinSpeed = 90f;
        public float bobHeight = 0.05f;
        [Tooltip("Parte visual que gira (a moeda).")]
        public Transform visual;
        [Tooltip("Texto que sobe ao coletar (\"+1 token\"), opcional.")]
        public DaoText toast;
        public AudioClip collectSound;
        public UnityEvent onCollected = new UnityEvent();

        public bool Collected { get; private set; }

        Vector3 visualBase;
        Vector3 toastBase;

        void Awake()
        {
            if (visual == null) visual = transform;
            visualBase = visual.localPosition;
            if (toast != null) { toastBase = toast.transform.localPosition; toast.gameObject.SetActive(false); }
        }

        void Update()
        {
            if (Collected) return;
            visual.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
            visual.localPosition = visualBase + Vector3.up * Mathf.Sin(Time.time * 2f + transform.position.x) * bobHeight;

            var mgr = DaoRoomManager.Instance;
            if (mgr == null || mgr.head == null || mgr.IsTransitioning) return;
            Vector3 d = mgr.head.position - transform.position;
            float s = transform.lossyScale.x;
            if (new Vector2(d.x, d.z).magnitude < pickupRadius * Mathf.Max(1f, s) && d.y > -0.5f && d.y < 2.5f * Mathf.Max(1f, s))
                Collect();
        }

        public void Collect()
        {
            if (Collected) return;
            Collected = true;
            DaoLedger.Instance?.AddTokens(amount);
            if (collectSound != null) AudioSource.PlayClipAtPoint(collectSound, transform.position);
            visual.gameObject.SetActive(false);
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
            onCollected.Invoke();
            if (toast != null) StartCoroutine(ShowToast());
        }

        IEnumerator ShowToast()
        {
            int total = DaoLedger.Instance != null ? DaoLedger.Instance.Tokens : amount;
            toast.gameObject.SetActive(true);
            toast.Set($"+{amount} token!  Carteira: {total}");
            var mgr = DaoRoomManager.Instance;
            for (float t = 0f; t < 1.8f; t += Time.deltaTime)
            {
                toast.transform.localPosition = toastBase + Vector3.up * (t * 0.35f);
                if (mgr != null && mgr.head != null)
                {
                    Vector3 away = toast.transform.position - mgr.head.position; away.y = 0f;
                    if (away.sqrMagnitude > 1e-4f) toast.transform.rotation = Quaternion.LookRotation(away);
                }
                yield return null;
            }
            toast.gameObject.SetActive(false);
            toast.transform.localPosition = toastBase;
        }

        /// <summary>Volta a moeda para a sala (reiniciar o jogo).</summary>
        public void ResetToken()
        {
            StopAllCoroutines();
            Collected = false;
            visual.gameObject.SetActive(true);
            visual.localPosition = visualBase;
            foreach (var c in GetComponents<Collider>()) c.enabled = true;
            if (toast != null) { toast.gameObject.SetActive(false); toast.transform.localPosition = toastBase; }
        }
    }
}
