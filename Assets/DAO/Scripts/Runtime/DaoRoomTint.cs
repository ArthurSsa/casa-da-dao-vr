using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DAO
{
    /// <summary>
    /// Muda a "cor" da sala (é o resultado do Desafio 05 do PDF).
    /// Tinge as luzes E os materiais dos modelos via MaterialPropertyBlock, então funciona também com
    /// materiais unlit (o sótão do Sketchfab é unlit e ignora luzes).
    /// </summary>
    public class DaoRoomTint : MonoBehaviour
    {
        [Tooltip("Raiz do modelo da sala. Todos os renderers abaixo serão tingidos.")]
        public Transform roomRoot;
        public Light[] lights;
        public Color[] palette = { new Color(1f, 0.62f, 0.3f), new Color(0.35f, 0.6f, 1f) };
        [Range(0f, 1f)] public float materialStrength = 0.65f;
        public float duration = 1.2f;

        static readonly string[] ColorProps = { "_BaseColor", "baseColorFactor", "_Color", "_TintColor" };

        struct Slot { public Renderer r; public int index; public int prop; public Color original; }

        readonly List<Slot> slots = new List<Slot>();
        Color[] lightOriginals;
        MaterialPropertyBlock mpb;
        float current; // 0 = original, 1 = cor aplicada
        Color currentTarget = Color.white;
        Coroutine anim;
        bool cached;

        void Cache()
        {
            if (cached) return;
            cached = true;
            mpb = new MaterialPropertyBlock();
            if (roomRoot != null)
            {
                foreach (var r in roomRoot.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null) continue;
                        foreach (var p in ColorProps)
                        {
                            if (!m.HasProperty(p)) continue;
                            slots.Add(new Slot { r = r, index = i, prop = Shader.PropertyToID(p), original = m.GetColor(p) });
                            break;
                        }
                    }
                }
            }
            lightOriginals = new Color[lights?.Length ?? 0];
            for (int i = 0; i < lightOriginals.Length; i++) lightOriginals[i] = lights[i] != null ? lights[i].color : Color.white;
        }

        public void ApplyPalette(int index)
        {
            if (palette == null || palette.Length == 0) return;
            ApplyColor(palette[Mathf.Clamp(index, 0, palette.Length - 1)]);
        }

        public void ApplyColor(Color c)
        {
            Cache();
            if (anim != null) StopCoroutine(anim);
            anim = StartCoroutine(Animate(c, 1f));
        }

        public void ResetTint()
        {
            Cache();
            if (anim != null) StopCoroutine(anim);
            anim = StartCoroutine(Animate(currentTarget, 0f));
        }

        IEnumerator Animate(Color target, float to)
        {
            Color fromColor = currentTarget;
            float from = current;
            for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, duration))
            {
                float k = Mathf.SmoothStep(0f, 1f, t);
                Write(Color.Lerp(fromColor, target, k), Mathf.Lerp(from, to, k));
                yield return null;
            }
            Write(target, to);
            anim = null;
        }

        void Write(Color target, float amount)
        {
            currentTarget = target;
            current = amount;
            foreach (var s in slots)
            {
                if (s.r == null) continue;
                Color tinted = s.original * Color.Lerp(Color.white, target, materialStrength);
                tinted.a = s.original.a;
                s.r.GetPropertyBlock(mpb, s.index);
                mpb.SetColor(s.prop, Color.Lerp(s.original, tinted, amount));
                s.r.SetPropertyBlock(mpb, s.index);
            }
            for (int i = 0; i < lightOriginals.Length; i++)
                if (lights[i] != null) lights[i].color = Color.Lerp(lightOriginals[i], target, amount);
        }
    }
}
