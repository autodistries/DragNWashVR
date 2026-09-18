using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WalkNWash.VRCompanion
{
    // Borrow live desktop assets, never reparent or modify the desktop UI.
    internal sealed class DialogueStyle
    {
        internal static readonly Color Paper = new Color(.82f, .79f, .73f, .78f);
        internal static readonly Color Ink = new Color(.12f, .12f, .12f, 1);
        private readonly Dictionary<Material, Material> materials = new Dictionary<Material, Material>();
        internal void Text(TMP_Text target, TMP_Text source)
        {
            target.color = Ink;
            target.enableVertexGradient = false;
            if (!source || !source.font) return;
            target.font = source.font;
            if (source.font.material)
            {
                if (!materials.TryGetValue(source.font.material, out var copy))
                {
                    copy = new Material(source.font.material);
                    copy.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                    materials.Add(source.font.material, copy);
                }
                target.fontSharedMaterial = copy;
            }
            target.color = Ink;
            target.fontStyle = source.fontStyle;
            target.enableVertexGradient = false;
            target.colorGradient = source.colorGradient;
            target.characterSpacing = source.characterSpacing;
            target.wordSpacing = source.wordSpacing;
            target.lineSpacing = source.lineSpacing;
            target.richText = source.richText;
        }
        internal void Dispose()
        {
            foreach (var material in materials.Values) if (material) Object.Destroy(material);
            materials.Clear();
        }
    }
}
