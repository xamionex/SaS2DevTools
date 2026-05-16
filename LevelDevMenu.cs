using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Common;
using HarmonyLib;
using Menumancer.hud;
using Menumancer.UIFormat;
using ProjectMage;
using ProjectMage.character;
using ProjectMage.gamestate;
using ProjectMage.gamestate.mage;
using ProjectMage.player;
using ProjectMage.player.menu;

namespace SaS2DevTools;

public class LevelDevMenu : LevelBase
{
    // Full sorted config list, doesn't change
    private List<DevConfigEntry> _allConfigs;

    // Configs for the currently active tab only
    private List<DevConfigEntry> _displayedConfigs;

    // Tab state
    private List<string> _tabs;
    private int _currentTabIndex;

    private int _selectedIndex;
    private float _scrollOffset;
    private readonly int _returnScreen;
    private int _currentPlayerId;
    private bool _fast;

    // UI Constants
    private const float TabBarHeight = 36f; // height of one tab row
    private const float TabPadX = 18f; // horizontal text padding inside each tab
    private const float ItemHeight = 40f;
    private const float SectionHeight = 60f;
    private const float TopMargin = 40f; // space above the tab bar
    private const float BottomMargin = 40f; // space below the last item=

    private float _listX;
    private float _listY;
    private float _listWidth;
    private const float ValueWidth = 240f;

    // Color Editing State
    private int _colorCompIndex = -1; // -1 = none, 0=R, 1=G, 2=B, 3=A

    // Dynamic sizing
    private float _currentListVisibleHeight;

    // 10 = LevelGameMenu, 25 = LevelMainMenu
    public LevelDevMenu(Player player, int returnToScreen = 10)
    {
        this.player = player;
        _returnScreen = returnToScreen;
        Init("DevOptions", player);
    }

    public sealed override void Init(string strScreen, Player plr)
    {
        base.Init(strScreen, plr);
        // Make this screen modal to block game input
        if (!screen.uiFlag.Contains(9)) screen.uiFlag.Add(9);
        _currentPlayerId = plr.ID; // 0 = Player1, 1 = Player2

        // Build config list from the active SaS2DevTools instance
        _allConfigs = BuildDevConfigList();
        _allConfigs = _allConfigs
            .OrderBy(c => c.ModName)
            .ThenBy(c => c.Order)
            .ThenBy(c => c.DisplayName)
            .ToList();

        _tabs = _allConfigs
            .GroupBy(c => c.ModName)
            .OrderBy(g => g.Min(c => c.Order))
            .Select(g => g.Key)
            .ToList();
        
        _currentTabIndex = 0;
        RefreshDisplayedConfigs();
    }

    private void RefreshDisplayedConfigs()
    {
        if (_tabs.Count == 0)
        {
            _displayedConfigs = [];
            return;
        }

        var tab = _tabs[_currentTabIndex];
        _displayedConfigs = _allConfigs.Where(c => c.ModName == tab).ToList();
        _selectedIndex = 0;
        _scrollOffset = 0f;
        _colorCompIndex = -1;
        _fast = false;
    }

    private bool HasTabs => _tabs.Count > 1;

    // Helper to get the correct ConfigEntryBase for the current player
    private DevConfigEntry GetActiveEntry(DevConfigEntry cfg) => cfg;

    // Helper to replace missing Math.Clamp in .NET Framework 4.5
    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
    private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));

    public override void Update(Character character, float frameTime)
    {
        if (!CanInput()) return;

        // Tab navigation
        if (HasTabs)
        {
            if (player.keys.keyCatLeft)
            {
                _currentTabIndex = (_currentTabIndex - 1 + _tabs.Count) % _tabs.Count;
                RefreshDisplayedConfigs();
                PlaySelect();
                return;
            }

            if (player.keys.keyCatRight)
            {
                _currentTabIndex = (_currentTabIndex + 1) % _tabs.Count;
                RefreshDisplayedConfigs();
                PlaySelect();
                return;
            }
        }

        if (_displayedConfigs.Count == 0) return;

        if (player.keys.keyUp || player.keys.keyDown)
        {
            var dir = player.keys.keyUp ? -1 : 1;
            _selectedIndex = (_selectedIndex + dir + _displayedConfigs.Count) % _displayedConfigs.Count;
            _colorCompIndex = -1;
            PlaySelect();
            EnsureVisible();
            return;
        }

        var config = _displayedConfigs[_selectedIndex];
        var activeEntry = GetActiveEntry(config);
        var isColor = IsColorString(activeEntry);
        var isBool = IsBool(activeEntry);
        var valueChanged = false;

        // Color Picker: Accept cycles R -> G -> B -> A -> Off
        if (player.keys.keyAccept && isColor)
        {
            _colorCompIndex++;
            if (_colorCompIndex > 3) _colorCompIndex = -1;
            PlayAccept();
            return;
        }

        if (player.keys.keyLeft || player.keys.keyRight || isBool && player.keys.keyAccept)
        {
            var right = player.keys.keyRight;
            if (isColor && _colorCompIndex != -1)
                ModifyColorComponent(config, _colorCompIndex, right);
            else
                ModifyValue(config, right, _fast);

            valueChanged = true;
        }
        else if (player.keys.keyAccept && !isColor && !config.IsAction)
        {
            _fast = !_fast;
        }

        if (valueChanged)
        {
            SaS2DevTools.Instance.Config.Save();
            PlaySelect();
        }

        if (player.keys.keyCancel)
        {
            PlayCancel();
            Deactivate();
            player.menu.GetLevelByScreen(_returnScreen).Activate();
        }
    }

    private static void ModifyValue(DevConfigEntry config, bool increase, bool fast = false)
    {
        var type = config.SettingType;

        if (type == typeof(bool))
        {
            config.BoolValue = !config.BoolValue;
        }
        else if (type.IsEnum)
        {
            CycleEnum(config, increase);
        }
        else if (type == typeof(int))
        {
            config.IntValue += increase ? 1 : -1;
        }
        else if (type == typeof(float))
        {
            if (fast) config.FloatValue += increase ? 0.5f : -0.5f;
            else config.FloatValue += increase ? 0.05f : -0.05f;
        }
        else if (type == typeof(string))
        {
            // Cycle through the registered acceptable values list (if provided).
            // Color strings (4-part comma format) are handled separately via the
            // color picker and never reach this branch.
            var acceptable = config.AcceptableValues;
            if (acceptable == null || acceptable.Length == 0) return;

            var current = (string)config.BoxedValue ?? "";
            var idx = Array.IndexOf(acceptable, current);

            // If the stored value isn't in the list, snap to the first item
            if (idx < 0) idx = 0;
            else
                idx = increase
                    ? (idx + 1) % acceptable.Length
                    : (idx - 1 + acceptable.Length) % acceptable.Length;

            config.StringValue = acceptable[idx];
        }
    }

    private static void ModifyColorComponent(DevConfigEntry config, int comp, bool inc)
    {
        var parts = ((string)config.BoxedValue).Split(',');
        if (parts.Length != 4) return;

        if (comp < 3) // RGB channels (0-255)
        {
            if (int.TryParse(parts[comp], out var v))
            {
                v = Clamp(inc ? v + 5 : v - 5, 0, 255);
                parts[comp] = v.ToString();
            }
        }
        else // Alpha channel (0.0-1.0)
        {
            if (float.TryParse(parts[comp], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                v = (float)Math.Round(Clamp(inc ? v + 0.05f : v - 0.05f, 0f, 1f), 2);
                parts[comp] = v.ToString(CultureInfo.InvariantCulture);
            }
        }

        config.BoxedValue = string.Join(",", parts);
    }

    private static void CycleEnum(DevConfigEntry entry, bool forward)
    {
        var values = Enum.GetValues(entry.SettingType);
        var index = Array.IndexOf(values, entry.BoxedValue);
        index = forward
            ? (index + 1) % values.Length
            : (index - 1 + values.Length) % values.Length;
        entry.BoxedValue = values.GetValue(index);
    }

    private static bool IsColorString(DevConfigEntry entry) =>
        entry.SettingType == typeof(string) && ((string)entry.BoxedValue)?.Split(',').Length == 4;

    private static bool IsBool(DevConfigEntry entry) => entry.SettingType == typeof(bool);

    /// Calculates tab rows that fit inside boxWidth, and returns total height used.
    /// Also draws the tabs if draw == true.
    private float LayoutAndDrawTabs(float boxX, float boxY, float boxWidth, bool draw)
    {
        if (!HasTabs) return 0f;

        var tabY = boxY + 6f;
        var tabH = TabBarHeight - 6f;

        // Pre‑measure each tab width
        var tabWidths = new float[_tabs.Count];
        for (var t = 0; t < _tabs.Count; t++)
            tabWidths[t] = Text.GetStringSpace(new StringBuilder(_tabs[t]), 0.65f, player, 1) + TabPadX * 2f;

        // Build rows
        var rows = new List<List<int>>(); // list of tab indices per row
        var currentRow = new List<int>();
        var currentRowWidth = 0f;
        const float tabGap = 2f;

        for (var t = 0; t < _tabs.Count; t++)
        {
            var w = tabWidths[t];
            if (currentRow.Count > 0 && currentRowWidth + w + tabGap > boxWidth)
            {
                rows.Add(currentRow);
                currentRow = [];
                currentRowWidth = 0f;
            }

            currentRow.Add(t);
            currentRowWidth += w + (currentRow.Count > 1 ? tabGap : 0f);
        }

        if (currentRow.Count > 0) rows.Add(currentRow);

        var totalTabHeight = rows.Count * TabBarHeight;

        if (!draw) return totalTabHeight;

        // Draw each row, centered
        for (var r = 0; r < rows.Count; r++)
        {
            var rowIndices = rows[r];
            var rowTotalWidth = rowIndices.Sum(idx => tabWidths[idx]) + (rowIndices.Count - 1) * tabGap;
            var startX = boxX + (boxWidth - rowTotalWidth) / 2f;
            var curX = startX;
            var rowY = tabY + r * TabBarHeight;

            foreach (var idx in rowIndices)
            {
                var tw = tabWidths[idx];
                var rect = new Rectangle((int)curX, (int)rowY, (int)tw, (int)tabH);

                if (idx == _currentTabIndex)
                {
                    UIRender.DrawRect(rect, 0.35f, 3, 1f, 1f, UIRender.interfaceTex);
                    Text.DrawText(new StringBuilder(_tabs[idx]),
                        new Vector2(curX + tw / 2f, rowY + tabH * 0.72f),
                        Color.Yellow, 0.65f, 1);
                }
                else
                {
                    UIRender.DrawRect(rect, 0.15f, 0, 1f, 1f, UIRender.interfaceTex);
                    Text.DrawText(new StringBuilder(_tabs[idx]),
                        new Vector2(curX + tw / 2f, rowY + tabH * 0.72f),
                        new Color(0.7f, 0.7f, 0.7f, 1f), 0.65f, 1);
                }

                curX += tw + tabGap;
            }
        }

        return totalTabHeight;
    }

    public override void Draw()
    {
        base.Draw();
        var vp = Game1.Instance.GraphicsDevice.Viewport;
        var boxWidth = vp.Width * 0.5f;
        var boxHeight = vp.Height * 0.8f;

        // Always assume local coop; menu takes place in respective player's side
        var margin = boxWidth * 0.025f;
        var isMainPlayer = player.ID == GameSessionMgr.gameSession.mainPlayerIdx;
        var boxX = isMainPlayer ? 0f - margin : vp.Width * 0.5f + margin;
        var boxY = (vp.Height - boxHeight) / 2f;

        UIRender.DrawRect(new Rectangle((int)boxX, (int)boxY, (int)boxWidth, (int)boxHeight), 0.85f, 0, 1f, 1f,
            UIRender.interfaceTex);

        // Draw tabs
        var usedTabHeight = LayoutAndDrawTabs(boxX, boxY, boxWidth, true);

        // Config list area
        _listX = boxX + 40f;
        _listY = boxY + TopMargin + usedTabHeight;
        _listWidth = boxWidth - 80f;
        var listVisibleHeight = boxHeight - TopMargin - usedTabHeight - BottomMargin;
        _currentListVisibleHeight = listVisibleHeight;

        var currentY = _listY - _scrollOffset;

        string lastMod = null;
        for (var i = 0; i < _displayedConfigs.Count; i++)
        {
            var cfg = _displayedConfigs[i];
            var selected = i == _selectedIndex;

            // Section header (only when there are no tabs)
            if (!HasTabs && cfg.ModName != lastMod)
            {
                if (lastMod != null) currentY += 20f;
                if (currentY + SectionHeight > _listY && currentY < _listY + listVisibleHeight)
                {
                    Text.DrawText(new StringBuilder(cfg.ModName), new Vector2(_listX, currentY + 35f),
                        new Color(0.6f, 0.8f, 1f, 1f), 0.85f, 0);
                    UIRender.DrawDivider(new Vector2(_listX + _listWidth / 2f, currentY + 45f), 0.7f, 1f, 1f, 0.7f,
                        0.5f, 1, UIRender.interfaceTex);
                }

                currentY += SectionHeight;
                lastMod = cfg.ModName;
            }

            // Config row
            if (currentY + ItemHeight > _listY && currentY < _listY + listVisibleHeight)
            {
                if (selected)
                    UIRender.DrawRect(new Rectangle((int)_listX, (int)currentY, (int)_listWidth, (int)ItemHeight), 0.2f,
                        3, 1f, 1f, UIRender.interfaceTex);

                var textColor = selected ? Color.Yellow : Color.White;
                var textY = currentY + ItemHeight * 0.75f;

                Text.DrawText(new StringBuilder(cfg.DisplayName), new Vector2(_listX + 10, textY), textColor, 0.7f, 0);

                var valStr = FormatValue(cfg, selected);
                Text.DrawText(new StringBuilder(valStr), new Vector2(_listX + _listWidth - ValueWidth, textY),
                    textColor, 0.7f, 0);
            }

            currentY += ItemHeight;
        }

        DrawHelpBar(boxX, boxWidth, vp.Height);
    }

    private void DrawHelpBar(float boxX, float boxWidth, float vpHeight)
    {
        var useKeyboard = player.inputProfile.keyMouseEnable;
        var action = useKeyboard ? "[Space]" : "[a]";

        var sb = new StringBuilder();
        sb.Append($"\u02ef{action}\u02f0 Cycle/Edit  |  \u02ef[ll]/[lr]\u02f0 Change  |  \u02ef[b]\u02f0 Back");

        if (HasTabs) sb.Append(useKeyboard ? "  |  \u02ef[Z]/[X]\u02f0 Tab" : "  |  \u02ef[lt]/[rt]\u02f0 Tab");

        Text.DrawText(sb, new Vector2(boxX + boxWidth / 2f, vpHeight - 40),
            Color.White, 0.6f, 1, player, 1);
    }

    private string FormatValue(DevConfigEntry config, bool selected)
    {
        // Action items show a button prompt
        if (config.IsAction)
            return "[Activate]";

        // Color string: show component highlight when actively editing
        if (IsColorString(config) && selected && _colorCompIndex != -1)
        {
            var p = ((string)config.BoxedValue).Split(',');
            p[_colorCompIndex] = ">" + p[_colorCompIndex] + "<";
            return string.Join(",", p);
        }

        // Bool
        if (config.SettingType == typeof(bool)) return config.BoolValue ? "On" : "Off";

        // Float
        if (config.SettingType == typeof(float)) return config.FloatValue.ToString("F2");

        // String with acceptable-values list: show "Value (N/Total)"
        if (config.SettingType == typeof(string) &&
            config.AcceptableValues is { Length: > 0 } values &&
            !IsColorString(config))
        {
            var current = (string)config.BoxedValue ?? "";
            var idx = Array.IndexOf((Array)values, current);
            var pos = idx >= 0 ? idx + 1 : 1; // snap display to 1 if value is unexpected
            return $"{current} ({pos}/{values.Length})";
        }

        return config.BoxedValue?.ToString() ?? "null";
    }

    // Scrolling helpers
    private float GetItemY(int index)
    {
        float y = 0;
        string last = null;

        if (!HasTabs)
        {
            for (var i = 0; i <= index; i++)
            {
                if (_displayedConfigs[i].ModName != last)
                {
                    y += last == null ? SectionHeight : SectionHeight + 20f;
                    last = _displayedConfigs[i].ModName;
                }

                if (i < index) y += ItemHeight;
            }
        }
        else
        {
            y = index * ItemHeight;
        }

        return y;
    }

    private void EnsureVisible()
    {
        if (_displayedConfigs.Count == 0) return;

        var itemTop = GetItemY(_selectedIndex);
        var itemBottom = itemTop + ItemHeight;

        // Use the visible area that was computed during the last Draw()
        var visibleTop = _scrollOffset;
        var visibleBottom = _scrollOffset + _currentListVisibleHeight;

        if (itemTop < visibleTop)
            _scrollOffset = itemTop;
        else if (itemBottom > visibleBottom)
            _scrollOffset = itemBottom - _currentListVisibleHeight;

        _scrollOffset = Math.Max(0f, _scrollOffset);
    }

    private new void PlaySelect() => AccessTools.Method(typeof(LevelBase), "PlaySelect")?.Invoke(this, null);
    private new void PlayAccept() => AccessTools.Method(typeof(LevelBase), "PlayAccept")?.Invoke(this, null);
    private new void PlayCancel() => AccessTools.Method(typeof(LevelBase), "PlayCancel")?.Invoke(this, null);
    private new bool CanInput() => (bool)AccessTools.Method(typeof(LevelBase), "CanInput")?.Invoke(this, null)!;

    // Self-initialisation: build the config list from SaS2DevTools.Instance
    private List<DevConfigEntry> BuildDevConfigList()
    {
        var list = new List<DevConfigEntry>();
        var devTools = SaS2DevTools.Instance;
        if (devTools == null) return list;

        var cheats = devTools.GetCheats(_currentPlayerId);
        var global = devTools.Global;
        if (cheats == null || global == null) return list;

        string cat;
        var order = 0;
        // TOGGLES
        AddBool(list, cat = "Toggles", "Godmode", () => cheats.Godmode.Value, v => cheats.Godmode.Value = v, order: order += 1);
        AddBool(list, cat, "Invulnerable", () => cheats.Invulnerable.Value, v => cheats.Invulnerable.Value = v, order: order += 1);
        AddBool(list, cat, "Infinite Stamina", () => cheats.InfStamina.Value, v => cheats.InfStamina.Value = v, order: order += 1);
        AddBool(list, cat, "Infinite Poise", () => cheats.InfPoise.Value, v => cheats.InfPoise.Value = v, order: order += 1);
        AddBool(list, cat, "Unstaggerable", () => cheats.Unstaggerable.Value, v => cheats.Unstaggerable.Value = v, order: order += 1);
        AddBool(list, cat, "Infinite Jumps", () => cheats.InfJumps.Value, v => cheats.InfJumps.Value = v, order: order += 1);
        AddBool(list, cat, "Play Jump Sound", () => cheats.PlayJumpSnd.Value, v => cheats.PlayJumpSnd.Value = v, order: order += 1);
        AddBool(list, cat, "Increase Jump Amount", () => cheats.IncreaseJumps.Value, v => cheats.IncreaseJumps.Value = v, order: order += 1);
        AddInt(list, cat, "Extra Jump Amount", () => cheats.ExtraJumps.Value, v => cheats.ExtraJumps.Value = v, order: order += 1);
        AddBool(list, cat, "No Fall Damage", () => cheats.NoFallDmg.Value, v => cheats.NoFallDmg.Value = v, order: order += 1);
        AddBool(list, cat, "NoClip", () => cheats.NoClip.Value, v => cheats.NoClip.Value = v, order: order += 1);
        AddFloat(list, cat, "NoClipSpeed", () => cheats.NoClipSpeed.Value, v => cheats.NoClipSpeed.Value = v, order: order += 1);

        // STATS & ACTIONS
        AddLong(list, cat = "Stats and Actions", "Silver", () => player.stats.silver, v => player.stats.silver = v, order: order += 1);
        AddLong(list, cat, "XP", () => player.stats.xp, v => player.stats.xp = v, order: order += 1);
        AddAction(list, cat, "Teleport to Mage", TeleportToMageArena, order += 1);
        AddAction(list, cat, "Refill Health/Stam", () =>
        {
            var c = (Character)AccessTools.Method(typeof(Player), "GetCharacter").Invoke(player, null);
            if (c == null) return;
            c.hp = 9999f;
            c.stamina = 9999f;
        }, order += 1);

        // CAMERA
        AddBool(list, cat = "Camera", "Block Input in Freecam", () => global.BlockInputInFreecam.Value, v => global.BlockInputInFreecam.Value = v, order: order += 1);
        AddFloat(list, cat, "Camera Speed (pixels/tick)", () => global.CamSpeed, v => global.CamSpeed = v, order: order += 1);
        AddFloat(list, cat, "Camera Zoom", () => global.CamZoom, v => global.CamZoom = v, order: order += 1);
        AddFloat(list, cat, "Camera Zoom Multiplier (Non-freecam)", () => global.CamZoomNonFreecam.Value, v => global.CamZoomNonFreecam.Value = v, order: order += 1);
        AddAction(list, cat, "Save current speed/zoom as defaults", global.SaveDefaults, order += 1);

        // VISIBILITY
        AddBool(list, cat = "Visibility", "Show HUD", () => global.ShowHud.Value, v => global.ShowHud.Value = v, order: order += 1);
        AddBool(list, cat, "Show Debug HUD", () => global.ShowDebugHud.Value, v => global.ShowDebugHud.Value = v, order: order += 1);
        AddBool(list, cat, "Show Player", () => global.ShowPlayer.Value, v => global.ShowPlayer.Value = v, order: order += 1);

        // Per-monster visibility toggles
        for (var i = 0; i < GlobalSettings.MonsterTypeLabels.Length; i++)
        {
            var label = GlobalSettings.MonsterTypeLabels[i];
            var idx = i;
            AddBool(list, cat, $"Show {label}",
                () => global.ShowMonsterType[idx].Value,
                v => global.ShowMonsterType[idx].Value = v, order: order += 1);
        }

        return list;
    }

    private static void AddBool(List<DevConfigEntry> list, string mod, string name, Func<bool> getter, Action<bool> setter, int order = 0)
    {
        list.Add(new DevConfigEntry(mod, name, typeof(bool), getter, setter, order));
    }

    private static void AddInt(List<DevConfigEntry> list, string mod, string name, Func<int> getter, Action<int> setter, int order = 0)
    {
        list.Add(new DevConfigEntry(mod, name, typeof(int), getter, setter, order));
    }

    private static void AddFloat(List<DevConfigEntry> list, string mod, string name, Func<float> getter, Action<float> setter, int order = 0)
    {
        list.Add(new DevConfigEntry(mod, name, typeof(float), getter, setter, order));
    }

    private static void AddLong(List<DevConfigEntry> list, string mod, string name, Func<long> getter, Action<long> setter, int order = 0)
    {
        list.Add(new DevConfigEntry(mod, name, typeof(long), getter, setter, order));
    }

    private static void AddAction(List<DevConfigEntry> list, string mod, string name, Action action, int order = 0)
    {
        list.Add(new DevConfigEntry(mod, name, typeof(void), null, null, action, order));
    }

    private void TeleportToMageArena()
    {
        var session = GameSessionMgr.gameSession;
        if (session?.mageMgr == null) return;
        var character = (Character)AccessTools.Method(typeof(Player), "GetCharacter").Invoke(player, null);
        if (character is not { exists: true }) return;
        var mageArray = (Mage[])AccessTools.Field(typeof(MageMgr), "mage").GetValue(session.mageMgr);
        foreach (var m in mageArray)
        {
            if (!m.exists || m.charIdx < 0) continue;
            var mageChar = CharMgr.character[m.charIdx];
            character.loc = mageChar.loc;
            if (character.warp == null) return;
            character.warp.active = true;
            character.warp.warpDest = mageChar.loc;
            return;
        }
    }

    // Internal equivalent of RegisteredConfig
    private class DevConfigEntry(
        string mod,
        string name,
        Type type,
        Func<object> getter,
        Action<object> setter,
        Action action = null,
        int order = 0)
    {
        public readonly string ModName = mod;
        public readonly string DisplayName = name;
        public readonly int Order = order;
        public readonly Type SettingType = type;
        public readonly string[] AcceptableValues = null;

        public bool IsAction => action != null;

        public DevConfigEntry(string mod, string name, Type type, Func<bool> getter, Action<bool> setter, int order = 0)
            : this(mod, name, type, () => getter(), (Action<object>)(v => setter((bool)v)), null, order) { }

        public DevConfigEntry(string mod, string name, Type type, Func<int> getter, Action<int> setter, int order = 0)
            : this(mod, name, type, () => getter(), (Action<object>)(v => setter((int)v)), null, order) { }

        public DevConfigEntry(string mod, string name, Type type, Func<float> getter, Action<float> setter, int order = 0)
            : this(mod, name, type, () => getter(), (Action<object>)(v => setter((float)v)), null, order) { }

        public DevConfigEntry(string mod, string name, Type type, Func<long> getter, Action<long> setter, int order = 0)
            : this(mod, name, type, () => getter(), (Action<object>)(v => setter((long)v)), null, order) { }

        public object BoxedValue
        {
            get => action != null ? null : getter();
            set
            {
                if (action == null && setter != null) setter(value);
            }
        }

        public bool BoolValue
        {
            get => (bool)getter();
            set => setter(value);
        }

        public long LongValue
        {
            get => (long)getter();
            set => setter(value);
        }

        public int IntValue
        {
            get => (int)getter();
            set => setter(value);
        }

        public float FloatValue
        {
            get => (float)getter();
            set => setter(value);
        }

        public string StringValue
        {
            get => (string)getter();
            set => setter(value);
        }

        public void InvokeAction() => action?.Invoke();
    }
}