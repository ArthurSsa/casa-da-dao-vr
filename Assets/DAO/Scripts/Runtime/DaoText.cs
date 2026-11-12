using System.Reflection;
using UnityEngine;

namespace DAO
{
    /// <summary>
    /// Coloque no mesmo objeto de um TextMeshPro, UI Text ou TextMesh.
    /// Os scripts DAO escrevem textos através dele, sem depender de um tipo de texto específico.
    /// Se tiver um painel (quad branco atrás do texto), ele é redimensionado sozinho para caber o texto.
    /// </summary>
    [DisallowMultipleComponent]
    public class DaoText : MonoBehaviour
    {
        [Tooltip("Quad branco atrás do texto, para leitura. Ajusta o tamanho ao texto a cada mudança.")]
        public Transform panel;
        [Tooltip("Quad um pouco maior, atrás do painel, que forma a borda colorida.")]
        public Transform panelBorder;
        public Vector2 padding = new Vector2(0.05f, 0.035f);
        public float borderWidth = 0.012f;

        Component target;
        PropertyInfo textProperty;
        Renderer textRenderer;
        Bounds lastBounds;

        void Awake() => Resolve();

        void Resolve()
        {
            if (textRenderer == null) textRenderer = GetComponent<Renderer>();
            if (textProperty != null) return;
            foreach (var c in GetComponents<Component>())
            {
                if (c == null || c == this) continue;
                var p = c.GetType().GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
                if (p != null && p.CanWrite && p.PropertyType == typeof(string))
                {
                    target = c;
                    textProperty = p;
                    return;
                }
            }
            Debug.LogWarning($"[DAO] DaoText em '{name}' não achou nenhum componente de texto (TMP, Text ou TextMesh).", this);
        }

        public string Get()
        {
            Resolve();
            return textProperty != null ? (string)textProperty.GetValue(target) : "";
        }

        public void Set(string value)
        {
            Resolve();
            if (textProperty != null) textProperty.SetValue(target, value);
            FitPanel(true);
        }

        public static void Set(DaoText t, string value)
        {
            if (t != null) t.Set(value);
        }

        void LateUpdate() => FitPanel(false);

        /// <summary>Encaixa o painel branco (e a borda) no tamanho atual do texto.</summary>
        public void FitPanel(bool force)
        {
            if (panel == null) return;
            if (textRenderer == null) textRenderer = GetComponent<Renderer>();
            if (textRenderer == null) return;
            Bounds b = textRenderer.localBounds;
            if (!force && b == lastBounds) return;
            lastBounds = b;

            bool empty = b.size.x < 1e-4f || string.IsNullOrEmpty(Get());
            panel.gameObject.SetActive(!empty);
            if (panelBorder != null) panelBorder.gameObject.SetActive(!empty);
            if (empty) return;

            Vector3 size = new Vector3(b.size.x + padding.x * 2f, b.size.y + padding.y * 2f, 1f);
            panel.localPosition = new Vector3(b.center.x, b.center.y, 0.01f);
            panel.localRotation = Quaternion.identity;
            panel.localScale = size;
            if (panelBorder != null)
            {
                panelBorder.localPosition = new Vector3(b.center.x, b.center.y, 0.014f);
                panelBorder.localRotation = Quaternion.identity;
                panelBorder.localScale = new Vector3(size.x + borderWidth * 2f, size.y + borderWidth * 2f, 1f);
            }
        }
    }
}
