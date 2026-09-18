using UnityEngine;
using UnityEngine.UI;

namespace WalkNWash.VRCompanion
{
    // Rounded progress-bar geometry; layered border, track and fill.
    internal sealed class HudBarShape : Image
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            Fill(mesh, rect, color);
        }
        private static void Fill(VertexHelper mesh, Rect rect, Color tint, bool border = false)
        {
            int start = mesh.currentVertCount;
            float radius = Mathf.Min(7, Mathf.Min(rect.width, rect.height) * .25f);
            if (!border) mesh.AddVert(rect.center, tint, Vector2.zero);
            for (int corner = 0; corner < 4; corner++)
            {
                var center = new Vector2(corner == 0 || corner == 3 ? rect.xMax - radius : rect.xMin + radius,
                    corner < 2 ? rect.yMax - radius : rect.yMin + radius);
                for (int step = 0; step <= 8; step++)
                {
                    float angle = (corner * 90 + step * 90f / 8) * Mathf.Deg2Rad;
                    var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    mesh.AddVert(center + direction * radius, tint, Vector2.zero);
                    if (border) mesh.AddVert(center + direction * (radius - 2), tint, Vector2.zero);
                }
            }
            for (int i = 0; i < 36; i++)
            {
                if (!border) mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % 36);
                else
                {
                    int a = start + i * 2, b = start + (i + 1) % 36 * 2;
                    mesh.AddTriangle(a, b, a + 1);
                    mesh.AddTriangle(a + 1, b, b + 1);
                }
            }
        }
    }
}
