using BepInEx.Configuration;

namespace SaS2DevTools;

/// Holds the full set of cheat <see cref="ConfigEntry{T}"/> values for one player.
/// Two instances are created, one per player slot, using different config sections.
public class PlayerCheats(ConfigFile config, string section)
{
    public readonly ConfigEntry<bool> Godmode =
        config.Bind(section, "Godmode", false,
            "Player takes no damage.");

    public readonly ConfigEntry<bool> Invulnerable =
        config.Bind(section, "Invulnerable", false,
            "HP cannot decrease (blocks all damage at the source).");

    public readonly ConfigEntry<bool> InfStamina =
        config.Bind(section, "InfiniteStamina", false,
            "Stamina never depletes.");

    public readonly ConfigEntry<bool> InfPoise =
        config.Bind(section, "InfinitePoise", false,
            "Poise never breaks.");

    public readonly ConfigEntry<bool> Unstaggerable =
        config.Bind(section, "Unstaggerable", false,
            "Cannot be staggered or flung by hits.");

    public readonly ConfigEntry<bool> PlayJumpSnd =
        config.Bind(section, "PlayJumpSound", true,
            "Play jump sound.");

    public readonly ConfigEntry<bool> InfJumps =
        config.Bind(section, "InfiniteJumps", false,
            "Unlimited air jumps.");

    public readonly ConfigEntry<bool> IncreaseJumps =
        config.Bind(section, "IncreaseJumps", false,
            "Enable extra jumps.");

    public readonly ConfigEntry<int> ExtraJumps =
        config.Bind(section, "ExtraJumps", 0,
            "Increase jumps by amount.");

    public int ExtraJumpsUsed;

    public readonly ConfigEntry<bool> NoFallDmg =
        config.Bind(section, "NoFallDmg", false,
            "Cancels fall damage.");

    public readonly ConfigEntry<bool> NoClip =
        config.Bind(section, "NoClip", false,
            "Pass through walls. Hold Jump+Up/Down to fly, run keys move horizontally.");

    public readonly ConfigEntry<float> NoClipSpeed =
        config.Bind(section, "NoClipSpeed", 400f,
            "NoClip speed. Toggle 2x by rolling while in noclip");

    public readonly ConfigEntry<float> MovementSpeedMult =
        config.Bind(section, "MovementSpeedMult", 1f,
            "Horizontal movement speed multiplier (1.0 = default). Adjust in Dev Menu only.");
}