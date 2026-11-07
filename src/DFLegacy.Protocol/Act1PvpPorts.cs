namespace DFLegacy.Protocol;

public static class Act1PvpPorts
{
    public const int Initial = 7004;
    public const int Intermediate = 7110;
    public const int Advanced = 7111;
    public const int Free = 7112;
    public static int[] Additional => [Intermediate, Advanced, Free];
}
