using System.Collections.Generic;
using TwoCT.Combat;
using TwoCT.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TwoCT.UI
{
    /// <summary>
    /// Combat HUD (editor-built uGUI): per-player HP/mana <b>bars</b> (shielded HP shows yellow),
    /// a boss health bar, the boss dialogue box positioned <b>above the boss's head</b>, a
    /// victory/defeat banner, toasts and floating damage numbers. Built by the scene tool; the
    /// runtime rebuilds only as a fallback if references are missing.
    /// </summary>
    public class CombatHUD : MonoBehaviour
    {
        [System.Serializable]
        private class PlayerPanel
        {
            public GameObject root;
            public Text nameText;
            public Image hpFill;      // green (current HP)
            public Image shieldFill;  // yellow (HP + shield), drawn behind hpFill
            public Text hpText;
            public Image manaFill;
            public Text manaText;
        }

        [Header("Auto-wired (built by 2CT ▸ Scenes tool; editable in the scene)")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private Text bossNameText;
        [SerializeField] private Image bossHpFill;
        [SerializeField] private Text bossHpText;
        [SerializeField] private Text bossFireText;
        [SerializeField] private GameObject bossDialoguePanel;
        [SerializeField] private Text bossDialogueText;
        [SerializeField] private PlayerPanel[] playerPanels = new PlayerPanel[3];
        [SerializeField] private Text bannerText;
        [SerializeField] private Text toastText;
        [SerializeField] private RectTransform floatingLayer;

        [Header("Tuning")]
        [SerializeField] private float manaBarMax = 50f;
        [Tooltip("World-space height above the boss transform to float its dialogue box.")]
        [SerializeField] private float bossHeadOffset = 2.0f;

        private static readonly Color Green = new Color(0.35f, 0.85f, 0.4f);
        private static readonly Color Yellow = new Color(0.95f, 0.85f, 0.25f);
        private static readonly Color Blue = new Color(0.35f, 0.6f, 1f);
        private static readonly Color BarBg = new Color(0.08f, 0.08f, 0.12f, 0.9f);

        private float _bossTextHideAt;
        private float _toastHideAt;

        private class Floater { public RectTransform rt; public Text text; public Vector3 world; public float dieAt; }
        private readonly List<Floater> _floaters = new List<Floater>();

        private void Awake() { if (canvas == null) BuildInEditor(); }

        private void OnEnable()
        {
            CombatEvents.BossSay += OnBossSay;
            CombatEvents.DamageNumber += OnDamage;
            CombatEvents.Toast += OnToast;
        }

        private void OnDisable()
        {
            CombatEvents.BossSay -= OnBossSay;
            CombatEvents.DamageNumber -= OnDamage;
            CombatEvents.Toast -= OnToast;
        }

        // =====================================================================
        //  Runtime updates
        // =====================================================================
        private void Update()
        {
            var cm = CombatManager.Instance;
            var boss = cm != null ? cm.Boss : null;

            if (boss != null && bossHpFill != null)
            {
                float frac = boss.MaxHP.Value > 0 ? (float)boss.HP.Value / boss.MaxHP.Value : 0f;
                SetFrac(bossHpFill, frac);
                if (bossNameText) bossNameText.text = $"{(boss.Data != null ? boss.Data.bossName : "Boss")}   —   Phase {boss.PhaseIndex.Value + 1}";
                if (bossHpText) bossHpText.text = $"{boss.HP.Value} / {boss.MaxHP.Value}";
                if (bossFireText) bossFireText.text = boss.TotalFireStacks > 0 ? $"🔥 x{boss.TotalFireStacks}" : "";
            }

            for (int i = 0; i < playerPanels.Length; i++)
            {
                var panel = playerPanels[i];
                if (panel == null || panel.root == null) continue;
                var p = PlayerRegistry.BySlot(i);
                if (p == null) { panel.root.SetActive(false); continue; }
                panel.root.SetActive(true);

                float maxHP = Mathf.Max(1, p.MaxHP.Value);
                if (panel.shieldFill) SetFrac(panel.shieldFill, (p.HP.Value + p.Shield.Value) / maxHP);
                if (panel.hpFill) SetFrac(panel.hpFill, p.HP.Value / maxHP);
                if (panel.manaFill) SetFrac(panel.manaFill, p.Mana.Value / Mathf.Max(1f, manaBarMax));

                string state = !p.IsAlive ? "  <color=#ff6666>[KO]</color>"
                             : (!p.CanAct.Value ? "  <color=#aaaaaa>(waiting)</color>"
                             : (p.HasEndedTurn.Value ? "  <color=#aaaaaa>(ended)</color>" : ""));
                if (panel.nameText) panel.nameText.text = $"<b>Player {p.Slot.Value + 1}</b>{state}";
                if (panel.hpText) panel.hpText.text = p.Shield.Value > 0 ? $"{p.HP.Value}/{p.MaxHP.Value}  (+{p.Shield.Value})" : $"{p.HP.Value}/{p.MaxHP.Value}";
                if (panel.manaText) panel.manaText.text = $"{p.Mana.Value} MP";
            }

            // Boss dialogue box floats above the boss's head (auto-hides after its line duration).
            if (bossDialoguePanel != null && bossDialoguePanel.activeSelf)
            {
                if (Time.time > _bossTextHideAt) bossDialoguePanel.SetActive(false);
                else PositionBossDialogue(boss);
            }

            if (toastText != null && toastText.gameObject.activeSelf && Time.time > _toastHideAt)
                toastText.gameObject.SetActive(false);

            if (bannerText != null && cm != null)
            {
                bool show = cm.Phase.Value == CombatPhase.Victory || cm.Phase.Value == CombatPhase.Defeat;
                bannerText.gameObject.SetActive(show);
                if (show) bannerText.text = cm.Phase.Value == CombatPhase.Victory ? "VICTORY" : "DEFEAT";
            }

            UpdateFloaters();
        }

        private void PositionBossDialogue(BossController boss)
        {
            var cam = Camera.main;
            if (cam == null || boss == null) return;
            Vector3 head = boss.transform.position + Vector3.up * bossHeadOffset;
            Vector3 sp = cam.WorldToScreenPoint(head);
            if (sp.z < 0) return;
            bossDialoguePanel.transform.position = sp;
        }

        private void OnBossSay(string text, float seconds)
        {
            if (bossDialoguePanel == null) return;
            bossDialogueText.text = text;
            bossDialoguePanel.SetActive(true);
            var boss = CombatManager.Instance != null ? CombatManager.Instance.Boss : null;
            PositionBossDialogue(boss);
            float dur = seconds > 0 ? seconds : 3f;
            if (boss != null)   // squash-and-stretch the boss sprite while its line is up
            {
                var jiggle = boss.GetComponent<SpeakingJiggle>();
                if (jiggle == null) jiggle = boss.gameObject.AddComponent<SpeakingJiggle>();
                jiggle.Talk(dur);
            }
            _bossTextHideAt = Time.time + dur;
        }

        private void OnToast(string msg)
        {
            if (toastText == null) return;
            toastText.text = msg;
            toastText.gameObject.SetActive(true);
            _toastHideAt = Time.time + 2f;
        }

        private void OnDamage(int slot, int amount, Vector3 world)
        {
            if (floatingLayer == null) return;
            var p = PlayerRegistry.BySlot(slot);
            Vector3 at = p != null ? p.transform.position + Vector3.up * 1.2f : world;
            var rt = Ui.New("Dmg", floatingLayer, out _);
            var txt = rt.gameObject.AddComponent<Text>();
            txt.font = Ui.Font; txt.fontSize = 30; txt.fontStyle = FontStyle.Bold; txt.alignment = TextAnchor.MiddleCenter;
            txt.color = new Color(1f, 0.85f, 0.2f); txt.text = $"-{amount}"; txt.raycastTarget = false;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow; txt.verticalOverflow = VerticalWrapMode.Overflow;
            rt.sizeDelta = new Vector2(80, 40);
            _floaters.Add(new Floater { rt = rt, text = txt, world = at, dieAt = Time.time + 1.1f });
        }

        private void UpdateFloaters()
        {
            var cam = Camera.main;
            for (int i = _floaters.Count - 1; i >= 0; i--)
            {
                var f = _floaters[i];
                float life = f.dieAt - Time.time;
                if (life <= 0f || cam == null) { if (f.rt) Destroy(f.rt.gameObject); _floaters.RemoveAt(i); continue; }
                Vector3 sp = cam.WorldToScreenPoint(f.world + Vector3.up * (1.1f - life));
                f.rt.position = sp;
                var c = f.text.color; c.a = Mathf.Clamp01(life); f.text.color = c;
            }
        }

        // =====================================================================
        //  Build (edit-time or runtime fallback)
        // =====================================================================
        public void BuildInEditor()
        {
            Ui.EnsureEventSystem();
            canvas = Ui.Canvas("CombatCanvas", transform, 100);
            var root = canvas.transform;

            // Boss bar (top-right)
            var bossBox = Ui.Panel("BossBox", root, new Color(0, 0, 0, 0.35f));
            Ui.Anchor(bossBox.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-240, -70), new Vector2(440, 96));
            bossNameText = Ui.Label(bossBox.rectTransform, "Boss", 20, FontStyle.Bold, Color.white, TextAnchor.UpperCenter);
            Ui.Anchor(bossNameText.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(420, 26));
            var bossBar = Ui.Panel("BossHpBg", bossBox.rectTransform, BarBg);
            Ui.Anchor(bossBar.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(410, 26));
            bossHpFill = Fill(bossBar.rectTransform, new Color(0.85f, 0.2f, 0.25f));
            bossHpText = Ui.Label(bossBar.rectTransform, "", 15, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter); Ui.Stretch(bossHpText.rectTransform);
            bossFireText = Ui.Label(bossBox.rectTransform, "", 15, FontStyle.Bold, new Color(1f, 0.6f, 0.3f), TextAnchor.MiddleCenter);
            Ui.Anchor(bossFireText.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -70), new Vector2(420, 20));

            // Boss dialogue box (floats above the boss; pivot bottom-centre)
            var dlg = Ui.Panel("BossDialogue", root, new Color(0, 0, 0, 0.85f));
            Ui.Anchor(dlg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420, 64), new Vector2(0.5f, 0));
            bossDialoguePanel = dlg.gameObject;
            bossDialogueText = Ui.Label(dlg.rectTransform, "", 20, FontStyle.Normal, Color.white, TextAnchor.MiddleCenter);
            Ui.Stretch(bossDialogueText.rectTransform);
            bossDialogueText.rectTransform.offsetMin = new Vector2(16, 8); bossDialogueText.rectTransform.offsetMax = new Vector2(-16, -8);
            dlg.gameObject.SetActive(false);

            // Player panels with HP/mana bars (top-left, stacked)
            playerPanels = new PlayerPanel[3];
            for (int i = 0; i < 3; i++)
                playerPanels[i] = BuildPlayerPanel(root, i);

            // Toast (bottom-centre)
            toastText = Ui.Label(root, "", 22, FontStyle.Bold, new Color(1f, 0.9f, 0.6f), TextAnchor.MiddleCenter);
            Ui.Anchor(toastText.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 300), new Vector2(700, 30));
            toastText.gameObject.SetActive(false);

            // Banner (centre)
            bannerText = Ui.Label(root, "", 72, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            Ui.Anchor(bannerText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200, 120));
            bannerText.gameObject.SetActive(false);

            floatingLayer = Ui.New("Floaters", root, out _);
            Ui.Stretch(floatingLayer);
        }

        private PlayerPanel BuildPlayerPanel(Transform parent, int i)
        {
            var pp = new PlayerPanel();
            var box = Ui.Panel($"Player{i}", parent, new Color(0, 0, 0, 0.5f));
            Ui.Anchor(box.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(160, -60 - i * 104), new Vector2(300, 92), new Vector2(0.5f, 1));
            pp.root = box.gameObject;

            pp.nameText = Ui.Label(box.rectTransform, "", 16, FontStyle.Bold, Color.white, TextAnchor.UpperLeft);
            Ui.Anchor(pp.nameText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -6), new Vector2(-24, 24), new Vector2(0.5f, 1));
            pp.nameText.rectTransform.offsetMin = new Vector2(12, pp.nameText.rectTransform.offsetMin.y);

            // HP bar: yellow (HP+shield) behind, green (HP) in front.
            var hpBg = Ui.Panel("HpBg", box.rectTransform, BarBg);
            Ui.Anchor(hpBg.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -38), new Vector2(276, 24));
            pp.shieldFill = Fill(hpBg.rectTransform, Yellow);
            pp.hpFill = Fill(hpBg.rectTransform, Green);
            pp.hpText = Ui.Label(hpBg.rectTransform, "", 14, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter); Ui.Stretch(pp.hpText.rectTransform);

            // Mana bar
            var manaBg = Ui.Panel("ManaBg", box.rectTransform, BarBg);
            Ui.Anchor(manaBg.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -68), new Vector2(276, 20));
            pp.manaFill = Fill(manaBg.rectTransform, Blue);
            pp.manaText = Ui.Label(manaBg.rectTransform, "", 13, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter); Ui.Stretch(pp.manaText.rectTransform);

            box.gameObject.SetActive(false);
            return pp;
        }

        // Fills are driven by RectTransform width (anchorMax.x), not Image.fillAmount — the latter
        // needs a sprite to clip and silently no-ops without one. Width-based fills always work.
        private static Image Fill(Transform parent, Color color)
        {
            var img = Ui.Panel("Fill", parent, color);
            Ui.Stretch(img.rectTransform);   // anchorMin (0,0), anchorMax (1,1), zero offsets
            return img;
        }

        private static void SetFrac(Image fill, float frac)
        {
            var rt = fill.rectTransform;
            rt.anchorMax = new Vector2(Mathf.Clamp01(frac), 1f);  // right edge tracks the fraction
        }
    }
}
