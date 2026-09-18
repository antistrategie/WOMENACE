using Jiangyu.Sdk;

namespace WOMENACE.Code;

[DevVerb]
public static class FrostEcho
{
    [MutatingVerb]
    public static object Give(int count = 1) => Weapons.Give("weapon.alva_ssr", count);
}
