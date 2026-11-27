"""The loose pieces of a prop's mesh and the "split" cuts that part them onto bones of their own.

build_team_states.py parts the prop with these cuts, and render_weapon.py's fold makes the same
cuts again on the prop's rest, so the two read one rule. Runs inside Blender.
"""


def pieces_of(mesh):
    """The mesh's loose pieces, as lists of vertex indices."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(mesh.data)
    seen, out = set(), []
    for start in bm.verts:
        if start.index in seen:
            continue
        piece, stack = [], [start]
        seen.add(start.index)
        while stack:
            v = stack.pop()
            piece.append(v.index)
            for e in v.link_edges:
                w = e.other_vert(v)
                if w.index not in seen:
                    seen.add(w.index)
                    stack.append(w)
        out.append(piece)
    bm.free()
    return out


def cut_takes(cut, points):
    """Whether a piece, its vertices at <points> in the prop's space at rest, is one the cut
    moves: wholly below "below" or above "above", or centred below "mid_below" or above
    "mid_above", along "axis" (Y by default), and reaching no higher than "y_below" on Y."""
    along = [p[cut.get("axis", 1)] for p in points]
    mid = sum(along) / len(along)
    if ("below" in cut and max(along) >= cut["below"]) or ("above" in cut and min(along) <= cut["above"]) \
            or ("mid_below" in cut and mid >= cut["mid_below"]) or ("mid_above" in cut and mid <= cut["mid_above"]):
        return False
    return not ("y_below" in cut and max(p.y for p in points) >= cut["y_below"])
