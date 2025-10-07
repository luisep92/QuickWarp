namespace QuickWarp.Domain;

public interface IWarpStorage
{
    bool TryLoad(out WarpData data);

    bool TrySave(WarpData data, out string error);
}
