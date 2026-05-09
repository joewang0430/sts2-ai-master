namespace STS2.Agent.Sim;

/// <summary>
/// Compile-time integer indices for every concrete AfflictionModel subclass
/// in the game. Index 0 is the None sentinel stored on cards without an
/// affliction; real types start at 1.
/// </summary>
internal static class SimAfflictionType
{
    public const byte None       = 0;

    public const byte Bound      = 1;
    public const byte Devoured   = 2;
    public const byte Entangled  = 3;
    public const byte Galvanized = 4;
    public const byte Hexed      = 5;
    public const byte Ringing    = 6;
    public const byte Smog       = 7;
    public const byte Weighted   = 8;

    public const int Count = 9;
}