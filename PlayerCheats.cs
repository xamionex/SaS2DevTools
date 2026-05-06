using BepInEx.Configuration;

namespace SaS2DevTools;

/// Holds the full set of cheat <see cref="ConfigEntry{T}"/> values for one player.
/// Two instances are created, one per player slot, using different config sections.
public class PlayerCheats(ConfigFile config, string section)
{
    public readonly ConfigEntry<bool> Godmode = config.Bind(section, "Godmode",         false, "Player takes no damage.");
    public readonly ConfigEntry<bool> Invulnerable = config.Bind(section, "Invulnerable",    false, "HP cannot decrease (blocks all damage at the source).");
    public readonly ConfigEntry<bool> InfStamina = config.Bind(section, "InfiniteStamina", false, "Stamina never depletes.");
    public readonly ConfigEntry<bool> InfPoise = config.Bind(section, "InfinitePoise",   false, "Poise never breaks.");
    public readonly ConfigEntry<bool> Unstaggerable = config.Bind(section, "Unstaggerable",   false, "Cannot be staggered.");
    public readonly ConfigEntry<bool> InfJumps = config.Bind(section, "InfiniteJumps",  false, "Unlimited air jumps. Also cancels fall damage.");
}