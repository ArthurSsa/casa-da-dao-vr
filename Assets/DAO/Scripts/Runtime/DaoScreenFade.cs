using System.Collections;
using UnityEngine;

namespace DAO
{
    /// <summary>
    /// Fade para preto em volta da cabeça do jogador (esfera com shader ZTest Always).
    /// Funciona no Built-in e no URP, com single-pass/multiview. Usado nas trocas de sala e mudanças de escala
    /// para evitar enjoo (nunca mova o jogador "de supetão" sem fade).
    /// </summary>
    public class DaoScreenFade : MonoBehaviour
    {
        [Tooltip("CenterEyeAnchor do rig. Se vazio, usa a Camera.main.")]
        public Transform head;
        [Tooltip("Material com o shader DAO/ScreenFade (garante que o shader entre no build).")]
        public Material fadeMaterial;
        public Color color = Color.black;
        [Tooltip("Raio da esfera em metros (precisa ser maior que o near clip da câmera).")]
        public float radius = 0.3f;

        GameObject sphere;
        Material runtimeMaterial;
        public float Alpha { get; private set; }

        void Awake()
        {
            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "DAO Fade Sphere";
            Destroy(sphere.GetComponent<Collider>());
            var r = sphere.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var shader = fadeMaterial != null ? fadeMaterial.shader : Shader.Find("DAO/ScreenFade");
            runtimeMaterial = fadeMaterial != null ? new Material(fadeMaterial) : new Material(shader);
            r.sharedMaterial = runtimeMaterial;
            DontDestroyOnLoad(sphere);
            SetAlpha(0f);
        }

        void OnEnable() => Application.onBeforeRender += Follow;
        void OnDisable() => Application.onBeforeRender -= Follow;
        void OnDestroy()
        {
            if (sphere != null) Destroy(sphere);
            if (runtimeMaterial != null) Destroy(runtimeMaterial);
        }

        void LateUpdate() => Follow();

        void Follow()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (head == null || sphere == null) return;
            sphere.transform.position = head.position;
            sphere.transform.localScale = Vector3.one * (radius * 2f); // escala de mundo fixa, mesmo com o rig encolhido
        }

        public void SetAlpha(float a)
        {
            Alpha = Mathf.Clamp01(a);
            var c = color;
            c.a = Alpha;
            runtimeMaterial.SetColor("_Color", c);
            sphere.SetActive(Alpha > 0.001f);
        }

        public IEnumerator FadeTo(float target, float duration)
        {
            float start = Alpha;
            if (duration <= 0f) { SetAlpha(target); yield break; }
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / duration)
            {
                SetAlpha(Mathf.Lerp(start, target, t));
                yield return null;
            }
            SetAlpha(target);
        }

        public void FadeOut(float duration) => StartCoroutine(FadeTo(1f, duration));
        public void FadeIn(float duration) => StartCoroutine(FadeTo(0f, duration));
    }
}
