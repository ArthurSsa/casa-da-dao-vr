using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace DAO
{
    /// <summary>
    /// Deixa o jogador minúsculo ou gigante escalando o rig inteiro (cabeça, mãos e passo da locomoção juntos).
    /// O ponto sob a cabeça fica parado no chão, então o mundo "cresce/encolhe" em volta da pessoa.
    /// </summary>
    public class DaoPlayerScale : MonoBehaviour
    {
        [Tooltip("Se vazio, pega do DaoRoomManager.")]
        public Transform playerRig;
        public Transform head;

        [Header("Escalas")]
        [Tooltip("0.15 = olhos a ~25 cm do chão.")]
        public float tinyScale = 0.15f;
        [Tooltip("Confira o pé-direito da sala: altura dos olhos ≈ 1,6 m × escala.")]
        public float giantScale = 2f;
        [Tooltip("Duração da transição. Mais lenta = mais confortável.")]
        public float duration = 1.5f;
        [Tooltip("Volta ao tamanho normal sozinho depois de N segundos (0 = nunca). Útil na apresentação.")]
        public float autoResetAfter = 30f;

        public UnityEvent<float> onScaleChanged = new UnityEvent<float>();

        Coroutine anim;
        Coroutine timer;

        public float CurrentScale => playerRig != null ? playerRig.localScale.x : 1f;

        void Start() => Resolve();

        void Resolve()
        {
            var mgr = DaoRoomManager.Instance;
            if (playerRig == null && mgr != null) playerRig = mgr.playerRig;
            if (head == null && mgr != null) head = mgr.head;
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (playerRig == null && head != null) playerRig = head.root;
        }

        public void Shrink() => SetScale(tinyScale);
        public void Grow() => SetScale(giantScale);
        public void ResetScale() => SetScale(1f);

        public void ResetScaleInstant()
        {
            Resolve();
            if (anim != null) StopCoroutine(anim);
            if (timer != null) StopCoroutine(timer);
            anim = timer = null;
            Apply(1f);
        }

        public void SetScale(float target)
        {
            Resolve();
            if (playerRig == null) return;
            if (anim != null) StopCoroutine(anim);
            if (timer != null) StopCoroutine(timer);
            anim = StartCoroutine(Animate(target));
            if (autoResetAfter > 0f && !Mathf.Approximately(target, 1f))
                timer = StartCoroutine(AutoReset());
        }

        IEnumerator AutoReset()
        {
            yield return new WaitForSeconds(autoResetAfter);
            timer = null;
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

        void Apply(float s)
        {
            if (playerRig == null) return;
            float current = CurrentScale;
            if (current <= 0f) current = 1f;
            Vector3 pivot = head != null
                ? new Vector3(head.position.x, playerRig.position.y, head.position.z)
                : playerRig.position;
            playerRig.position = pivot + (playerRig.position - pivot) * (s / current);
            playerRig.localScale = Vector3.one * s;
            onScaleChanged.Invoke(s);
        }
    }
}
