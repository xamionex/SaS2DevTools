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
    private bool _fast;

    // UI Constants
    private const float ItemHeight = 40f;
    private const float SectionHeight = 60f;
    private float _listX;
    private float _listY;
    private float _listWidth;
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
        if (!screen.uiFlag.Contains(9)) screen.uiFlag.Add(9);
        var cheats = SaS2DevTools.Instance?.GetCheats(plr.ID);
        var global = SaS2DevTools.Instance?.Global;
        if (cheats == null || global == null)
        {
            SaS2DevTools.Instance?.Log.LogError($"Failed to get settings for player {plr.ID}");
            _items = [];
            return;
        }

        _items =
        [
            new DevItem("TOGGLES", "Godmode",
                () => cheats.Godmode.Value,
                v => cheats.Godmode.Value = (bool)v),
            new DevItem("TOGGLES", "Invulnerable",
                () => cheats.Invulnerable.Value,
                v => cheats.Invulnerable.Value = (bool)v),
            new DevItem("TOGGLES", "Infinite Stamina",
                () => cheats.InfStamina.Value,
                v => cheats.InfStamina.Value = (bool)v),
            new DevItem("TOGGLES", "Infinite Poise",
                () => cheats.InfPoise.Value,
                v => cheats.InfPoise.Value = (bool)v),
            new DevItem("TOGGLES", "Unstaggerable",
                () => cheats.Unstaggerable.Value,
                v => cheats.Unstaggerable.Value = (bool)v),
            new DevItem("TOGGLES", "Infinite Jumps",
                () => cheats.InfJumps.Value,
                v => cheats.InfJumps.Value = (bool)v),
            new DevItem("TOGGLES", "Play Jump Sound",
                () => cheats.PlayJumpSnd.Value,
                v => cheats.PlayJumpSnd.Value = (bool)v),
            new DevItem("TOGGLES", "Increase Jump Amount",
                () => cheats.IncreaseJumps.Value,
                v => cheats.IncreaseJumps.Value = (bool)v),
            new DevItem("TOGGLES", "Extra Jump Amount",
                () => cheats.ExtraJumps.Value,
                v => cheats.ExtraJumps.Value = (int)v),
            new DevItem("TOGGLES", "No Fall Damage",
                () => cheats.NoFallDmg.Value,
                v => cheats.NoFallDmg.Value = (bool)v),
            new DevItem("TOGGLES", "NoClip",
                () => cheats.NoClip.Value,
                v => cheats.NoClip.Value = (bool)v),
            new DevItem("TOGGLES", "NoClipSpeed",
                () => cheats.NoClipSpeed.Value,
                v => cheats.NoClipSpeed.Value = (float)v),

            new DevItem("STATS & ACTIONS", "Silver",
                () => plr.stats.silver,
                v => plr.stats.silver = (long)v),
            new DevItem("STATS & ACTIONS", "XP",
                () => plr.stats.xp,
                v => plr.stats.xp = (long)v),
            new DevItem("STATS & ACTIONS", "Teleport to Mage",
                null,
                null,
                TeleportToMageArena),
            new DevItem(
                "STATS & ACTIONS", "Refill Health/Stam",
                null,
                null,
                () =>
                {
                    var c = (Character)AccessTools.Method(typeof(Player), "GetCharacter").Invoke(plr, null);
                    if (c == null) return;
                    c.hp = 9999f;
                    c.stamina = 9999f;
                }),

            // Toggling here is stupid
            // Could make it work by changing the bool only when menu is closed, but I'm lazy
            //new DevItem("CAMERA", "Free Camera (active)",
            //    () => global.FreeCamActive,
            //    v => global.ActivateFreecam = (bool)v),
            
            new DevItem("CAMERA", "Block Input in Freecam",
                () => global.BlockInputInFreecam.Value,
                v => global.BlockInputInFreecam.Value = (bool)v),
            new DevItem("CAMERA", "Camera Speed (pixels/tick)",
                () => global.CamSpeed,
                v => global.CamSpeed = (float)v),
            new DevItem("CAMERA", "Camera Zoom",
                () => global.CamZoom,
                v => global.CamZoom = (float)v),
            new DevItem("CAMERA", "Camera Zoom Multiplier (Non-freecam)",
                () => global.CamZoomNonFreecam.Value,
                v => global.CamZoomNonFreecam.Value = (float)v),
            // useless in menu
            //new DevItem("CAMERA", "Reset camera to player",
            //    null,
            //    null,
            //    global.ResetCamToPlayer),
            new DevItem("CAMERA", "Save current speed/zoom as defaults",
                null,
                null,
                global.SaveDefaults),

            new DevItem("VISIBILITY", "Show HUD",
                () => global.ShowHud.Value,
                v => global.ShowHud.Value = (bool)v),
            new DevItem("VISIBILITY", "Show Debug HUD",
                () => global.ShowDebugHud.Value,
                v => global.ShowDebugHud.Value = (bool)v),
            new DevItem("VISIBILITY", "Show Player",
                () => global.ShowPlayer.Value,
                v => global.ShowPlayer.Value = (bool)v),
        ];

        // Add per monster type visibility toggles
        for (var i = 0; i < GlobalSettings.MonsterTypeLabels.Length; i++)
        {
            var label = GlobalSettings.MonsterTypeLabels[i];
            var idx = i; // capture for lambda
            _items.Add(new DevItem("VISIBILITY", $"Show {label}",
                () => global.ShowMonsterType[idx].Value,
                v => global.ShowMonsterType[idx].Value = (bool)v));
        }
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
        if (player.keys.keyAccept && !item.IsAction && !item.IsBool)
        {
            _fast = !_fast;
            PlayAccept();
            return;
        }

        if (!item.IsAction && (player.keys.keyLeft || player.keys.keyRight || (player.keys.keyAccept && item.IsBool)))
        {
            var right = player.keys.keyRight || (player.keys.keyAccept && item.IsBool);
            if (item.IsBool)
            {
                item.BoolValue = !item.BoolValue;
            }
            else if (item.IsLong)
            {
                var change = right ? 1000 : -1000;
                item.LongValue += change;
            }
            else if (item.IsInt)
            {
                var change = right ? 1 : -1;
                item.IntValue += change;
            }
            else if (item.IsFloat)
            {
                var delta = _fast ? right ? 1f : -1f :
                    right ? 0.1f : -0.1f;
                item.FloatValue += delta;
            }

            PlaySelect();
        }

        if (player.keys.keyAccept && item.IsAction)
        {
            item.Action?.Invoke();
            PlayAccept();
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

        // Local coop positioning
        var halfWidth = vp.Width * 0.5f;
        var isMainPlayer = player.ID == GameSessionMgr.gameSession.mainPlayerIdx;
        var boxX = isMainPlayer ? halfWidth * 0.5f - boxWidth * 0.5f : halfWidth + halfWidth * 0.5f - boxWidth * 0.5f;
        var boxY = (vp.Height - boxHeight) / 2f;
        UIRender.DrawRect(new Rectangle((int)boxX, (int)boxY, (int)boxWidth, (int)boxHeight), 0.85f, 0, 1f, 1f,
            UIRender.interfaceTex);
        _listX = boxX + 40f;
        _listY = boxY + 40f;
        _listWidth = boxWidth - 80f;
        var listVisibleHeight = boxHeight - 80f;
        var currentY = _listY - _scrollOffset;
        string lastCat = null;
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var selected = i == _selectedIndex;

            // Category header
            if (item.Category != lastCat)
            {
                if (lastCat != null) currentY += 20f;
                if (currentY + SectionHeight > _listY && currentY < _listY + listVisibleHeight)
                {
                    Text.DrawText(new StringBuilder(item.Category), new Vector2(_listX, currentY + 35f),
                        new Color(0.6f, 0.8f, 1f, 1f), 0.85f, 0);
                    UIRender.DrawDivider(new Vector2(_listX + _listWidth / 2f, currentY + 45f), 0.7f, 1f, 1f, 0.7f,
                        0.5f, 1, UIRender.interfaceTex);
                }

                currentY += SectionHeight;
                lastCat = item.Category;
            }

            // Draw item row
            if (currentY + ItemHeight > _listY && currentY < _listY + listVisibleHeight)
            {
                if (selected)
                    UIRender.DrawRect(new Rectangle((int)_listX, (int)currentY, (int)_listWidth, (int)ItemHeight), 0.2f,
                        3, 1f, 1f, UIRender.interfaceTex);
                var textColor = selected ? Color.Yellow : Color.White;
                var textY = currentY + ItemHeight * 0.75f;
                Text.DrawText(new StringBuilder(item.Name), new Vector2(_listX + 10, textY), textColor, 0.7f, 0);
                var valStr = FormatValue(item);
                Text.DrawText(new StringBuilder(valStr), new Vector2(_listX + _listWidth - ValueWidth, textY),
                    textColor, 0.7f, 0);
            }

            currentY += ItemHeight;
        }

        var action = player.inputProfile.keyMouseEnable ? "[Space]" : "[a]";
        var help = new StringBuilder(
            $"\u02ef{action}\u02f0 Activate/Edit  |  \u02ef[ll]/[lr]\u02f0 Change  |  \u02ef[b]\u02f0 Back");
        Text.DrawText(help, new Vector2(boxX + boxWidth / 2f, vp.Height - 40), Color.White, 0.6f, 1, player, 1);
    }

    private static string FormatValue(DevItem item)
    {
        if (item.IsAction) return "[Activate]";
        if (item.IsBool) return item.BoolValue ? "On" : "Off";
        if (item.IsLong) return item.LongValue.ToString("N0");
        if (item.IsInt) return item.IntValue.ToString("N0");
        if (item.IsFloat) return item.FloatValue.ToString("F1");
        return "";
    }

    private void EnsureVisible()
    {
        var y = GetItemY(_selectedIndex);
        var viewHeight = Game1.Instance.GraphicsDevice.Viewport.Height * 0.7f - 80f;
        if (y < _scrollOffset)
            _scrollOffset = y;
        else if (y + ItemHeight > _scrollOffset + viewHeight) _scrollOffset = y + ItemHeight - viewHeight;
    }

    private float GetItemY(int index)
    {
        float y = 0;
        string last = null;
        for (var i = 0; i <= index; i++)
        {
            if (_items[i].Category != last)
            {
                y += last == null ? SectionHeight : SectionHeight + 20f;
                last = _items[i].Category;
            }

            if (i < index) y += ItemHeight;
        }

        return y;
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

    private new void PlaySelect() => AccessTools.Method(typeof(LevelBase), "PlaySelect")?.Invoke(this, null);
    private new void PlayAccept() => AccessTools.Method(typeof(LevelBase), "PlayAccept")?.Invoke(this, null);
    private new void PlayCancel() => AccessTools.Method(typeof(LevelBase), "PlayCancel")?.Invoke(this, null);
    private new bool CanInput() => (bool)AccessTools.Method(typeof(LevelBase), "CanInput")?.Invoke(this, null)!;

    private class DevItem(string cat, string name, Func<object> getter, Action<object> setter, Action action = null)
    {
        public readonly string Category = cat;
        public readonly string Name = name;
        public readonly Action Action = action;

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

        public bool IsBool => getter?.Invoke() is bool;
        public bool IsLong => getter?.Invoke() is long;
        public bool IsInt => getter?.Invoke() is int;
        public bool IsFloat => getter?.Invoke() is float;
        public bool IsAction => Action != null;
    }
}