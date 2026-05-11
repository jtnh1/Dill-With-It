public static class MatchSessionConfig
{
    public static MatchMode SelectedMatchMode { get; private set; } = MatchMode.Doubles;

    public static void SetMode(MatchMode mode)
    {
        SelectedMatchMode = mode;
    }

    public static MatchMode ToggleMode()
    {
        SelectedMatchMode = SelectedMatchMode == MatchMode.Doubles ? MatchMode.Singles : MatchMode.Doubles;
        return SelectedMatchMode;
    }
}
