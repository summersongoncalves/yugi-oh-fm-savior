using YgoFm.Core;

namespace YgoFm.App;

/// <summary>Display-friendly wrapper around one <see cref="FusionFinder.FusionOption"/>.</summary>
public sealed class FusionRowView(FusionFinder.FusionOption option)
{
    public string CardA => option.MaterialA.Name;
    public string CardB => option.MaterialB.Name;
    public string Result => option.Result.Name;
}
