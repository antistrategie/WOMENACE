using Il2CppMenace.Tactical;

namespace WOMENACE.Code;

// Tile.GetActor casts the tile's entity to Actor natively and throws when the entity is a
// Structure (a bunker or other building). These read the entity safely instead.
public static class TileActors
{
    // The actor standing on the tile, or null when the tile holds a structure or nothing.
    public static Actor ActorOn(Tile tile) => tile?.GetEntity()?.TryCast<Actor>();

    // The actor on the tile, or the one sheltering inside the structure there: the unit an attack
    // or skill aimed at the tile actually reaches.
    public static Actor TargetOn(Tile tile)
    {
        var entity = tile?.GetEntity();
        return entity?.TryCast<Actor>() ?? entity?.GetContainedEntity()?.TryCast<Actor>();
    }
}
