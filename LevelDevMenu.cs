using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Menumancer.hud;
using Menumancer.UIFormat;
using ProjectMage;
using ProjectMage.character;
using ProjectMage.gamestate;
using ProjectMage.gamestate.mage;
using ProjectMage.player;
using ProjectMage.player.menu;
using Color = Common.Color;
using Rectangle = Common.Rectangle;
using Vector2 = Common.Vector2;

namespace SaS2DevTools;

public class LevelDevMenu : LevelBase
{
    private List<DevItem> _items = [];
    private int _selectedIndex;
    private float _scrollOffset;
    private readonly int _returnScreen;
    
    // UI Styling
    private const float ItemHeight = 40f;
    private const float SectionHeight = 60f;
    private const float ValueWidth = 220f;

    public LevelDevMenu(Player player, int returnToScreen = 10)
    {
        this.player = player;
        _returnScreen = returnToScreen;
        Init("DevMenu", player);
    }

    public sealed override void Init(string strScreen, Player plr)
    {
        base.Init(strScreen, plr);
        if (!screen.uiFlag.Contains(9)) screen.uiFlag.Add(9); // Stop player movement
        
        var cheats = SaS2DevTools.Instance?.GetCheats(plr.ID);
        if (cheats == null)
        {
            SaS2DevTools.Instance?.Log.LogError($"Failed to get PlayerCheats for player {plr.ID}");
            _items = [];
            return;
        }

        _items =
        [
            new DevItem("TOGGLES", "Godmode", () => cheats.Godmode.Value, v => cheats.Godmode.Value = (bool)v),
            new DevItem("TOGGLES", "Invulnerable", () => cheats.Invulnerable.Value, v => cheats.Invulnerable.Value = (bool)v),
            new DevItem("TOGGLES", "Infinite Stamina", () => cheats.InfStamina.Value, v => cheats.InfStamina.Value = (bool)v),
            new DevItem("TOGGLES", "Infinite Poise", () => cheats.InfPoise.Value, v => cheats.InfPoise.Value = (bool)v),
            new DevItem("TOGGLES", "Unstaggerable", () => cheats.Unstaggerable.Value, v => cheats.Unstaggerable.Value = (bool)v),
            new DevItem("TOGGLES", "Infinite Jumps", () => cheats.InfJumps.Value, v => cheats.InfJumps.Value = (bool)v),

            new DevItem("STATS & ACTIONS", "Silver", () => plr.stats.silver, v => plr.stats.silver = (long)v),
            new DevItem("STATS & ACTIONS", "XP", () => plr.stats.xp, v => plr.stats.xp = (long)v),
            new DevItem("STATS & ACTIONS", "Teleport to Mage", null, null, TeleportToMageArena),
            new DevItem("STATS & ACTIONS", "Refill Health/Stam", null, null, () =>
            {
                var c = (Character)AccessTools.Method(typeof(Player), "GetCharacter").Invoke(plr, null);
                if (c == null) return;
                c.hp = 9999f;
                c.stamina = 9999f;
            })
        ];
    }

    public override void Update(Character character, float frameTime)
    {
        if (!CanInput()) return;

        if (player.keys.keyUp || player.keys.keyDown)
        {
            var dir = player.keys.keyUp ? -1 : 1;
            _selectedIndex = (_selectedIndex + dir + _items.Count) % _items.Count;
            PlaySelect();
            EnsureVisible();
            return;
        }

        var item = _items[_selectedIndex];

        if (player.keys.keyAccept)
        {
            if (item.IsAction) { item.Action?.Invoke(); PlayAccept(); }
            else if (item.IsToggle) { item.BoolValue = !item.BoolValue; PlayAccept(); }
        }

        if (player.keys.keyLeft || player.keys.keyRight)
        {
            if (item.IsLong)
            {
                long change = player.keys.keyRight ? 1000 : -1000;
                item.LongValue += change;
                PlaySelect();
            }
        }

        if (player.keys.keyCancel)
        {
            PlayCancel();
            Deactivate();
            player.menu.GetLevelByScreen(_returnScreen).Activate();
        }
    }

    public override void Draw()
    {
        base.Draw();
        var vp = Game1.Instance.GraphicsDevice.Viewport;
        var boxWidth = Math.Min(900, vp.Width * 0.7f);
        var boxHeight = vp.Height * 0.7f;

        // always assume local coop, therefore menu takes place in respective player side
        // done because controller always opens session, see comment commit to see how to revert
        var halfWidth = vp.Width * 0.5f; 
        var isMainPlayer = player.ID == GameSessionMgr.gameSession.mainPlayerIdx;
        var boxX = isMainPlayer ? halfWidth * 0.5f - boxWidth * 0.5f : halfWidth + halfWidth * 0.5f - boxWidth * 0.5f;
        var boxY = (vp.Height - boxHeight) / 2f;

        UIRender.DrawRect(new Rectangle((int)boxX, (int)boxY, (int)boxWidth, (int)boxHeight), 0.85f, 0, 1f, 1f, UIRender.interfaceTex);

        float listX = boxX + 40f, listY = boxY + 40f, listWidth = boxWidth - 80f;
        var visibleH = boxHeight - 80f;
        var currentY = listY - _scrollOffset;

        string lastCat = null;
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var selected = i == _selectedIndex;

            if (item.Category != lastCat)
            {
                if (lastCat != null) currentY += 20f;
                if (currentY + SectionHeight > listY && currentY < listY + visibleH)
                {
                    Text.DrawText(new StringBuilder(item.Category), new Vector2(listX, currentY + 35f), new Color(0.6f, 0.8f, 1f, 1f), 0.85f, 0);
                    UIRender.DrawDivider(new Vector2(listX + listWidth / 2f, currentY + 45f), 0.7f, 1f, 1f, 0.7f, 0.5f, 1, UIRender.interfaceTex);
                }
                currentY += SectionHeight;
                lastCat = item.Category;
            }

            if (currentY + ItemHeight > listY && currentY < listY + visibleH)
            {
                if (selected) UIRender.DrawRect(new Rectangle((int)listX, (int)currentY, (int)listWidth, (int)ItemHeight), 0.2f, 3, 1f, 1f, UIRender.interfaceTex);

                var color = selected ? Color.Yellow : Color.White;
                var textY = currentY + ItemHeight * 0.75f;

                Text.DrawText(new StringBuilder(item.Name), new Vector2(listX + 10, textY), color, 0.7f, 0);
                
                var valStr = item.GetFormattedValue();
                Text.DrawText(new StringBuilder(valStr), new Vector2(listX + listWidth - ValueWidth, textY), color, 0.7f, 0);
            }
            currentY += ItemHeight;
        }

        var action = player.inputProfile.keyMouseEnable ? "[Space]" : "[a]";
        var help = new StringBuilder($"\u02ef{action}\u02f0 Activate/Edit  |  \u02ef[ll]/[lr]\u02f0 Change  |  \u02ef[b]\u02f0 Back");
        // ReSharper disable once PossibleLossOfFraction
        Text.DrawText(help, new Vector2(boxX + boxWidth / 2f, vp.Height - 40), Color.White, 0.6f, 1, player, 1);
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
            character.warp.active = true; character.warp.warpDest = mageChar.loc;
            return;
        }
    }

    private void EnsureVisible()
    {
        float y = GetItemY(_selectedIndex), viewH = Game1.Instance.GraphicsDevice.Viewport.Height * 0.7f - 80f;
        if (y < _scrollOffset) _scrollOffset = y;
        else if (y + ItemHeight > _scrollOffset + viewH) _scrollOffset = y + ItemHeight - viewH;
    }

    private float GetItemY(int index)
    {
        float y = 0; string last = null;
        for (var i = 0; i <= index; i++) {
            if (_items[i].Category != last) { y += last == null ? SectionHeight : SectionHeight + 20f; last = _items[i].Category; }
            if (i < index) y += ItemHeight;
        }
        return y;
    }

    private new void PlaySelect() => AccessTools.Method(typeof(LevelBase), "PlaySelect")?.Invoke(this, null);
    private new void PlayAccept() => AccessTools.Method(typeof(LevelBase), "PlayAccept")?.Invoke(this, null);
    private new void PlayCancel() => AccessTools.Method(typeof(LevelBase), "PlayCancel")?.Invoke(this, null);
    private new bool CanInput() => (bool)AccessTools.Method(typeof(LevelBase), "CanInput")?.Invoke(this, null)!;

    private class DevItem(string cat, string name, Func<object> getter, Action<object> setter, Action action = null, string staticVal = null)
    {
        public readonly string Category = cat;
        public readonly string Name = name;
        public readonly string StaticVal = staticVal;
        public readonly Action Action = action;
        public bool BoolValue { get => (bool)getter(); set => setter(value); }
        public long LongValue { get => (long)getter(); set => setter(value); }
        public bool IsToggle => getter?.Invoke() is bool;
        public bool IsLong => getter?.Invoke() is long;
        public bool IsAction => Action != null;

        public string GetFormattedValue() {
            if (StaticVal != null) return StaticVal;
            if (IsAction) return "[Activate]";
            if (IsToggle) return BoolValue ? "On" : "Off";
            if (IsLong) return LongValue.ToString("N0");
            return "";
        }
    }
}