using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace DAO
{
    /// <summary>
    /// Ponte entre o botão de Poke do Meta XR SDK e a lógica do projeto.
    /// No Poke: InteractableUnityEventWrapper > When Select > DaoButton.Press().
    /// Dá retorno visual (pulso de escala), som e evita clique duplo.
    /// </summary>
    public class DaoButton : MonoBehaviour
    {
        public UnityEvent onPress = new UnityEvent();
        [Tooltip("Tempo mínimo entre dois cliques (o poke às vezes dispara duas vezes).")]
        public float cooldown = 0.6f;
        [Tooltip("Parte visual que pulsa ao apertar (opcional).")]
        public Transform visual;
        public AudioClip clickSound;
        [Range(0f, 1f)] public float volume = 0.8f;
        public bool interactable = true;

        float lastPress = -10f;
        Vector3 baseScale;
        Coroutine pulse;

        void Awake()
        {
            if (visual == null) visual = transform;
            baseScale = visual.localScale;
        }

        public void Press()
        {
            if (!interactable || Time.time - lastPress < cooldown) return;
            lastPress = Time.time;
            if (clickSound != null) AudioSource.PlayClipAtPoint(clickSound, transform.position, volume);
            if (pulse != null) StopCoroutine(pulse);
            pulse = StartCoroutine(Pulse());
            onPress.Invoke();
        }

        public void SetInteractable(bool value) => interactable = value;

        IEnumerator Pulse()
        {
            const float d = 0.15f;
            for (float t = 0f; t < 1f; t += Time.deltaTime / d)
            {
                float k = 1f - 0.12f * Mathf.Sin(t * Mathf.PI);
                visual.localScale = baseScale * k;
                yield return null;
            }
            visual.localScale = baseScale;
        }
    }
}
