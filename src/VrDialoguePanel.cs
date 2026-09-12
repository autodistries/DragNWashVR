using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WalkNWash.VRCompanion
{
    internal sealed class DialogueChoice
    {
        internal object Token;
        internal string Text;
        internal bool Enabled;
    }

    // A separate canvas mirrors dialogue, leaving desktop layout/input untouched.
    // Hit tests use this canvas's local coordinates, never desktop mouse pixels.
    internal sealed class VrDialoguePanel : IDisposable
    {
        internal const int None = -1, Continue = -2, Previous = -3, Next = -4;
        private const int PageSize = 4;
        private GameObject root;
        private Canvas canvas;
        private TMP_Text heading, body, footer;
        private readonly TMP_Text[] labels = new TMP_Text[PageSize];
        private readonly Image[] rows = new Image[PageSize];
        private Image previous, next, cursor, background;
        private LineRenderer laser;
        private Material uiMaterial, fontMaterial, laserMaterial;
        private TMP_FontAsset font;
        private readonly List<DialogueChoice> choices = new List<DialogueChoice>();
        private int page;
        private bool anchored, canContinue, laserRequested;
        private static readonly Color Normal = new Color(.08f, .13f, .2f, .98f);
        private static readonly Color Hover = new Color(.05f, .36f, .48f, 1);

        private static RectTransform Rect(GameObject go, Transform parent, float x, float y, float width, float height)
        {
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }
        private Image Box(string name, Transform parent, float x, float y, float w, float h, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Rect(go, parent, x, y, w, h);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.material = uiMaterial;
            image.raycastTarget = false;
            return image;
        }
        private TMP_Text Label(string name, Transform parent, float x, float y, float w, float h, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            Rect(go, parent, x, y, w, h);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSharedMaterial = fontMaterial;
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMin = 18;
            text.fontSizeMax = size;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }
        private void Create(TMP_Text source)
        {
            if (root != null) return;
            Dispose();
            font = source != null ? source.font : TMP_Settings.defaultFontAsset;
            if (font == null) throw new InvalidOperationException("Dialogue font unavailable");
            uiMaterial = new Material(Graphic.defaultGraphicMaterial);
            uiMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            fontMaterial = new Material(font.material);
            fontMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            root = new GameObject("WalkNWash_VR_Dialogue", typeof(RectTransform), typeof(Canvas));
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(1000, 800);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 32000;
            canvas.overrideSorting = true;
            background = Box("Background", root.transform, 0, 0, 1000, 800, new Color(.015f, .025f, .05f, .97f));
            heading = Label("Speaker", root.transform, 0, 340, 940, 50, 32);
            heading.color = new Color(.4f, .9f, 1);
            body = Label("Text", root.transform, 0, 190, 940, 230, 32);
            for (int i = 0; i < PageSize; i++)
            {
                rows[i] = Box("Answer " + i, root.transform, 0, 30 - i * 90, 940, 82, Normal);
                labels[i] = Label("Text", rows[i].transform, 0, 0, 910, 78, 28);
            }
            previous = Box("Previous page", root.transform, -380, -345, 180, 60, Normal);
            Label("Text", previous.transform, 0, 0, 160, 50, 26).text = "< Previous";
            next = Box("Next page", root.transform, 380, -345, 180, 60, Normal);
            Label("Text", next.transform, 0, 0, 160, 50, 26).text = "Next >";
            footer = Label("Help", root.transform, 0, -345, 550, 60, 24);
            footer.alignment = TextAlignmentOptions.Center;
            cursor = Box("Pointer", root.transform, 0, 0, 12, 12, Color.cyan);
            var beam = new GameObject("Controller ray", typeof(LineRenderer));
            beam.transform.SetParent(root.transform, false);
            laser = beam.GetComponent<LineRenderer>();
            laser.positionCount = 2;
            laser.useWorldSpace = true;
            laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            laser.receiveShadows = false;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("Pointer shader unavailable");
            laserMaterial = new Material(shader);
            laserMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            laser.sharedMaterial = laserMaterial;
            laser.startColor = laser.endColor = Color.cyan;
            EndEye();
        }

        internal void Present(TMP_Text source, string speaker, string text, int visibleCharacters,
            IList<DialogueChoice> answers, bool advance)
        {
            Create(source);
            bool changed = choices.Count != answers.Count;
            if (!changed)
                for (int i = 0; i < choices.Count; i++)
                    if (!ReferenceEquals(choices[i].Token, answers[i].Token)) { changed = true; break; }
            if (changed) page = 0;
            choices.Clear();
            choices.AddRange(answers);
            canContinue = advance && choices.Count == 0;
            heading.text = speaker ?? "";
            body.text = text ?? "";
            body.maxVisibleCharacters = visibleCharacters;
            UpdateRows();
        }

        private void UpdateRows()
        {
            for (int i = 0; i < PageSize; i++)
            {
                int index = page * PageSize + i;
                rows[i].gameObject.SetActive(index < choices.Count);
                if (index >= choices.Count) continue;
                labels[i].text = choices[index].Text;
                labels[i].color = choices[index].Enabled ? Color.white : Color.gray;
                rows[i].color = Normal;
            }
            previous.gameObject.SetActive(page > 0);
            next.gameObject.SetActive((page + 1) * PageSize < choices.Count);
            footer.text = choices.Count > 0
                ? "Point + trigger to choose" + (choices.Count > PageSize ? "\n" + (page + 1) + " / " + ((choices.Count + PageSize - 1) / PageSize) : "")
                : canContinue ? "Point here + trigger to continue" : "";
            Layout();
        }

        private static void Position(RectTransform rect, float y, float height)
        {
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        }

        private void Layout()
        {
            // Measure full text, not revealed characters, so typewriter animation
            // cannot move the panel or click targets on every letter.
            float speakerHeight = string.IsNullOrWhiteSpace(heading.text) ? 0 : 42;
            heading.gameObject.SetActive(speakerHeight > 0);
            body.fontSize = body.fontSizeMax;
            float textHeight = Mathf.Clamp(body.GetPreferredValues(body.text, 940, 10000).y + 12, 65, choices.Count > 0 ? 200 : 360);
            float total = 24 + speakerHeight + textHeight + 16 + 60 + 20;
            int count = Math.Min(PageSize, choices.Count - page * PageSize);
            for (int i = 0; i < count; i++)
            {
                labels[i].fontSize = labels[i].fontSizeMax;
                float height = Mathf.Clamp(labels[i].GetPreferredValues(labels[i].text, 910, 10000).y + 18, 52, 100);
                rows[i].rectTransform.sizeDelta = new Vector2(940, height);
                labels[i].rectTransform.sizeDelta = new Vector2(910, height - 8);
                total += height + 8;
            }
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(1000, total);
            background.rectTransform.sizeDelta = new Vector2(1000, total);
            float top = total / 2 - 24;
            Position(heading.rectTransform, top - speakerHeight / 2, speakerHeight);
            top -= speakerHeight;
            Position(body.rectTransform, top - textHeight / 2, textHeight);
            top -= textHeight + 16;
            for (int i = 0; i < count; i++)
            {
                float height = rows[i].rectTransform.sizeDelta.y;
                Position(rows[i].rectTransform, top - height / 2, height);
                top -= height + 8;
            }
            Position(footer.rectTransform, top - 30, 60);
            Position(previous.rectTransform, top - 30, 60);
            Position(next.rectTransform, top - 30, 60);
        }

        private static bool Contains(RectTransform rect, Vector3 local)
            => rect.rect.Contains((Vector2)local - rect.anchoredPosition);

        internal void Place(Transform rig, Vector3 head, Quaternion rotation, float width, float distance)
        {
            if (root.transform.parent != rig) { root.transform.SetParent(rig, false); anchored = false; }
            if (!anchored)
            {
                Quaternion yaw = Quaternion.Euler(0, rotation.eulerAngles.y, 0);
                root.transform.localPosition = head + yaw * new Vector3(0, -.15f, distance);
                root.transform.localRotation = yaw;
                anchored = true;
            }
            root.transform.localScale = Vector3.one * width / 1000;
        }
        internal void Recenter() { anchored = false; }

        internal int Point(Ray ray, bool tracked)
        {
            cursor.enabled = false;
            laserRequested = tracked;
            laser.enabled = tracked;
            for (int i = 0; i < rows.Length; i++) rows[i].color = Normal;
            previous.color = next.color = Normal;
            if (!tracked) return None;
            laser.startWidth = laser.endWidth = .003f * root.transform.parent.lossyScale.x;
            Vector3 origin = root.transform.InverseTransformPoint(ray.origin);
            Vector3 direction = root.transform.InverseTransformDirection(ray.direction);
            int hit = None;
            Vector3 end = ray.GetPoint(2 * root.transform.parent.lossyScale.x);
            // Canvas faces the user from its negative Z side. Reject back-facing rays.
            if (origin.z < 0 && direction.z > .0001f)
            {
                Vector3 local = origin + direction * (-origin.z / direction.z);
                if (root.GetComponent<RectTransform>().rect.Contains(local))
                {
                    end = root.transform.TransformPoint(local);
                    cursor.rectTransform.anchoredPosition = new Vector2(local.x, local.y);
                    cursor.enabled = true;
                    if (choices.Count == 0) hit = canContinue ? Continue : None;
                    else
                    {
                        for (int i = 0; i < PageSize; i++)
                        {
                            int index = page * PageSize + i;
                            if (index < choices.Count && choices[index].Enabled && Contains(rows[i].rectTransform, local))
                            { hit = index; rows[i].color = Hover; }
                        }
                        if (page > 0 && Contains(previous.rectTransform, local))
                        { hit = Previous; previous.color = Hover; }
                        if ((page + 1) * PageSize < choices.Count && Contains(next.rectTransform, local))
                        { hit = Next; next.color = Hover; }
                    }
                }
            }
            laser.SetPosition(0, ray.origin);
            laser.SetPosition(1, end);
            return hit;
        }

        internal bool ChangePage(int target)
        {
            if (target == Previous && page > 0) page--;
            else if (target == Next && (page + 1) * PageSize < choices.Count) page++;
            else return false;
            UpdateRows();
            return true;
        }
        internal void BeginEye(int layer)
        {
            foreach (Transform item in root.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = layer;
            canvas.enabled = true;
            laser.enabled = laserRequested;
            Canvas.ForceUpdateCanvases();
        }
        internal void EndEye()
        {
            if (canvas != null) canvas.enabled = false;
            if (laser != null) laser.enabled = false;
        }
        internal void Hide() { EndEye(); Recenter(); laserRequested = false; }
        public void Dispose()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            if (uiMaterial != null) UnityEngine.Object.Destroy(uiMaterial);
            if (fontMaterial != null) UnityEngine.Object.Destroy(fontMaterial);
            if (laserMaterial != null) UnityEngine.Object.Destroy(laserMaterial);
            root = null;
        }
    }
}
