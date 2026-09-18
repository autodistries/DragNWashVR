using UnityEngine;
using UnityEngine.UI;

namespace WalkNWash.VRCompanion
{
    // Vector capsule: crisp at any VR scale, no texture or asset bundle needed.
    internal sealed class TriggerBadge : MaskableGraphic
    {
        private Text label;
        internal static TriggerBadge Create(Transform parent, Material material, string caption, float width, float height)
        {
            var go = new GameObject("Trigger badge", typeof(RectTransform), typeof(CanvasRenderer));
            var badge = go.AddComponent<TriggerBadge>();
            badge.rectTransform.SetParent(parent, false);
            badge.rectTransform.sizeDelta = new Vector2(width, height);
            badge.material = material; badge.raycastTarget = false;
            var textObject = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var rect = textObject.GetComponent<RectTransform>(); rect.SetParent(go.transform, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            badge.label = textObject.GetComponent<Text>();
            badge.label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            badge.label.fontSize = Mathf.RoundToInt(height * .52f); badge.label.fontStyle = FontStyle.Bold;
            badge.label.alignment = TextAnchor.MiddleCenter; badge.label.color = new Color(.88f, .97f, 1);
            badge.label.material = material; badge.label.raycastTarget = false;
            badge.SetCaption(caption);
            return badge;
        }
        internal void SetCaption(string caption) { label.text = caption; }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            DrawCapsule(mesh, rect.width, rect.height, new Color(.12f, .50f, .66f, .95f));
            DrawCapsule(mesh, rect.width - 4, rect.height - 4, new Color(.025f, .09f, .15f, .96f));
        }
        private void DrawCapsule(VertexHelper mesh, float width, float height, Color tint)
        {
            const int segments = 48;
            int start = mesh.currentVertCount;
            Vector2 center = rectTransform.rect.center;
            float radius = Mathf.Max(0, height * .5f), straight = Mathf.Max(0, (width - height) * .5f);
            mesh.AddVert(center, tint, Vector2.zero);
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * 2 * Mathf.PI / segments;
                float cos = Mathf.Cos(angle);
                mesh.AddVert(center + new Vector2(cos * radius + (cos >= 0 ? straight : -straight), Mathf.Sin(angle) * radius), tint, Vector2.zero);
                if (i > 0) mesh.AddTriangle(start, start + i, start + i + 1);
            }
        }
    }
}
