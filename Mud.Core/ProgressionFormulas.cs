namespace Mud.Core;

/// <summary>
/// Constants and calculations for player progression.
/// </summary>
public static class ProgressionFormulas
{
    public const int XpPerKill = 25;
    public const int PointsPerLevel = 5;
    public const int MaxLevel = 60;

    public const int BaseMeleeDamage = 5;
    public const int BaseRangedDamage = 5;
    public const int BaseHealth = 50;
    public const int HealthPerStamina = 10;

    public const int BaseStrength = 5;
    public const int BaseDexterity = 5;
    public const int BaseStamina = 5;

    /// <summary>
    /// XP required to reach a given level (cumulative threshold).
    /// Level 1 requires 0 XP, Level 2 requires 100 XP, Level 3 requires 400 XP, etc.
    /// </summary>
    public static int ExperienceForLevel(int level) => level <= 1 ? 0 : 100 * (level - 1) * (level - 1);

    /// <summary>
    /// Melee damage based on strength attribute.
    /// </summary>
    public static int MeleeDamage(int strength) => BaseMeleeDamage + strength;

    /// <summary>
    /// Ranged damage based on dexterity attribute.
    /// </summary>
    public static int RangedDamage(int dexterity) => BaseRangedDamage + dexterity;

    /// <summary>
    /// Maximum health based on stamina attribute.
    /// </summary>
    public static int MaxHealth(int stamina) => BaseHealth + (stamina * HealthPerStamina);
}
