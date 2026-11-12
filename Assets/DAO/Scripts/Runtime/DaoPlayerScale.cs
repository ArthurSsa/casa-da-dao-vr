using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace DAO
{
    /// <summary>
    /// Deixa o jogador "minúsculo" ou "gigante" escalando a SALA em volta dele (e não o jogador).
    /// Assim o rig da Meta, a cápsula do locomotor, as mãos e a física continuam em tamanho real:
    /// não dá para cair do mapa nem atravessar o chão ao se agachar. Os pés do jogador são o ponto fixo.
    /// Minúsculo 0,15 = sala 6,7x maior · Gigante 2,2 = sala 2,2x menor.
    /// </summary>
    public class DaoPlayerScale : MonoBehaviour
    {
        [Tooltip("Raiz da sala que será escalada (preenchido pelo montador da cena).")]
        public Transform worldRoot;
        [Tooltip("Rig do jogador (só para achar o ponto dos pés). Se vazio, pega do DaoRoomManager.")]
        public Transform playerRig;
        public Transform head;

        [Header("Escalas aparentes do jogador")]
        [Tooltip("0.15 = o jogador parece ter 15% do tamanho (a sala fica 6,7x maior).")]
        public float tinyScale = 0.15f;
        [Tooltip("2.2 = o jogador parece 2,2x maior (a sala fica 2,2x menor).")]
        public float giantScale = 2f;
        [Tooltip("Duração da transição. Mais lenta = mais confortável.")]
        public float duration = 1.5f;
        [Tooltip("Volta ao tamanho normal sozinho depois de N segundos (0 = nunca).")]
        public float autoResetAfter = 25f;

        [Header("Texto (opcional)")]
        [Tooltip("Mostra o efeito ativo e a contagem regressiva.")]
        public DaoText statusText;

        public UnityEvent<float> onScaleChanged = new UnityEvent<float>();

        Coroutine anim;
        Coroutine timer;
        bool saved;
        Vector3 originalPosition;
        Vector3 originalScale;
        readonly List<(Light light, float range)> lights = new List<(Light, float)>();

        /// <summary>Escala aparente do jogador (1 = normal).</summary>
        public float CurrentScale => worldRoot != null && saved ? originalScale.x / worldRoot.localScale.x : 1f;
        public bool IsActive => !Mathf.Approximately(CurrentScale, 1f);
        public float SecondsLeft { get; private set; }

        void Resolve()
        {
            var mgr = DaoRoomManager.Instance;
            if (mgr != null) { playerRig = mgr.playerRig; head = mgr.head; } // pode mudar (modo desktop)
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (worldRoot != null && !saved)
            {
                saved = true;
                originalPosition = worldRoot.position;
                originalScale = worldRoot.localScale;
                lights.Clear();
                foreach (var l in worldRoot.GetComponentsInChildren<Light>(true)) lights.Add((l, l.range));
            }
        }

        public void Shrink() => SetScale(tinyScale);
        public void Grow() => SetScale(giantScale);
        public void ResetScale() => SetScale(1f);

        public void ResetScaleInstant()
        {
            if (anim != null) StopCoroutine(anim);
            if (timer != null) StopCoroutine(timer);
            anim = timer = null;
            if (worldRoot == null || !saved) return;
            worldRoot.position = originalPosition;
            worldRoot.localScale = originalScale;
            foreach (var (l, r) in lights) if (l != null) l.range = r;
            Physics.SyncTransforms();
            SecondsLeft = 0f;
            onScaleChanged.Invoke(1f);
        }

        public void SetScale(float target)
        {
            Resolve();
            if (worldRoot == null) { Debug.LogWarning("[DAO] DaoPlayerScale sem worldRoot."); return; }
            if (anim != null) StopCoroutine(anim);
            if (timer != null) StopCoroutine(timer);
            timer = null;
            anim = StartCoroutine(Animate(target));
            if (autoResetAfter > 0f && !Mathf.Approximately(target, 1f))
                timer = StartCoroutine(AutoReset(target));
            else
                DaoText.Set(statusText, "");
        }

        IEnumerator AutoReset(float target)
        {
            string name = target < 1f ? "MINÚSCULO" : "GIGANTE";
            for (SecondsLeft = autoResetAfter; SecondsLeft > 0f; SecondsLeft -= Time.deltaTime)
            {
                DaoText.Set(statusText, $"Efeito ativo: {name}\nVocê volta ao tamanho normal em {Mathf.CeilToInt(SecondsLeft)} s");
                yield return null;
            }
            SecondsLeft = 0f;
            timer = null;
            DaoText.Set(statusText, "Tamanho normal de novo.\nA decisão continua registrada no livro-razão.");
            if (anim != null) StopCoroutine(anim);
            anim = StartCoroutine(Animate(1f));
        }

        IEnumerator Animate(float target)
        {
            float start = CurrentScale;
            for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, duration))
            {
                Apply(Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t)));
                yield return null;
            }
            Apply(target);
            anim = null;
        }

        void Apply(float playerScale)
        {
            if (worldRoot == null || playerScale <= 0f) return;
            Resolve();
            // Ponto fixo: o chão sob a cabeça do jogador.
            Vector3 pivot = head != null ? head.position : worldRoot.position;
            if (playerRig != null) pivot.y = playerRig.position.y;

            float currentRoomScale = worldRoot.localScale.x;
            float newRoomScale = originalScale.x / playerScale;
            float k = newRoomScale / currentRoomScale;
            worldRoot.position = pivot + (worldRoot.position - pivot) * k;
            worldRoot.localScale = originalScale * (1f / playerScale);
            foreach (var (l, r) in lights) if (l != null) l.range = r / playerScale;
            Physics.SyncTransforms();
            onScaleChanged.Invoke(playerScale);
        }
    }
}
