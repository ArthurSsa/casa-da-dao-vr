using System.Reflection;
using UnityEngine;

namespace DAO
{
    /// <summary>
    /// Coloque no mesmo objeto de um TextMeshPro, UI Text ou TextMesh.
    /// Os scripts DAO escrevem textos através dele, sem depender de um tipo de texto específico.
    /// </summary>
    [DisallowMultipleComponent]
    public class DaoText : MonoBehaviour
    {
        Component target;
        PropertyInfo textProperty;

        void Awake() => Resolve();

        void Resolve()
        {
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

        public void Set(string value)
        {
            Resolve();
            if (textProperty != null) textProperty.SetValue(target, value);
        }

        public static void Set(DaoText t, string value)
        {
            if (t != null) t.Set(value);
        }
    }
}
